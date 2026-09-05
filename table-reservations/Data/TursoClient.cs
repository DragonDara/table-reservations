using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;

namespace table_reservations.Data;

public sealed record TursoStatement(string Sql, IReadOnlyList<object?>? Args = null);
public interface ITursoClient
{
    Task<TursoResultSet> QueryAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default);
    Task<long> ExecuteAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default);
    Task ExecuteBatchAsync(IReadOnlyList<string> statements, CancellationToken ct = default);
    Task<IReadOnlyList<TursoResultSet>> QueryBatchAsync(IReadOnlyList<TursoStatement> statements, CancellationToken ct = default);
    Task<T> TransactionAsync<T>(Func<ITursoClient, CancellationToken, Task<T>> action, CancellationToken ct = default);
}

public sealed class TursoRow
{
    private readonly IReadOnlyDictionary<string, object?> _values;
    public TursoRow(IReadOnlyDictionary<string, object?> values) => _values = values;
    public object? this[string column] => _values.TryGetValue(column, out var value) ? value : null;
    public string GetString(string column) => Convert.ToString(this[column], CultureInfo.InvariantCulture) ?? "";
    public long GetInt64(string column) => long.TryParse(GetString(column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    public int GetInt32(string column) => checked((int)GetInt64(column));
    public bool GetBoolean(string column) => GetInt64(column) != 0;
}
public sealed class TursoResultSet
{
    public IReadOnlyList<TursoRow> Rows { get; init; } = Array.Empty<TursoRow>();
    public long LastInsertRowId { get; init; }
    public long AffectedRowCount { get; init; }
}
public sealed class TursoException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

/// <summary>Each transaction owns its own HTTP baton; request-scoped callers never share a transaction.</summary>
public sealed class TursoClient : ITursoClient
{
    private readonly HttpClient _http;
    private readonly bool _transaction;
    private string? _baton;
    private Uri? _sessionBase;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TursoClient(HttpClient http, IOptions<TursoOptions> options)
    {
        var settings = options.Value;
        if (!settings.IsConfigured) throw new InvalidOperationException("Configure Turso:DatabaseUrl and Turso:AuthToken.");
        _http = http;
        _http.BaseAddress = new Uri(NormalizeUrl(settings.ConnectionUrl));
        _http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 30);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AuthToken.Trim());
    }
    private TursoClient(HttpClient http) { _http = http; _transaction = true; }

