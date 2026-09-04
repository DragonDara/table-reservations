using Google.Apis.Sheets.v4.Data;
using table_reservations.Models;

namespace table_reservations.Services
{
    public interface IGoogleSheetsService
    {
        Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default);
        Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, int? excludeSheetRowNumber = null, CancellationToken ct = default);
        Task<bool> HasConflictAsync(ReservationInfo reservation, DateTime scheduledAt, int? excludeSheetRowNumber = null, CancellationToken ct = default);
        Task<bool> IsPhoneAlreadyReservedAsync(string customerPhone, CancellationToken ct = default);
        Task<bool> HasReservationForPhoneAsync(string customerPhone, DateTime scheduledAt, CancellationToken ct = default);
        Task<ActiveReservationInfo?> FindActiveReservationByPhoneAsync(string customerPhone, CancellationToken ct = default);
        Task<IReadOnlyList<ActiveReservationInfo>> FindAllActiveReservationsByPhoneAsync(string customerPhone, CancellationToken ct = default);
        Task<AppendValuesResponse> AppendReservationAsync(ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default);
        Task OverwriteReservationAsync(int sheetRowNumber, ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default);
        Task ClearReservationRowAsync(int sheetRowNumber, CancellationToken ct = default);
        bool TryParseTableIds(string value, out int[] ids);
        Task MarkReminderSentAsync(int sheetRowNumber, CancellationToken ct);
        Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default);
    }
}
