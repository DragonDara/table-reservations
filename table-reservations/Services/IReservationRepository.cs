using table_reservations.Models;

namespace table_reservations.Services;

public interface IReservationRepository
{
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default);
    Task<IReadOnlyList<DateTime>> GetAvailableSlotsAsync(DateOnly date, DateTime now, CancellationToken ct = default);
    Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, CancellationToken ct = default);
    Task<CarWashCatalog> GetCarWashCatalogAsync(CancellationToken ct = default);
    Task<CarWashAvailability> GetCarWashAvailabilityAsync(DateOnly date, CarWashSelection selection, CancellationToken ct = default);
    Task<BookingResult> BookAsync(ReservationInfo request, DateTime scheduledAt, CancellationToken ct = default);
    Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default);
    Task MarkReminderSentAsync(string reservationId, CancellationToken ct);
}
