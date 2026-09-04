using table_reservations.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace table_reservations.Pos;

public class IikoAdapter : ApiClient, IPosAdapter
{
    private readonly string _apiLogin;
    private readonly string _organizationId;

    public string ProviderName => "iiko";

    public IikoAdapter(HttpClient http, string apiLogin, string organizationId)
        : base(http)
    {
        _apiLogin = apiLogin;
        _organizationId = organizationId;
    }

    public async Task AuthenticateAsync(CancellationToken ct = default)
    {
        var request = new IikoTokenRequest(_apiLogin);
        var (success, data, error) = await PostAsync<IikoTokenRequest, IikoTokenResponse>("api/1/access_token", request, ct);

        if (!success || data is null)
        {
            throw new InvalidOperationException($"Не удалось авторизоваться в iiko: {error}");
        }

        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.Token);
    }
    public async Task<List<PosTable>> GetTablesAsync(CancellationToken ct = default)
    {
        var payload = new
        {
            organizationIds = new[] { _organizationId },
            terminalGroupIds = new[] { "cc57c04a-7727-c9c0-019b-bfef90f80066" }
        };

        var (success, data, error) = await PostAsync<object, System.Text.Json.Nodes.JsonNode>(
            "api/1/reserve/available_restaurant_sections",
            payload,
            ct);

        if (!success || data is null)
        {
            Console.WriteLine($"[iiko Error] {error}");
            return new List<PosTable>();
        }

        var resultList = new List<PosTable>();

        var sections = data["restaurantSections"]?.AsArray();
        if (sections == null) return resultList;

        foreach (var section in sections)
        {
            if (section == null) continue;

            string sectionName = section["name"]?.ToString() ?? "Без зала";
            var tables = section["tables"]?.AsArray();

            if (tables == null) continue;

            foreach (var t in tables)
            {
                if (t == null) continue;
                if (t["isDeleted"]?.GetValue<bool>() == true) continue;

                int number = t["number"]?.GetValue<int>() ?? 0;
                string rawName = t["name"]?.ToString() ?? "";

                string tableName = string.IsNullOrWhiteSpace(rawName)
                    ? $"Стол №{number}"
                    : rawName;

                resultList.Add(new PosTable(
                    Id: t["id"]?.ToString() ?? "",
                    Name: tableName,
                    Seats: t["seatingCapacity"]?.GetValue<int>() ?? 0,
                    IsAvailable: true,
                    Number: number,
                    SectionName: sectionName
                ));
            }
        }

        return resultList;
    }
    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var payload = new
        {
            organizationId = _organizationId,
            order = new
            {
                tableIds = request.TableId is not null ? new[] { request.TableId } : Array.Empty<string>(),
                phone = request.Customer.Phone,
                items = request.Items.Select(i => new { productId = i.ExternalProductId, amount = i.Quantity })
            }
        };

        var (success, data, error) = await PostAsync<object, IikoOrderResponse>("api/1/order/create", payload, ct);

        return success && data is not null
           ? new OrderResult(Success: true, ExternalOrderId: data.OrderId, Total: null, ErrorMessage: null)
           : new OrderResult(Success: false, ExternalOrderId: null, Total: null, ErrorMessage: error);
    }

    public async Task<OrderStatusResult> GetOrderStatusAsync(string externalOrderId, CancellationToken ct = default)
    {
        var payload = new
        {
            organizationId = _organizationId,
            orderIds = new[] { externalOrderId }
        };

        // Используем POST api/1/order/by_id  (iiko Cloud API)
        var (success, data, error) = await PostAsync<object, System.Text.Json.Nodes.JsonNode>(
            "api/1/order/by_id",
            payload,
            ct);

        if (!success || data is null)
        {
            return new OrderStatusResult(false, "Unknown", error);
        }

        // В ответе iiko возвращается массив "orders"
        var ordersArray = data["orders"]?.AsArray();
        if (ordersArray is null || ordersArray.Count == 0)
        {
            return new OrderStatusResult(false, "NotFound", "Заказ не найден");
        }

        var order = ordersArray.FirstOrDefault();
        if (order is null)
        {
            return new OrderStatusResult(false, "NotFound", "Заказ не найден");
        }

        // Статус заказа в iiko (например: "New", "Bill", "Closed", "Deleted")
        string status = order["status"]?.ToString() ?? "Unknown";

        return new OrderStatusResult(true, status, null);
    }

    /// <summary>
    /// Получить список всех активных (незакрытых) заказов организации.
    /// Используется для определения занятых столов и статусов открытых чеков.
    /// </summary>
    public async Task<List<ActiveOrderDto>> GetActiveOrdersAsync(CancellationToken ct = default)
    {
        var tables = await GetTablesAsync(ct);
        var tableIds = tables.Select(t => t.Id).ToArray();

        if (tableIds.Length == 0)
        {
            return new List<ActiveOrderDto>();
        }


        var payload = new
        {
            organizationIds = new[] { _organizationId },
            tableIds = tableIds
        };



        // Эндпоинт iiko для получения всех открытых/активных заказов
        var (success, data, error) = await PostAsync<object, System.Text.Json.Nodes.JsonNode>(
            "api/1/order/by_table",
            payload,
            ct);

        var activeOrders = new List<ActiveOrderDto>();

        if (!success || data is null)
        {
            Console.WriteLine($"[iiko Error] Ошибка получения активных заказов: {error}");
            return activeOrders;
        }

        var ordersArray = data["orders"]?.AsArray();
        if (ordersArray == null) return activeOrders;

        foreach (var o in ordersArray)
        {
            if (o == null) continue;

            string orderId = o["id"]?.ToString() ?? "";
            string status = o["status"]?.ToString() ?? "";

            if (status != "New" && status != "InProgress" && status != "Bill")
            {
                // Пропускаем заказы, которые уже закрыты или удалены
                continue;
            }

            activeOrders.Add(new ActiveOrderDto(
                OrderId: o["id"]?.ToString() ?? "",
                Status: status,
                TableIds: o["tableIds"]?.AsArray()?.Select(t => t?.ToString() ?? "").ToList() ?? new(),
                Sum: o["sum"]?.GetValue<decimal>() ?? 0m
            ));
        }

        return activeOrders;
    }

    public async Task<OrderResult> CancelOrderAsync(string externalOrderId, CancellationToken ct = default)
    {
        var payload = new
        {
            organizationId = _organizationId,
            orderId = externalOrderId
            // возможно, ещё понадобится terminalGroupId или paymentInfo 
        };

        var (success, data, error) = await PostAsync<object, System.Text.Json.Nodes.JsonNode>(
            "api/1/order/close",
            payload,
            ct);

        return new OrderResult(
            Success: success,
            ExternalOrderId: externalOrderId,
            Total: null,
            ErrorMessage: success ? null : error);
    }
    private async Task<List<string>> GetRestaurantSectionIdsAsync(CancellationToken ct)
    {
        var payload = new
        {
            organizationIds = new[] { _organizationId },
            terminalGroupIds = new[] { "cc57c04a-7727-c9c0-019b-bfef90f80066" }
        };

        var (success, data, error) = await PostAsync<object, System.Text.Json.Nodes.JsonNode>(
            "api/1/reserve/available_restaurant_sections",
            payload,
            ct);

        var ids = new List<string>();
        if (!success || data is null) return ids;

        var sections = data["restaurantSections"]?.AsArray();
        if (sections == null) return ids;

        foreach (var s in sections)
        {
            var id = s?["id"]?.ToString();
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }

        return ids;
    }
    public async Task<List<ReservationInfoDto>> GetReservationsAsync(CancellationToken ct = default)
    {
        var sectionIds = await GetRestaurantSectionIdsAsync(ct);
        if (sectionIds.Count == 0)
        {
            Console.WriteLine("[iiko] Не удалось получить restaurantSectionIds");
            return new List<ReservationInfoDto>();
        }

        var today = DateTime.Today;

        var payload = new
        {
            organizationIds = new[] { _organizationId },
            restaurantSectionIds = sectionIds,
            dateFrom = today.ToString("yyyy-MM-ddTHH:mm:ss"),
            dateTo = today.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss")
        };

        var (success, data, error) = await PostAsync<object, System.Text.Json.Nodes.JsonNode>(
            "api/1/reserve/restaurant_sections_workload",
            payload,
            ct);

        var result = new List<ReservationInfoDto>();

        if (!success || data is null)
        {
            Console.WriteLine($"[iiko Error] Ошибка получения броней: {error}");
            return result;
        }

        var reservesArray = data["reserves"]?.AsArray();
        if (reservesArray == null) return result;

        foreach (var r in reservesArray)
        {
            if (r == null) continue;

            result.Add(new ReservationInfoDto(
                ReservationId: r["id"]?.ToString() ?? "",
                TableIds: r["tableIds"]?.AsArray()?.Select(t => t?.ToString() ?? "").ToList() ?? new(),
                CustomerName: r["customer"]?["name"]?.ToString() ?? "",
                CustomerPhone: r["phone"]?.ToString() ?? "",
                EstimatedStartTime: DateTime.TryParse(r["estimatedStartTime"]?.ToString(), out var dt) ? dt : DateTime.MinValue,
                DurationInMinutes: r["durationInMinutes"]?.GetValue<int>() ?? 0,
                GuestsCount: r["guestsCount"]?.GetValue<int>() ?? 0
            ));
        }

        return result;
    }
    public async Task<ReservationResult> ReserveTableAsync(CreateReservationRequest request, CancellationToken ct = default)
    {

        var payload = new
        {
            organizationId = _organizationId,
            terminalGroupId = "cc57c04a-7727-c9c0-019b-bfef90f80066",
            tableIds = request.TableIds,
            customer = new { name = request.Customer.Name },
            phone = request.Customer.Phone,
            estimatedStartTime = request.ReservationTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            durationInMinutes = 120,
            guestsCount = request.GuestsCount,
            shouldRemind = false
        };

        var (success, data, error) = await PostAsync<object, IikoReservationResponse>("api/1/reserve/create", payload, ct);

        return success && data is not null
            ? new ReservationResult(true, data.ReservationId, null)
            : new ReservationResult(false, null, error);
    }

    public async Task<ReservationResult> CancelReservationAsync(string externalReservationId, CancellationToken ct = default)
    {
        var (success, _, error) = await PostAsync<IikoCancelRequest, object>($"api/1/reserve/{externalReservationId}/cancel", new IikoCancelRequest(), ct);
        return new ReservationResult(success, externalReservationId, success ? null : error);
    }
}

// ==========================================
// DTOs (Внутренние контракты iiko)
// ==========================================

// 1. Авторизация
internal record IikoTokenRequest([property: JsonPropertyName("apiLogin")] string ApiLogin);
internal record IikoTokenResponse([property: JsonPropertyName("token")] string Token);

// 2. Столы (Терминальные группы)
internal record IikoTerminalGroupsResponse(
    [property: JsonPropertyName("terminalGroups")] List<IikoTerminalGroup>? TerminalGroups
);

internal record IikoTerminalGroup(
    [property: JsonPropertyName("items")] List<IikoTerminalGroupItem>? Items
);

internal record IikoTerminalGroupItem(
    [property: JsonPropertyName("restaurantSections")] List<IikoRestaurantSection>? RestaurantSections
);

internal record IikoRestaurantSection(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("tables")] List<IikoTable>? Tables
);

internal record IikoTable(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("capacity")] int Capacity
);

// 3. Заказы и бронь
internal record IikoOrderResponse([property: JsonPropertyName("orderId")] string OrderId);
internal record IikoOrderStatusResponse([property: JsonPropertyName("status")] string Status);
internal record IikoReservationResponse([property: JsonPropertyName("reservationId")] string ReservationId);
internal record IikoCancelRequest;