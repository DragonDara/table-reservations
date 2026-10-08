using table_reservations.Models;

namespace table_reservations.Services;

public interface IReservationRepository
{
    Task<IReadOnlyList<ReservationListItem>> GetReservationsAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default);
    Task<IReadOnlyList<DateTime>> GetAvailableSlotsAsync(DateOnly date, DateTime now, CancellationToken ct = default);
    Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, CancellationToken ct = default);
    Task<CarWashCatalog> GetCarWashCatalogAsync(CancellationToken ct = default);
    Task<CarWashAvailability> GetCarWashAvailabilityAsync(DateOnly date, CarWashSelection selection, CancellationToken ct = default);
    Task<BookingResult> BookAsync(ReservationInfo request, DateTime scheduledAt, CancellationToken ct = default);
    Task<BookingResult> BookWithIdempotencyAsync(ReservationInfo request, DateTime scheduledAt, string? key, CancellationToken ct = default)
        => BookAsync(request, scheduledAt, ct);
    Task<BookingResult?> GetBookingStatusAsync(string id, CancellationToken ct = default) => Task.FromResult<BookingResult?>(null);
    Task<BookingResult?> CheckPosBookingAsync(string id, CancellationToken ct = default) => GetBookingStatusAsync(id, ct);
    Task NotifyPosBookingAsync(string id, IReservationNotificationService notifications, CancellationToken ct = default) => Task.CompletedTask;
    Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default);
    Task MarkReminderSentAsync(string reservationId, CancellationToken ct);
}
