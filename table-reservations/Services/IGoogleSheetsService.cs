using Google.Apis.Sheets.v4.Data;
using table_reservations.Models;

namespace table_reservations.Services
{
    public interface IGoogleSheetsService
    {
        Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default);
        Task<bool> IsReservationTakenAsync(string tableId, DateTime scheduledAt, CancellationToken ct = default);
        Task<AppendValuesResponse> AppendReservationAsync(ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default);
        bool TryParseTableIds(string value, out int[] ids);
        Task MarkReminderSentAsync(int sheetRowNumber, CancellationToken ct);
        Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default);
    }
}
