using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using table_reservations.Constants;
using table_reservations.Models;

namespace table_reservations.Services
{
    public class GoogleSheetsService : IGoogleSheetsService
    {
        private const string ApplicationName = "TableReservationsAPI";
        private const string TablesRange = "Столики!A2:C100";
        private const string ReservationsRange = "Брони!A2:G10000";
        private const string ReservationsAppendRange = "Брони!A:G";

        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };

        private readonly IConfiguration _config;

        public GoogleSheetsService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(string date, string time, int duration, CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();

            var slotStart = ParseSlotStart(date, time);
            var slotEnd = slotStart.AddHours(duration);

            var tablesResponse = await service.Spreadsheets.Values
                .Get(spreadsheetId, TablesRange)
                .ExecuteAsync(ct);

            var tableRows = tablesResponse.Values;
            var tables = new List<TableInfo>();

            if (tableRows == null || tableRows.Count == 0)
            {
                return tables;
            }

            foreach (var row in tableRows)
            {
                string GetCell(int index) =>
                    row != null && row.Count > index ? row[index]?.ToString()?.Trim() ?? "" : "";

                var idText = GetCell(0);
                var typeText = GetCell(1);
                var capText = GetCell(2);

                if (string.IsNullOrWhiteSpace(idText) &&
                    string.IsNullOrWhiteSpace(typeText) &&
                    string.IsNullOrWhiteSpace(capText))
                {
                    continue;
                }

                if (!int.TryParse(idText, out var id) || id <= 0)
                {
                    continue;
                }

                int.TryParse(capText, out var capacity);

                tables.Add(new TableInfo
                {
                    Id = id,
                    Type = ParseTableType(typeText),
                    Capacity = capacity,
                    Status = TableStatuses.Free
                });
            }

            var reservationResponse = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var reservationRows = reservationResponse.Values ?? new List<IList<object>>();

            foreach (var table in tables)
            {
                var isOccupied = reservationRows.Any(row =>
                {
                    string GetCell(int idx) =>
                        row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";
                    // B = TableId, E = Start, F = Status, G = Duration
                    if (!int.TryParse(GetCell(1), out var reservationTableId) || reservationTableId != table.Id)
                    {
                        return false;
                    }

                    var reservationStatus = GetCell(5);
                    if (reservationStatus.Equals("Отменено", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (!TryParseSheetDateTime(GetCell(4), out var reservationStart))
                    {
                        return false;
                    }

                    var reservationHours = 1;
                    if (int.TryParse(GetCell(6), out var parsedHours) && parsedHours > 0)
                    {
                        reservationHours = parsedHours;
                    }
                    var reservationEnd = reservationStart.AddHours(reservationHours);
                    // пересечение интервалов
                    return reservationStart < slotEnd && reservationEnd > slotStart;
                });
                table.Status = isOccupied ? TableStatuses.Occupied : TableStatuses.Free;
            }
            return tables;

        }

        public async Task<bool> IsReservationTakenAsync(int tableId, DateTime dateTime, int durationHours, CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();

            var slotStart = dateTime;
            var slotEnd = slotStart.AddHours(durationHours);

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();

            return rows.Any(row =>
            {
                string GetCell(int idx) => row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";
                // B = TableId
                if (!int.TryParse(GetCell(1), out var reservationTableId) || reservationTableId != tableId)
                {
                    return false;
                }
                // F = Status
                var reservationStatus = GetCell(5);
                if (reservationStatus.Equals("Отменено", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                // E = Start
                if (!TryParseSheetDateTime(GetCell(4), out var reservationStart))
                {
                    return false;
                }
                // G = Duration (если пусто — 1 час)
                var reservationHours = 1;
                if (int.TryParse(GetCell(6), out var parsedHours) && parsedHours > 0)
                {
                    reservationHours = parsedHours;
                }
                var reservationEnd = reservationStart.AddHours(reservationHours);
                // пересечение интервалов — как в GetTablesAsync
                return reservationStart < slotEnd && reservationEnd > slotStart;
            });
        }

        public async Task<AppendValuesResponse> AppendReservationAsync(ReservationInfo reservation, DateTime dateTime, CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();

            var newRow = new List<object>
            {
                Guid.NewGuid().ToString(),
                reservation.TableId,
                reservation.CustomerName,
                reservation.CustomerPhone,
                dateTime.ToString(ReservationDateTime.Format),
                "Ожидает",
                reservation.Duration
            };

            var valueRange = new ValueRange
            {
                Values = new List<IList<object>> { newRow }
            };

            var appendReq = service.Spreadsheets.Values.Append(valueRange, spreadsheetId, ReservationsAppendRange);
            appendReq.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            appendReq.InsertDataOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;

            return await appendReq.ExecuteAsync(ct);
        }

        private SheetsService CreateService()
        {
            var jsonPath = _config["GoogleSheets:CredentialsJsonPath"]
                ?? throw new InvalidOperationException("GoogleSheets:CredentialsJsonPath is not configured.");

            GoogleCredential credential;
            using (var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }

            return new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });
        }

        private string GetSpreadsheetId() =>
            _config["GoogleSheets:SpreadsheetId"]
            ?? throw new InvalidOperationException("GoogleSheets:SpreadsheetId is not configured.");

        private static TableType ParseTableType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TableType.Обычный;
            }

            return value.Trim().Equals("VIP", StringComparison.OrdinalIgnoreCase)
                ? TableType.VIP
                : TableType.Обычный;
        }

        private static DateTime ParseSlotStart(string date, string time)
        {
            if (!ReservationDateTime.TryParse($"{date} {time}", out var slotStart))
            {
                throw new ArgumentException("Некорректные date или time.");
            }

            return slotStart;
        }

        private static bool TryParseSheetDateTime(string value, out DateTime result) =>
            ReservationDateTime.TryParse(value, out result);
    }
}
