using Google.Apis.Sheets.v4.Data;
using table_reservations.Models;

namespace table_reservations.Services
{
    public interface IGoogleSheetsService
    {
        Task<IReadOnlyList<TableInfo>> GetTablesAsync(string date, string time, int duration, CancellationToken ct = default);
        Task<bool> IsReservationTakenAsync(int tableId, DateTime dateTime, int durationHours, CancellationToken ct = default);
        Task<AppendValuesResponse> AppendReservationAsync(ReservationInfo reservation, DateTime dateTime, CancellationToken ct = default);
    }
}
