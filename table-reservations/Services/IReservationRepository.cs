using table_reservations.Models;

namespace table_reservations.Services
{
    /// <summary>
    /// Tenant-scoped reservation data access. Backed by Turso (libSQL); replaces the
    /// previous Google Sheets implementation.
    /// </summary>
    public interface IReservationRepository
    {
        Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default);
        Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, long? excludeReservationId = null, CancellationToken ct = default);
        Task<bool> HasConflictAsync(ReservationInfo reservation, DateTime scheduledAt, long? excludeReservationId = null, CancellationToken ct = default);
        Task<bool> IsPhoneAlreadyReservedAsync(string customerPhone, CancellationToken ct = default);
        Task<bool> HasReservationForPhoneAsync(string customerPhone, DateTime scheduledAt, CancellationToken ct = default);
        Task<ActiveReservationInfo?> FindActiveReservationByPhoneAsync(string customerPhone, CancellationToken ct = default);
        Task<IReadOnlyList<ActiveReservationInfo>> FindAllActiveReservationsByPhoneAsync(string customerPhone, CancellationToken ct = default);
        Task<long> AppendReservationAsync(ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default);
        Task OverwriteReservationAsync(long reservationId, ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default);
        Task DeleteReservationAsync(long reservationId, CancellationToken ct = default);
        bool TryParseTableIds(string value, out int[] ids);
        Task MarkReminderSentAsync(long reservationId, CancellationToken ct);
        Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default);
    }
}
