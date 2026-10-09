using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using table_reservations.Constants;
using table_reservations.Models;

namespace table_reservations.Pos;

public class IikoAdapter(HttpClient http, string appId, string clientSecret, string apiKey,
    string organizationId, string? terminalGroupId = null)
    : ApiClient(http), IPosAdapter
{
    public string ProviderName => "iiko";
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss.fff";

    private async Task<JsonNode> SendAsync(string path, object payload, CancellationToken ct)
    {
        using var response = await Http.PostAsJsonAsync(path, payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = new HttpRequestException($"iiko POST /{path} returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                null, response.StatusCode);
            error.Data["IikoEndpoint"] = path;
            throw error;
        }
        return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct)
            ?? throw new JsonException("Empty iiko response.");
    }

    private static string Text(JsonNode node, string property) =>
        node[property]?.GetValue<string>() ?? throw new JsonException($"Missing iiko field: {property}.");

    private static JsonArray Array(JsonNode node, string property) =>
        node[property] as JsonArray ?? throw new JsonException($"Missing iiko array: {property}.");

    private string TerminalGroup => Guid.TryParse(terminalGroupId, out var id) && id != Guid.Empty
        ? id.ToString() : throw new InvalidOperationException("An iiko terminal group must be configured.");

    public async Task AuthenticateAsync(CancellationToken ct = default)
    {
        var data = await SendAsync("api/v2/access_token", new { appId, clientSecret, apiKey }, ct);
        var token = Text(data, "token");
        if (string.IsNullOrWhiteSpace(token)) throw new JsonException("Empty iiko token.");
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<JsonArray> SectionsAsync(CancellationToken ct)
    {
        var data = await SendAsync("api/1/reserve/available_restaurant_sections",
            new { terminalGroupIds = new[] { TerminalGroup }, returnSchema = false }, ct);
        var sections = Array(data, "restaurantSections");
        if (sections.Any(section => section is null || Text(section, "terminalGroupId") != TerminalGroup))
            throw new JsonException("Unexpected iiko terminal group.");
        return sections;
    }

    private static List<PosTable> ReadTables(JsonArray sections) => sections.SelectMany(section =>
        Array(section!, "tables").Select(table => table ?? throw new JsonException("Invalid iiko table."))
            .Where(table => table["isDeleted"]?.GetValue<bool>() == false)
            .Select(table => new PosTable(Text(table, "id"), table["name"]?.GetValue<string>() ?? "",
                table["seatingCapacity"]!.GetValue<int>(), true, table["number"]!.GetValue<int>(), Text(section!, "name"))))
        .ToList();

    public async Task<List<PosTable>> GetTablesAsync(CancellationToken ct = default) => ReadTables(await SectionsAsync(ct));

    private async Task EnsureOnlineAsync(CancellationToken ct)
    {
        var data = await SendAsync("api/1/terminal_groups/is_alive",
            new { organizationIds = new[] { organizationId }, terminalGroupIds = new[] { TerminalGroup } }, ct);
        var terminal = Array(data, "isAliveStatus").SingleOrDefault(item => item?["terminalGroupId"]?.GetValue<string>() == TerminalGroup
            && item?["organizationId"]?.GetValue<string>() == organizationId);
        if (terminal?["isAlive"]?.GetValue<bool>() != true)
            throw new HttpRequestException("iiko terminal is unavailable.");
    }

    private async Task<List<ActiveOrderDto>> OrdersAsync(IReadOnlyList<string> tableIds, CancellationToken ct)
    {
        var data = await SendAsync("api/1/order/by_table", new {
            organizationIds = new[] { organizationId }, tableIds,
            statuses = new[] { "New", "Bill" }
        }, ct);
        var result = new List<ActiveOrderDto>();
        foreach (var info in Array(data, "orders"))
        {
            if (info is null || Text(info, "organizationId") != organizationId || Text(info, "creationStatus") != "Success"
                || info["errorInfo"] is not null || info["order"] is not JsonNode order)
                throw new JsonException("Incomplete iiko order state.");
            var status = Text(order, "status");
            if (status is "Closed" or "Deleted") continue;
            if (status is not ("New" or "Bill")) throw new JsonException("Unknown iiko order status.");
            result.Add(new(Text(info, "id"), status, Array(order, "tableIds").Select(item => item!.GetValue<string>()).ToList(), 0));
        }
        return result;
    }

    public async Task<List<ActiveOrderDto>> GetActiveOrdersAsync(CancellationToken ct = default)
    {
        await EnsureOnlineAsync(ct);
        return await OrdersAsync((await GetTablesAsync(ct)).Select(table => table.Id).ToArray(), ct);
    }

    private async Task<List<ReservationInfoDto>> WorkloadAsync(JsonArray sections, DateTime from, DateTime until, CancellationToken ct)
    {
        var data = await SendAsync("api/1/reserve/restaurant_sections_workload", new {
            restaurantSectionIds = sections.Select(section => Text(section!, "id")).ToArray(),
            dateFrom = from.ToString(DateFormat, CultureInfo.InvariantCulture),
            dateTo = until.ToString(DateFormat, CultureInfo.InvariantCulture)
        }, ct);
        return Array(data, "reserves").Select(item => {
            if (item is null) throw new JsonException("Invalid iiko reserve.");
            return new ReservationInfoDto(Text(item, "id"), Array(item, "tableIds").Select(id => id!.GetValue<string>()).ToList(), "", "",
                DateTime.ParseExact(Text(item, "estimatedStartTime"), DateFormat, CultureInfo.InvariantCulture),
                item["durationInMinutes"]!.GetValue<int>(), item["guestsCount"]!.GetValue<int>());
        }).ToList();
    }

    public async Task<List<ReservationInfoDto>> GetReservationsAsync(CancellationToken ct = default)
    {
        var today = ReservationDateTime.KazakhstanNow().Date;
        return await WorkloadAsync(await SectionsAsync(ct), today.AddDays(-1), today.AddDays(2), ct);
    }

    public async Task<PosAvailabilitySnapshot> GetAvailabilityAsync(IReadOnlyList<string> tableIds, CancellationToken ct = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        if (tableIds.Count == 0) throw new JsonException("No iiko tables are configured.");
        var orders = await OrdersAsync(tableIds, ct);
        return new(checkedAt, orders.SelectMany(order => order.TableIds).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static ReservationResult ReadReserve(JsonNode info, string? correlationId)
    {
        var id = Text(info, "id");
        var creationStatus = Text(info, "creationStatus");
        if (creationStatus == "Error") return new(false, id, "POS_REJECTED", "Rejected", correlationId);
        if (creationStatus == "InProgress") return new(false, id, null, "Processing", correlationId);
        if (creationStatus != "Success" || info["errorInfo"] is not null)
            throw new JsonException("Unknown iiko creation state.");
        if (info["isDeleted"] is null) throw new JsonException("Missing iiko deletion state.");
        if (info["isDeleted"]!.GetValue<bool>()) return new(false, id, null, "Cancelled", correlationId);
        var reserve = info["reserve"] ?? throw new JsonException("Missing iiko reserve.");
        if (reserve["cancelReason"] is not null) return new(false, id, null, "Cancelled", correlationId);
        var details = new ReservationInfoDto(id, Array(reserve, "tableIds").Select(table => table!.GetValue<string>()).ToList(), "", "",
            DateTime.ParseExact(Text(reserve, "estimatedStartTime"), DateFormat, CultureInfo.InvariantCulture),
            reserve["durationInMinutes"]!.GetValue<int>(), reserve["guestsCount"]!.GetValue<int>());
        return Text(reserve, "status") switch {
            "New" or "Started" => new(true, id, null, "Confirmed", correlationId, details),
            "Closed" => new(false, id, null, "Closed", correlationId),
            _ => throw new JsonException("Unknown iiko reserve status.")
        };
    }

    public async Task<ReservationResult> ReserveTableAsync(CreateReservationRequest request, CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.ReservationId, out var id) || id == Guid.Empty)
            throw new ArgumentException("A durable reservation ID is required.");
        var data = await SendAsync("api/1/reserve/create", new {
            organizationId, terminalGroupId = TerminalGroup, id,
            customer = new { type = "regular", name = request.Customer.Name }, phone = request.Customer.Phone,
            tableIds = request.TableIds, guestsCount = request.GuestsCount, shouldRemind = false,
            durationInMinutes = request.DurationInMinutes,
            estimatedStartTime = request.ReservationTime.ToString(DateFormat, CultureInfo.InvariantCulture)
        }, ct);
        var info = data["reserveInfo"] ?? throw new JsonException("Missing reserveInfo.");
        if (Text(info, "id") != id.ToString() || Text(info, "organizationId") != organizationId)
            throw new JsonException("Unexpected iiko reservation identity.");
        return ReadReserve(info, Text(data, "correlationId"));
    }

    public async Task<ReservationResult> GetReservationAsync(string externalReservationId, CancellationToken ct = default)
    {
        var data = await SendAsync("api/1/reserve/status_by_id", new { organizationId, reserveIds = new[] { externalReservationId } }, ct);
        var reserves = Array(data, "reserves");
        if (reserves.Any(info => info is null || Text(info, "organizationId") != organizationId))
            throw new JsonException("Unexpected iiko reservation organization.");
        var info = reserves.SingleOrDefault(item => item?["id"]?.GetValue<string>() == externalReservationId);
        return info is null ? new ReservationResult(false, externalReservationId, null, "Unknown") : ReadReserve(info, null);
    }

    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var (success, data, error) = await PostAsync<object, JsonNode>("api/1/order/create", new { organizationId, order = new {
            tableIds = request.TableId is null ? System.Array.Empty<string>() : new[] { request.TableId },
            phone = request.Customer.Phone, items = request.Items.Select(item => new { productId = item.ExternalProductId, amount = item.Quantity })
        } }, ct);
        return success && data is not null ? new(true, data["orderId"]?.GetValue<string>(), null, null) : new(false, null, null, error);
    }

    public async Task<OrderStatusResult> GetOrderStatusAsync(string externalOrderId, CancellationToken ct = default)
    {
        var (success, data, error) = await PostAsync<object, JsonNode>("api/1/order/by_id", new { organizationId, orderIds = new[] { externalOrderId } }, ct);
        if (!success || data is null) return new(false, "Unknown", error);
        var info = data["orders"]?.AsArray().FirstOrDefault();
        return info is null ? new(false, "NotFound", "Заказ не найден") : new(true, info["status"]?.ToString() ?? "Unknown", null);
    }

    public async Task<OrderResult> CancelOrderAsync(string externalOrderId, CancellationToken ct = default)
    {
        var (success, _, error) = await PostAsync<object, JsonNode>("api/1/order/close", new { organizationId, orderId = externalOrderId }, ct);
        return new(success, externalOrderId, null, success ? null : error);
    }
}