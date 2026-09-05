using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Data;

namespace table_reservations.Tests;

public class TursoClientTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];
        public bool FailInsert { get; set; }
        public bool OmitBaton { get; set; }
        public bool CloseOnCommit { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("/v2/pipeline", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            Requests.Add(body);
            var entries = body.GetProperty("requests").EnumerateArray().Select(entry =>
            {
                if (entry.GetProperty("type").GetString() == "close")
                    return (object)new { type = "ok", response = new { type = "close" } };
                var sql = entry.GetProperty("stmt").GetProperty("sql").GetString()!;
                if (FailInsert && sql.StartsWith("INSERT"))
                    return new { type = "error", error = new { message = "constraint failed", code = "SQLITE_CONSTRAINT" } };
                return new { type = "ok", response = new { type = "execute", result = new {
                    cols = new[] { new { name = "number" }, new { name = "decimal" } },
                    rows = new object[][] { [new { type = "integer", value = "42" }, new { type = "float", value = 1.5 }] },
                    affected_row_count = 0, last_insert_rowid = (string?)null
                } } };
            }).ToArray();
            return new(HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(new {
                    baton = OmitBaton || (CloseOnCommit && body.GetProperty("requests")[0].GetProperty("stmt").GetProperty("sql").GetString() == "COMMIT")
                        ? null : $"baton-{Requests.Count}", base_url = "https://db.turso.io/", results = entries
                }), Encoding.UTF8, "application/json")
            };
        }
    }
    private static TursoClient Client(Handler handler) => new(new HttpClient(handler),
        Options.Create(new TursoOptions { DatabaseUrl = "libsql://db.turso.io", AuthToken = "local-test-token" }));
    private static string[] Sql(Handler handler) => handler.Requests.SelectMany(body => body.GetProperty("requests").EnumerateArray())
        .Where(entry => entry.GetProperty("type").GetString() == "execute")
        .Select(entry => entry.GetProperty("stmt").GetProperty("sql").GetString()!).ToArray();

    [Fact]
    public async Task EncodesParametersAndDecodesNumbers()
    {
        var handler = new Handler();
        var result = await Client(handler).QueryAsync("SELECT ?,?,?,?", [42, 1.5, true, null]);
        Assert.Equal(42, result.Rows[0].GetInt32("number"));
        Assert.Equal(1.5, result.Rows[0]["decimal"]);
        var args = handler.Requests[0].GetProperty("requests")[0].GetProperty("stmt").GetProperty("args");
        Assert.Equal("42", args[0].GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Number, args[1].GetProperty("value").ValueKind);
        Assert.Equal("1", args[2].GetProperty("value").GetString());
        Assert.Equal("null", args[3].GetProperty("type").GetString());
        Assert.Equal("close", handler.Requests[0].GetProperty("requests")[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task TransactionPassesRotatingBatonAndClosesAfterCommit()
    {
        var handler = new Handler();
        await Client(handler).TransactionAsync(async (db, ct) => { await db.ExecuteAsync("INSERT test", ct: ct); return true; });
        Assert.Equal(new[] { "BEGIN IMMEDIATE", "INSERT test", "COMMIT" }, Sql(handler));
        Assert.False(handler.Requests[0].TryGetProperty("baton", out _));
        for (int i = 1; i < handler.Requests.Count; i++)
            Assert.Equal($"baton-{i}", handler.Requests[i].GetProperty("baton").GetString());
        Assert.Equal("close", handler.Requests[^1].GetProperty("requests")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task SqlFailureRollsBackAndNeverCommits()
    {
        var handler = new Handler { FailInsert = true };
        var error = await Assert.ThrowsAsync<TursoException>(() => Client(handler).TransactionAsync(async (db, ct) => {
            await db.ExecuteAsync("INSERT test", ct: ct); return true;
        }));
        Assert.Equal("SQLITE_CONSTRAINT", error.Code);
        Assert.Equal(new[] { "BEGIN IMMEDIATE", "INSERT test", "ROLLBACK" }, Sql(handler));
    }

    [Fact]
    public async Task ApplicationValidationFailureRollsBack()
    {
        var handler = new Handler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).TransactionAsync<bool>((_, _) =>
            throw new InvalidOperationException("Occupied")));
        Assert.Equal(new[] { "BEGIN IMMEDIATE", "ROLLBACK" }, Sql(handler));
    }

    [Theory]
    [InlineData("libsql://db.turso.io", "https://db.turso.io/")]
    [InlineData("turso://db.turso.io/", "https://db.turso.io/")]
    [InlineData("db.turso.io", "https://db.turso.io/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void NormalizesEndpoint(string input, string expected) => Assert.Equal(expected, TursoClient.NormalizeUrl(input));

    [Fact]
    public async Task MissingBatonStopsBeforeAnyApplicationWrites()
    {
        var handler = new Handler { OmitBaton = true };
        await Assert.ThrowsAsync<TursoException>(() => Client(handler).TransactionAsync(async (db, ct) => {
            await db.ExecuteAsync("INSERT test", ct: ct); return true;
        }));
        Assert.DoesNotContain("INSERT test", Sql(handler));
        Assert.DoesNotContain("COMMIT", Sql(handler));
    }

    [Fact]
    public async Task SuccessfulCommitMayReturnNullBaton()
    {
        var handler = new Handler { CloseOnCommit = true };
        var result = await Client(handler).TransactionAsync(async (db, ct) => {
            await db.ExecuteAsync("INSERT test", ct: ct); return "saved";
        });
        Assert.Equal("saved", result);
        Assert.Equal(new[] { "BEGIN IMMEDIATE", "INSERT test", "COMMIT" }, Sql(handler));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public void RejectsUnencryptedRemoteEndpoint() =>
        Assert.Throws<InvalidOperationException>(() => TursoClient.NormalizeUrl("http://remote.example"));
}
