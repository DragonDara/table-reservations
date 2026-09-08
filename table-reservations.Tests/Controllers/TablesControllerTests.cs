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
    public void SlotsRoute_IsRegisteredExactlyOnce()
    {
        var routes = typeof(TablesController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true))
            .Cast<HttpGetAttribute>();

        Assert.Single(routes, route => route.Template == "slots");
    }

    [Fact]
    public async Task GetAvailableSlots_ReturnsNotFoundForNonRestaurantTenant()
    {
        var controller = CreateController(BusinessType.CarWash, new StubReservationRepository());

        var result = await controller.GetAvailableSlots("2026-09-04", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetAvailableSlots_RejectsInvalidDateFormat()
    {
        var controller = CreateController(BusinessType.Restaurant, new StubReservationRepository());

        var result = await controller.GetAvailableSlots("04.09.2026", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAvailableSlots_RejectsDateOutsideSevenDayWindow()
    {
        var controller = CreateController(BusinessType.Restaurant, new StubReservationRepository());
        var outsideWindow = DateOnly.FromDateTime(ReservationDateTime.KazakhstanNow())
            .AddDays(RestaurantSlotSchedule.BookingDays)
            .ToString("yyyy-MM-dd");

        var result = await controller.GetAvailableSlots(outsideWindow, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAvailableSlots_ReturnsExistingWireFormat()
    {
        var reservations = new StubReservationRepository
        {
            AvailableSlots =
            [
                new DateTime(2026, 9, 4, 18, 0, 0),
                new DateTime(2026, 9, 4, 18, 30, 0),
            ]
        };
        var controller = CreateController(BusinessType.Restaurant, reservations);
        var today = DateOnly.FromDateTime(ReservationDateTime.KazakhstanNow()).ToString("yyyy-MM-dd");

        var result = await controller.GetAvailableSlots(today, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(
            ["2026-09-04T18:00", "2026-09-04T18:30"],
            Assert.IsAssignableFrom<IEnumerable<string>>(ok.Value));
    }

    private static TablesController CreateController(BusinessType type, IReservationRepository reservations)
    {
        var tenant = new TenantContext();
        tenant.Set(new OrganizationOptions { Id = "test", BusinessType = type });
        return new TablesController(reservations, tenant);
    }

    private sealed class StubReservationRepository : IReservationRepository
    {
        public Task<IReadOnlyList<ReservationListItem>> GetReservationsAsync(DateOnly date, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IReadOnlyList<DateTime> AvailableSlots { get; init; } = Array.Empty<DateTime>();

        public Task<IReadOnlyList<DateTime>> GetAvailableSlotsAsync(
            DateOnly date,
            DateTime now,
            CancellationToken ct = default) => Task.FromResult(AvailableSlots);

        public Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CarWashCatalog> GetCarWashCatalogAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CarWashAvailability> GetCarWashAvailabilityAsync(DateOnly date, CarWashSelection selection, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<BookingResult> BookAsync(ReservationInfo request, DateTime scheduledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task MarkReminderSentAsync(string reservationId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
