using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;

namespace table_reservations.Data
{
    /// <summary>
    /// Minimal client for the Turso / libSQL HTTP API (<c>POST /v2/pipeline</c>).
    /// No ADO.NET-compatible libSQL provider exists for net10.0, so this is the
    /// single place that speaks the wire protocol. All SQL is parameterized.
    /// </summary>
    public interface ITursoClient
    {
        Task<TursoResultSet> QueryAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default);

        Task<long> ExecuteAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default);

        Task ExecuteBatchAsync(IReadOnlyList<string> statements, CancellationToken ct = default);
    }

    /// <summary>A single row of a Turso result set, addressable by column name.</summary>
    public sealed class TursoRow
    {
        private readonly IReadOnlyDictionary<string, int> _ordinals;
        private readonly object?[] _values;

        internal TursoRow(IReadOnlyDictionary<string, int> ordinals, object?[] values)
        {
            _ordinals = ordinals;
            _values = values;
        }

        public object? this[string column] =>
            _ordinals.TryGetValue(column, out var ordinal) ? _values[ordinal] : null;

        public string GetString(string column) =>
            this[column] switch
            {
                null => string.Empty,
                string s => s,
                var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty
            };

        public long GetInt64(string column) =>
            this[column] switch
            {
                null => 0L,
                long l => l,
                double d => (long)d,
                string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0L
            };

        public int GetInt32(string column) => (int)GetInt64(column);

        public bool GetBoolean(string column) => GetInt64(column) != 0;
    }

    public sealed class TursoResultSet
    {
        public IReadOnlyList<TursoRow> Rows { get; init; } = Array.Empty<TursoRow>();

        public long LastInsertRowId { get; init; }

        public long AffectedRowCount { get; init; }
    }

    public sealed class TursoClient : ITursoClient
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _http;

        public TursoClient(HttpClient http, IOptions<TursoOptions> options)
        {
            var settings = options.Value;
            if (!settings.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Turso is not configured. Set Turso:Url and Turso:AuthToken (Turso__Url / Turso__AuthToken).");
            }

            _http = http;
            _http.BaseAddress = new Uri(NormalizeUrl(settings.Url));
            _http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds <= 0 ? 30 : settings.TimeoutSeconds);
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.AuthToken);
        }

        internal static string NormalizeUrl(string url)
        {
            var trimmed = url.Trim().TrimEnd('/');
            if (trimmed.StartsWith("libsql://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = string.Concat("https://", trimmed.AsSpan("libsql://".Length));
            }
            else if (!trimmed.Contains("://", StringComparison.Ordinal))
            {
                trimmed = "https://" + trimmed;
            }

            return trimmed + "/";
        }

        public async Task<TursoResultSet> QueryAsync(
            string sql,
            IReadOnlyList<object?>? args = null,
            CancellationToken ct = default)
        {
            var results = await SendAsync(new[] { (sql, args) }, ct);
            return results[0];
        }

        public async Task<long> ExecuteAsync(
            string sql,
            IReadOnlyList<object?>? args = null,
            CancellationToken ct = default)
        {
            var result = await QueryAsync(sql, args, ct);
            return result.LastInsertRowId;
        }

        public async Task ExecuteBatchAsync(IReadOnlyList<string> statements, CancellationToken ct = default)
        {
            if (statements.Count == 0)
            {
                return;
            }

            await SendAsync(
                statements.Select(s => (s, (IReadOnlyList<object?>?)null)).ToArray(),
                ct);
        }

        private async Task<IReadOnlyList<TursoResultSet>> SendAsync(
            IReadOnlyList<(string Sql, IReadOnlyList<object?>? Args)> statements,
            CancellationToken ct)
        {
            var requests = new List<PipelineRequest>(statements.Count + 1);
            foreach (var (sql, args) in statements)
            {
                requests.Add(new PipelineRequest
                {
                    Type = "execute",
                    Stmt = new PipelineStatement
                    {
                        Sql = sql,
                        Args = (args ?? Array.Empty<object?>()).Select(ToValue).ToArray()
                    }
                });
            }

            requests.Add(new PipelineRequest { Type = "close" });

            using var response = await _http.PostAsJsonAsync(
                "v2/pipeline",
                new PipelineBody { Requests = requests },
                SerializerOptions,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"Turso request failed with {(int)response.StatusCode}: {body}");
            }

            var payload = await response.Content.ReadFromJsonAsync<PipelineResponse>(SerializerOptions, ct)
                          ?? throw new InvalidOperationException("Turso returned an empty response.");

            var sets = new List<TursoResultSet>(statements.Count);
            for (var i = 0; i < payload.Results.Count && sets.Count < statements.Count; i++)
            {
                var entry = payload.Results[i];
                if (!string.Equals(entry.Type, "ok", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Turso statement failed: {entry.Error?.Message ?? "unknown error"}. SQL: {statements[i].Sql}");
                }

                sets.Add(Materialize(entry.Response?.Result));
            }

            return sets;
        }

        private static TursoResultSet Materialize(StatementResult? result)
        {
            if (result is null)
            {
                return new TursoResultSet();
            }

            var ordinals = new Dictionary<string, int>(result.Cols.Count, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < result.Cols.Count; i++)
            {
                var name = result.Cols[i].Name;
                if (!string.IsNullOrEmpty(name))
                {
                    ordinals[name] = i;
                }
            }

            var rows = new List<TursoRow>(result.Rows.Count);
            foreach (var raw in result.Rows)
            {
                var values = new object?[result.Cols.Count];
                for (var i = 0; i < values.Length && i < raw.Count; i++)
                {
                    values[i] = FromValue(raw[i]);
                }

                rows.Add(new TursoRow(ordinals, values));
            }

            return new TursoResultSet
            {
                Rows = rows,
                LastInsertRowId = ParseLong(result.LastInsertRowId),
                AffectedRowCount = result.AffectedRowCount
            };
        }

        private static long ParseLong(string? value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;

        private static TursoValue ToValue(object? value) =>
            value switch
            {
                null => new TursoValue { Type = "null" },
                bool b => new TursoValue { Type = "integer", Value = b ? "1" : "0" },
                int i => new TursoValue { Type = "integer", Value = i.ToString(CultureInfo.InvariantCulture) },
                long l => new TursoValue { Type = "integer", Value = l.ToString(CultureInfo.InvariantCulture) },
                double d => new TursoValue { Type = "float", Value = d.ToString("R", CultureInfo.InvariantCulture) },
                decimal m => new TursoValue { Type = "float", Value = m.ToString(CultureInfo.InvariantCulture) },
                string s => new TursoValue { Type = "text", Value = s },
                _ => new TursoValue { Type = "text", Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty }
            };

        private static object? FromValue(TursoValue? value)
        {
            if (value is null || string.Equals(value.Type, "null", StringComparison.Ordinal))
            {
                return null;
            }

            return value.Type switch
            {
                "integer" => ParseLong(value.Value),
                "float" => double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d,
                _ => value.Value
            };
        }

        private sealed class PipelineBody
        {
            [JsonPropertyName("requests")]
            public List<PipelineRequest> Requests { get; set; } = new();
        }

        private sealed class PipelineRequest
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("stmt")]
            public PipelineStatement? Stmt { get; set; }
        }

        private sealed class PipelineStatement
        {
            [JsonPropertyName("sql")]
            public string Sql { get; set; } = string.Empty;

            [JsonPropertyName("args")]
            public TursoValue[] Args { get; set; } = Array.Empty<TursoValue>();
        }

        private sealed class PipelineResponse
        {
            [JsonPropertyName("results")]
            public List<PipelineResultEntry> Results { get; set; } = new();
        }

        private sealed class PipelineResultEntry
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("response")]
            public StatementResponse? Response { get; set; }

            [JsonPropertyName("error")]
            public PipelineError? Error { get; set; }
        }

        private sealed class PipelineError
        {
            [JsonPropertyName("message")]
            public string? Message { get; set; }
        }

        private sealed class StatementResponse
        {
            [JsonPropertyName("result")]
            public StatementResult? Result { get; set; }
        }

        private sealed class StatementResult
        {
            [JsonPropertyName("cols")]
            public List<ColumnDescriptor> Cols { get; set; } = new();

            [JsonPropertyName("rows")]
            public List<List<TursoValue>> Rows { get; set; } = new();

            [JsonPropertyName("affected_row_count")]
            public long AffectedRowCount { get; set; }

            [JsonPropertyName("last_insert_rowid")]
            public string? LastInsertRowId { get; set; }
        }

        private sealed class ColumnDescriptor
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }
    }

    internal sealed class TursoValue
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "null";

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }
}
