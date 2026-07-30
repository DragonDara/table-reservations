using System.Text;
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
        private static DateTime KazakhstanNow() => DateTime.UtcNow.AddHours(5);
        private const string ApplicationName = "TableReservationsAPI";
        private const string TablesRange = "Столики!A2:C100";
        private const string ReservationsRange = "Брони!A2:E10000";
        private const string ReservationsAppendRange = "Брони!A:E";

        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };

        private readonly IConfiguration _config;

        public GoogleSheetsService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var now = KazakhstanNow();

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
                    Seats = capacity,
                    Status = TableStatuses.Free
                });
            }

            var reservationResponse = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var reservationRows = reservationResponse.Values ?? new List<IList<object>>();

            foreach (var table in tables)
            {
                DateTime? nextStart = null;
                var isOccupied = false;

                foreach (var row in reservationRows)
                {
                    string GetCell(int idx) =>
                        row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";
                    // B = TableId, E = Start, F = Status, G = Duration
                    if (!TryParseTableIds(GetCell(1), out var reservationTableIds) || !reservationTableIds.Contains(table.Id))
                    {
                        continue;
                    }

                    if (!TryParseSheetDateTime(GetCell(4), out var reservationStart))
                    {
                        continue;
                    }

                    var reservationHours = ReservationDuration.Hours;

                    var reservationEnd = reservationStart.AddHours(reservationHours);

                    // если время пересекается с "сейчас", то столик занят
                    if (reservationStart <= now && reservationEnd > now)
                    {
                        isOccupied = true;
                        break;
                    }

                    // ближайшая будущая бронь
                    if (reservationStart > now && (nextStart is null || reservationStart < nextStart.Value))
                    {
                        nextStart = reservationStart;
                    }
                }

                if (isOccupied)
                {
                    table.Status = TableStatuses.Occupied;
                    table.NextReservationHours = null;
                }
                else if (nextStart is not null)
                {
                    var hours = (nextStart.Value - now).TotalHours;
                    table.NextReservationHours = Math.Round(hours, 2);
                    table.Status = TableStatuses.Limited;
                }
                else
                {
                    table.Status = TableStatuses.Free;
                    table.NextReservationHours = null;
                }
            }
            return tables;

        }

        public async Task<bool> IsReservationTakenAsync(string tablesId, DateTime scheduledAt, CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();

            var slotStart = scheduledAt;
            var slotEnd = slotStart.AddHours(ReservationDuration.Hours);

            // Нужно один раз сделать, в цикле не нужен
            if (!TryParseTableIds(tablesId, out var requestedIds))
            {
                return false;
            }

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();

            foreach ( var row in rows)
            {
                string GetCell(int idx) => row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";

                // B = TableId
                if (!TryParseTableIds(GetCell(1), out var reservationTableIds))
                {
                    continue;
                }

                if (!reservationTableIds.Intersect(requestedIds).Any())
                {
                    continue;
                }

                // E = Start
                if (!TryParseSheetDateTime(GetCell(4), out var reservationStart))
                {
                    continue;
                }
                // Duration (3 часа)
                var reservationHours = ReservationDuration.Hours;

                var reservationEnd = reservationStart.AddHours(reservationHours);
                // пересечение интервалов — как в GetTablesAsync
                if (reservationStart < slotEnd && reservationEnd > slotStart)
                {
                    return true;
                }
            }

            return false;

        }

        public async Task<AppendValuesResponse> AppendReservationAsync(ReservationInfo reservation, DateTime scheduledAt, CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();

            // Валидация, что формат массива ID правильный
            if (!TryParseTableIds(reservation.TablesId, out var ids))
                throw new ArgumentException("Некорректные TablesId.");
            var tablesIdCell = string.Join(",", ids);

            var newRow = new List<object>
            {
                Guid.NewGuid().ToString(),
                tablesIdCell,
                reservation.CustomerName,
                reservation.CustomerPhone,
                scheduledAt.ToString(ReservationDateTime.Format)
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

        private SheetsService CreateService() =>
            new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = CreateCredential(),
                ApplicationName = ApplicationName
            });

        private GoogleCredential CreateCredential()
        {
            var credentialsJson = _config["GoogleSheets:CredentialsJson"];
            if (!string.IsNullOrWhiteSpace(credentialsJson))
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(credentialsJson));
                return GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }

            var jsonPath = _config["GoogleSheets:CredentialsJsonPath"];
            if (!string.IsNullOrWhiteSpace(jsonPath))
            {
                using var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read);
                return GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }

            throw new InvalidOperationException(
                "GoogleSheets:CredentialsJson or GoogleSheets:CredentialsJsonPath must be configured.");
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

        private static bool TryParseSheetDateTime(string value, out DateTime result) =>
            ReservationDateTime.TryParse(value, out result);

        public bool TryParseTableIds(string value, out int[] ids)
        {
            ids = Array.Empty<int>();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var parts = value.Split(new[] { ',', ';', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var list = new List<int>(parts.Length);
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var id) || id <= 0)
                    return false;
                list.Add(id);
            }

            ids = list.ToArray();
            return ids.Length > 0;
        }
    }
}
