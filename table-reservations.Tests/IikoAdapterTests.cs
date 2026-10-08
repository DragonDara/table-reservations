using System.Net;
using System.Text.Json;
using table_reservations.Models;
using table_reservations.Pos;

namespace table_reservations.Tests;

public class IikoAdapterTests
{
    private const string Organization = "11111111-1111-1111-1111-111111111111";
    private const string Terminal = "22222222-2222-2222-2222-222222222222";
    private const string Reservation = "33333333-3333-3333-3333-333333333333";
    private const string AppId = "44444444-4444-4444-4444-444444444444";

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }

    [Fact]
    public async Task AuthenticationUsesV2CredentialsAndBearerTokenForSubsequentCalls()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(async request => {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            Assert.Equal(HttpMethod.Post, request.Method);
            if (path == "/api/v2/access_token")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var root = body.RootElement;
                Assert.Equal(3, root.EnumerateObject().Count());
                Assert.Equal(AppId, root.GetProperty("appId").GetString());
                Assert.Equal("test-secret", root.GetProperty("clientSecret").GetString());
                Assert.Equal("test-key", root.GetProperty("apiKey").GetString());
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"correlationId\":\"command\",\"token\":\"test-token\"}") };
            }
            Assert.Equal("/api/1/reserve/available_restaurant_sections", path);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"restaurantSections\":[]}") };
        })) { BaseAddress = new("https://example.invalid/") };
        var adapter = new AuthenticatingPosAdapter(new IikoAdapter(http, AppId, "test-secret", "test-key", Organization, Terminal));
        await adapter.GetTablesAsync();
        await adapter.GetTablesAsync();
        Assert.Equal(new[] { "/api/v2/access_token", "/api/1/reserve/available_restaurant_sections",
            "/api/1/reserve/available_restaurant_sections" }, paths);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":\"\"}")]
    [InlineData("{\"token\":\" \"}")]
    public async Task AuthenticationRejectsMissingOrEmptyTokens(string response)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(response)
        }))) { BaseAddress = new("https://example.invalid/") };
        var adapter = new IikoAdapter(http, AppId, "test-secret", "test-key", Organization, Terminal);
        await Assert.ThrowsAsync<JsonException>(() => adapter.AuthenticateAsync());
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task CreateUsesDurableIdConfiguredTerminalAndFullDuration()
    {
        using var http = new HttpClient(new Handler(async request => {
            Assert.Equal("/api/1/reserve/create", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.Equal(Terminal, root.GetProperty("terminalGroupId").GetString());
            Assert.Equal(Reservation, root.GetProperty("id").GetString());
            Assert.Equal(180, root.GetProperty("durationInMinutes").GetInt32());
            Assert.Equal("regular", root.GetProperty("customer").GetProperty("type").GetString());
            Assert.Equal("2026-10-09 18:00:00.000", root.GetProperty("estimatedStartTime").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent($$$"""
                {"correlationId":"command","reserveInfo":{"id":"{{{Reservation}}}","organizationId":"{{{Organization}}}","creationStatus":"InProgress"}}
                """) };
        })) { BaseAddress = new("https://example.invalid/") };
        var adapter = new IikoAdapter(http, AppId, "test-secret", "test-key", Organization, Terminal);
        var result = await adapter.ReserveTableAsync(new(Organization, new("Guest", "+77010000001"),
            new DateTime(2026, 10, 9, 18, 0, 0), 4, ["table"], 180, Reservation));
        Assert.False(result.Success);
        Assert.Equal("Processing", result.State);
        Assert.Equal("command", result.CorrelationId);
    }

    [Theory]
    [InlineData("{\"isAliveStatus\":[]}")]
    [InlineData("{}")]
    public async Task UnverifiedTerminalNeverProducesFreeSnapshot(string response)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(response)
        }))) { BaseAddress = new("https://example.invalid/") };
        var adapter = new IikoAdapter(http, AppId, "test-secret", "test-key", Organization, Terminal);
        await Assert.ThrowsAnyAsync<Exception>(() => adapter.GetAvailabilityAsync(DateTime.Today, DateTime.Today.AddDays(1)));
    }

    [Theory]
    [InlineData("New", true)]
    [InlineData("Bill", true)]
    [InlineData("Closed", false)]
    [InlineData("Deleted", false)]
    public async Task AvailabilityWaitsForInitializationResponseWithoutPollingCommandStatus(string status, bool occupied)
    {
        var paths = new List<string>();
        var initializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializationResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async request => {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            Assert.Equal(HttpMethod.Post, request.Method);
            if (path == "/api/1/order/init_by_table")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(Organization, body.RootElement.GetProperty("organizationId").GetString());
                Assert.Equal(Terminal, body.RootElement.GetProperty("terminalGroupId").GetString());
                Assert.Equal("table", Assert.Single(body.RootElement.GetProperty("tableIds").EnumerateArray()).GetString());
                initializationStarted.SetResult();
                return await initializationResponse.Task;
            }
            if (path == "/api/1/order/by_table")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(Organization, Assert.Single(body.RootElement.GetProperty("organizationIds").EnumerateArray()).GetString());
                Assert.Equal("table", Assert.Single(body.RootElement.GetProperty("tableIds").EnumerateArray()).GetString());
                Assert.Equal(new[] { "New", "Bill" }, body.RootElement.GetProperty("statuses").EnumerateArray().Select(item => item.GetString()));
            }
            return new(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(AvailabilityResponse(path, status)) };
        })) { BaseAddress = new("https://example.invalid/") };
        var adapter = new IikoAdapter(http, AppId, "test-secret", "test-key", Organization, Terminal);
        var requestTask = adapter.GetAvailabilityAsync(DateTime.Today, DateTime.Today.AddDays(1));
        await initializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False(requestTask.IsCompleted);
            Assert.DoesNotContain("/api/1/order/by_table", paths);
        }
        finally
        {
            initializationResponse.SetResult(new(HttpStatusCode.OK) {
                Content = System.Net.Http.Json.JsonContent.Create(AvailabilityResponse("/api/1/order/init_by_table"))
            });
        }
        var snapshot = await requestTask;
        Assert.Equal(occupied, snapshot.OccupiedTableIds.Contains("table"));
        Assert.Single(snapshot.Tables);
        Assert.Equal(Reservation, Assert.Single(snapshot.Reservations).ReservationId);
        Assert.Equal(new[] { "/api/1/terminal_groups/is_alive", "/api/1/reserve/available_restaurant_sections",
            "/api/1/order/init_by_table", "/api/1/order/by_table", "/api/1/reserve/restaurant_sections_workload" }, paths);
    }

    [Theory]
    [InlineData("/api/1/order/init_by_table", HttpStatusCode.Gone)]
    [InlineData("/api/1/order/init_by_table", HttpStatusCode.ServiceUnavailable)]
    [InlineData("/api/1/order/by_table", HttpStatusCode.Gone)]
    [InlineData("/api/1/order/by_table", HttpStatusCode.ServiceUnavailable)]
    [InlineData("/api/1/reserve/restaurant_sections_workload", HttpStatusCode.ServiceUnavailable)]
    public async Task FailedAvailabilityRequestNeverProducesFreeSnapshot(string endpoint, HttpStatusCode status)
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request => {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            return Task.FromResult(path == endpoint ? new HttpResponseMessage(status)
                : new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = System.Net.Http.Json.JsonContent.Create(AvailabilityResponse(path))
                });
        })) { BaseAddress = new("https://example.invalid/") };
        var adapter = new IikoAdapter(http, AppId, "test-secret", "test-key", Organization, Terminal);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => adapter.GetAvailabilityAsync(DateTime.Today, DateTime.Today.AddDays(1)));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(endpoint.TrimStart('/'), error.Data["IikoEndpoint"]);
        Assert.Equal(endpoint, paths.Last());
        Assert.Single(paths, path => path == endpoint);
        Assert.DoesNotContain("/api/1/commands/status", paths);
    }

    [Theory]
    [InlineData("/api/1/order/init_by_table", "{}")]
    [InlineData("/api/1/order/init_by_table", "{\"correlationId\":\" \"}")]
    [InlineData("/api/1/order/by_table", "{}")]
    [InlineData("/api/1/order/by_table", "{\"orders\":null}")]
    [InlineData("/api/1/order/by_table", "{\"orders\":[null]}")]
    [InlineData("/api/1/order/by_table", """{"orders":[{"id":"order","organizationId":"11111111-1111-1111-1111-111111111111","creationStatus":"InProgress"}]}""")]
    public async Task IncompleteOrderResponsesNeverProduceFreeSnapshot(string endpoint, string response)
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request => {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = path == endpoint ? new StringContent(response)
                    : System.Net.Http.Json.JsonContent.Create(AvailabilityResponse(path))
            });
        })) { BaseAddress = new("https://example.invalid/") };
        var adapter = new IikoAdapter(http, AppId, "test-secret", "test-key", Organization, Terminal);
        await Assert.ThrowsAsync<JsonException>(() => adapter.GetAvailabilityAsync(DateTime.Today, DateTime.Today.AddDays(1)));
        Assert.Equal(endpoint, paths.Last());
        Assert.DoesNotContain("/api/1/commands/status", paths);
        Assert.DoesNotContain("/api/1/reserve/restaurant_sections_workload", paths);
    }

    private static object AvailabilityResponse(string path, string status = "Bill") => path switch {
        "/api/1/terminal_groups/is_alive" => new { isAliveStatus = new[] { new { terminalGroupId = Terminal, organizationId = Organization, isAlive = true } } },
        "/api/1/reserve/available_restaurant_sections" => new { restaurantSections = new[] { new { id = "section", terminalGroupId = Terminal, name = "Hall", tables = new[] { new { id = "table", number = 1, name = "One", seatingCapacity = 4, isDeleted = false } } } } },
        "/api/1/order/init_by_table" => new { correlationId = "55555555-5555-5555-5555-555555555555" },
        "/api/1/order/by_table" => new { orders = new[] { new { id = "order", organizationId = Organization, creationStatus = "Success", order = new { status, tableIds = new[] { "table" } } } } },
        "/api/1/reserve/restaurant_sections_workload" => new { reserves = new[] { new { id = Reservation, tableIds = new[] { "table" }, estimatedStartTime = "2026-10-09 18:00:00.000", durationInMinutes = 180, guestsCount = 4 } } },
        _ => throw new InvalidOperationException(path)
    };
}