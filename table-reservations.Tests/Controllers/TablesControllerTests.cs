using Google.Apis.Sheets.v4.Data;
using Microsoft.AspNetCore.Mvc;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Controllers;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests.Controllers;

public class TablesControllerTests
{
    [Fact]
    public async Task GetAvailableSlots_ReturnsNotFoundForNonRestaurantTenant()
    {
        var controller = CreateController(BusinessType.CarWash, new StubSheetsService());

        var result = await controller.GetAvailableSlots("2026-09-04", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetAvailableSlots_RejectsInvalidDateFormat()
    {
        var controller = CreateController(BusinessType.Restaurant, new StubSheetsService());

        var result = await controller.GetAvailableSlots("04.09.2026", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAvailableSlots_RejectsDateOutsideSevenDayWindow()
    {
        var controller = CreateController(BusinessType.Restaurant, new StubSheetsService());
        var outsideWindow = DateOnly.FromDateTime(ReservationDateTime.KazakhstanNow())
            .AddDays(RestaurantSlotSchedule.BookingDays)
            .ToString("yyyy-MM-dd");

        var result = await controller.GetAvailableSlots(outsideWindow, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAvailableSlots_ReturnsExistingWireFormat()
    {
        var sheets = new StubSheetsService
        {
            AvailableSlots =
            [
                new DateTime(2026, 9, 4, 18, 0, 0),
                new DateTime(2026, 9, 4, 18, 30, 0),
            ]
        };
        var controller = CreateController(BusinessType.Restaurant, sheets);
        var today = DateOnly.FromDateTime(ReservationDateTime.KazakhstanNow()).ToString("yyyy-MM-dd");

        var result = await controller.GetAvailableSlots(today, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(
            ["2026-09-04T18:00", "2026-09-04T18:30"],
            Assert.IsAssignableFrom<IEnumerable<string>>(ok.Value));
    }

    private static TablesController CreateController(BusinessType type, IGoogleSheetsService sheets)
    {
        var tenant = new TenantContext();
        tenant.Set(new OrganizationOptions { Id = "test", BusinessType = type });
        return new TablesController(sheets, tenant);
    }

    private sealed class StubSheetsService : IGoogleSheetsService
    {
        public IReadOnlyList<DateTime> AvailableSlots { get; init; } = Array.Empty<DateTime>();

        public Task<IReadOnlyList<DateTime>> GetAvailableSlotsAsync(
            DateOnly date,
            DateTime now,
            CancellationToken ct = default) => Task.FromResult(AvailableSlots);

        public Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> HasConflictAsync(ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> IsPhoneAlreadyReservedAsync(string customerPhone, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> HasReservationForPhoneAsync(string customerPhone, DateTime scheduledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AppendValuesResponse> AppendReservationAsync(ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public bool TryParseTableIds(string value, out int[] ids)
        {
            ids = Array.Empty<int>();
            throw new NotSupportedException();
        }

        public Task MarkReminderSentAsync(int sheetRowNumber, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