    public static string NormalizeUrl(string url)
    {
        var value = url.Trim().TrimEnd('/');
        if (value.StartsWith("libsql://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value[9..];
        if (value.StartsWith("turso://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value[8..];
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        var uri = new Uri(value + "/");
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new InvalidOperationException("Turso requires HTTPS (HTTP is allowed for local tests only).");
        return uri.AbsoluteUri;
    }

    public async Task<TursoResultSet> QueryAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default) =>
        (await QueryBatchAsync([new(sql, args)], ct))[0];
    public async Task<long> ExecuteAsync(string sql, IReadOnlyList<object?>? args = null, CancellationToken ct = default) =>
        (await QueryAsync(sql, args, ct)).LastInsertRowId;
    public async Task ExecuteBatchAsync(IReadOnlyList<string> statements, CancellationToken ct = default) =>
        await QueryBatchAsync(statements.Select(sql => new TursoStatement(sql)).ToArray(), ct);

    public async Task<T> TransactionAsync<T>(Func<ITursoClient, CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        if (_transaction) throw new InvalidOperationException("Nested transactions are not supported.");
        var session = new TursoClient(_http);
        try
        {
            await session.QueryAsync("BEGIN IMMEDIATE", ct: ct);
            var result = await action(session, ct);
            await session.QueryAsync("COMMIT", ct: ct);
            return result;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { if (session._baton is not null) await session.QueryAsync("ROLLBACK", ct: cleanup.Token); } catch { /* Preserve original failure. */ }
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { if (session._baton is not null) await session.SendAsync([], true, cleanup.Token); } catch { /* Server also expires abandoned connections. */ }
        }
    }

    public Task<IReadOnlyList<TursoResultSet>> QueryBatchAsync(IReadOnlyList<TursoStatement> statements, CancellationToken ct = default) =>
        SendAsync(statements, !_transaction, ct);

    private async Task<IReadOnlyList<TursoResultSet>> SendAsync(IReadOnlyList<TursoStatement> statements, bool close, CancellationToken ct)
    {
        var requests = statements.Select(statement => (object)new
        {
            type = "execute",
            stmt = new { sql = statement.Sql, args = (statement.Args ?? []).Select(ToValue).ToArray() }
        }).ToList();
        if (close) requests.Add(new { type = "close" });
        using var response = await _http.PostAsJsonAsync(
            new Uri(_sessionBase ?? _http.BaseAddress!, "v2/pipeline"),
            new { baton = _baton, requests }, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
            throw new TursoException($"Turso returned HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        _baton = root.TryGetProperty("baton", out var baton) && baton.ValueKind == JsonValueKind.String ? baton.GetString() : null;
        if (root.TryGetProperty("base_url", out var address) && address.ValueKind == JsonValueKind.String)
        {
            var candidate = new Uri(NormalizeUrl(address.GetString()!));
            if (candidate.Host != _http.BaseAddress!.Host && !candidate.Host.EndsWith(".turso.io", StringComparison.OrdinalIgnoreCase))
                throw new TursoException("Unexpected transaction host.");
            _sessionBase = candidate;
        }
        var results = root.GetProperty("results");
        if (results.GetArrayLength() != requests.Count) throw new TursoException("Incomplete Turso response.");
        var sets = new List<TursoResultSet>();
        for (var index = 0; index < results.GetArrayLength(); index++)
        {
            var entry = results[index];
            if (entry.GetProperty("type").GetString() != "ok")
            {
                var error = entry.GetProperty("error");
                throw new TursoException(error.GetProperty("message").GetString() ?? "SQL failed",
                    error.TryGetProperty("code", out var code) ? code.GetString() : null);
            }
            if (index >= statements.Count) continue;
            var result = entry.GetProperty("response").GetProperty("result");
            var columns = result.GetProperty("cols").EnumerateArray().Select(c => c.GetProperty("name").GetString() ?? "").ToArray();
            var rows = result.GetProperty("rows").EnumerateArray().Select(row =>
            {
                var cells = row.EnumerateArray().ToArray();
                return new TursoRow(columns.Select((column, i) => (column, value: FromValue(cells[i])))
                    .ToDictionary(item => item.column, item => item.value, StringComparer.OrdinalIgnoreCase));
            }).ToArray();
            var id = result.GetProperty("last_insert_rowid");
            sets.Add(new TursoResultSet
            {
                Rows = rows,
                AffectedRowCount = result.GetProperty("affected_row_count").GetInt64(),
                LastInsertRowId = id.ValueKind == JsonValueKind.Null ? 0 : long.Parse(id.ToString(), CultureInfo.InvariantCulture)
            });
        }
        // Turso can close the stream after a successful COMMIT/ROLLBACK.
        var endedTransaction = statements.Count == 1 && statements[0].Sql is "COMMIT" or "ROLLBACK";
        if (_transaction && !close && !endedTransaction && string.IsNullOrWhiteSpace(_baton))
            throw new TursoException("Transaction connection was lost.");
        return sets;
    }

    private static object ToValue(object? value) => value switch
    {
        null => new { type = "null" },
        bool boolean => new { type = "integer", value = boolean ? "1" : "0" },
        int or long => new { type = "integer", value = Convert.ToString(value, CultureInfo.InvariantCulture) },
        double or float or decimal => new { type = "float", value = Convert.ToDouble(value, CultureInfo.InvariantCulture) },
        byte[] bytes => new { type = "blob", base64 = Convert.ToBase64String(bytes) },
        _ => new { type = "text", value = Convert.ToString(value, CultureInfo.InvariantCulture) }
    };
    private static object? FromValue(JsonElement cell) => cell.GetProperty("type").GetString() switch
    {
        "null" => null,
        "integer" => long.Parse(cell.GetProperty("value").GetString()!, CultureInfo.InvariantCulture),
        "float" => cell.GetProperty("value").GetDouble(),
        "blob" => Convert.FromBase64String(cell.GetProperty("base64").GetString()!),
        _ => cell.GetProperty("value").GetString()
    };
}
