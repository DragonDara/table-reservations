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
        private const string ApplicationName = "TableReservationsAPI";
        private const string TablesRange = "Столики!A2:C100";
        private const string ReservationsRange = "Брони!A2:H10000";
        private const string ReservationsAppendRange = "Брони!A:H";

        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };

        private readonly IConfiguration _config;

        public GoogleSheetsService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(DateTime? scheduledAt = null, CancellationToken ct = default)
            {
                var service = CreateService();
                var spreadsheetId = GetSpreadsheetId();
                var slotStart = scheduledAt ?? ReservationDateTime.KazakhstanNow();
                var slotEnd = slotStart.AddHours(ReservationDuration.Hours);

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

                    // если выбранный слот пересекается с существующей бронью — столик занят
                    if (reservationStart < slotEnd && reservationEnd > slotStart)
                    {
                        isOccupied = true;
                        break;
                    }

                    // ближайшая будущая бронь после выбранного слота
                    if (reservationStart >= slotEnd && (nextStart is null || reservationStart < nextStart.Value))
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
                    var hours = (nextStart.Value - slotStart).TotalHours;
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

        public async Task<bool> IsReservationTakenAsync(
            string tablesId,
            DateTime scheduledAt,
            int? excludeSheetRowNumber = null,
            CancellationToken ct = default)
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

            for (var i = 0; i < rows.Count; i++)
            {
                var sheetRowNumber = i + 2; // A2 = первая строка данных
                if (excludeSheetRowNumber.HasValue && sheetRowNumber == excludeSheetRowNumber.Value)
                {
                    continue;
                }

                var row = rows[i];
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

        public async Task<bool> IsPhoneAlreadyReservedAsync(string customerPhone, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                return false;
            }

            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var normalizedPhone = customerPhone.Trim();

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();

            foreach (var row in rows)
            {
                string GetCell(int idx) => row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";

                var existingPhone = GetCell(3);
                if (!string.Equals(existingPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public async Task<ActiveReservationInfo?> FindActiveReservationByPhoneAsync(
            string customerPhone,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                return null;
            }

            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var normalizedPhone = customerPhone.Trim();
            var now = ReservationDateTime.KazakhstanNow();

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();
            ActiveReservationInfo? nearest = null;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string GetCell(int idx) => row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";

                var existingPhone = GetCell(3);
                if (!string.Equals(existingPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseSheetDateTime(GetCell(4), out var reservationStart))
                {
                    continue;
                }

                var reservationEnd = reservationStart.AddHours(ReservationDuration.Hours);
                if (reservationEnd <= now)
                {
                    continue;
                }

                var candidate = new ActiveReservationInfo
                {
                    SheetRowNumber = i + 2,
                    TablesId = GetCell(1),
                    CustomerName = GetCell(2),
                    CustomerPhone = existingPhone,
                    ScheduledAt = reservationStart.ToString(ReservationDateTime.Format),
                    ScheduledAtValue = reservationStart
                };

                if (nearest is null || candidate.ScheduledAtValue < nearest.ScheduledAtValue)
                {
                    nearest = candidate;
                }
            }

            return nearest;
        }

        public async Task<IReadOnlyList<ActiveReservationInfo>> FindAllActiveReservationsByPhoneAsync(
            string customerPhone,
            CancellationToken ct = default)
        {
            var result = new List<ActiveReservationInfo>();
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                return result;
            }

            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var normalizedPhone = customerPhone.Trim();
            var now = ReservationDateTime.KazakhstanNow();

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string GetCell(int idx) => row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";

                var existingPhone = GetCell(3);
                if (!string.Equals(existingPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseSheetDateTime(GetCell(4), out var reservationStart))
                {
                    continue;
                }

                var reservationEnd = reservationStart.AddHours(ReservationDuration.Hours);
                if (reservationEnd <= now)
                {
                    continue;
                }

                result.Add(new ActiveReservationInfo
                {
                    SheetRowNumber = i + 2,
                    TablesId = GetCell(1),
                    CustomerName = GetCell(2),
                    CustomerPhone = existingPhone,
                    ScheduledAt = reservationStart.ToString(ReservationDateTime.Format),
                    ScheduledAtValue = reservationStart
                });
            }

            return result;
        }

        public async Task OverwriteReservationAsync(
            int sheetRowNumber,
            ReservationInfo reservation,
            DateTime scheduledAt,
            CancellationToken ct = default)
        {
            if (!TryParseTableIds(reservation.TablesId, out var ids))
                throw new ArgumentException("Некорректные TablesId.");

            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var tablesIdCell = string.Join(",", ids);
            var range = $"Брони!A{sheetRowNumber}:H{sheetRowNumber}";

            var valueRange = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object>
                    {
                        Guid.NewGuid().ToString(),
                        tablesIdCell,
                        reservation.CustomerName,
                        reservation.CustomerPhone,
                        scheduledAt.ToString(ReservationDateTime.Format),
                        "",
                        reservation.RemindBeforeHour ? "Да" : "Нет",
                        ""
                    }
                }
            };

            var update = service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
            update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await update.ExecuteAsync(ct);
        }

        public async Task ClearReservationRowAsync(int sheetRowNumber, CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var range = $"Брони!A{sheetRowNumber}:H{sheetRowNumber}";

            var clear = service.Spreadsheets.Values.Clear(new ClearValuesRequest(), spreadsheetId, range);
            await clear.ExecuteAsync(ct);
        }

        public async Task<bool> HasReservationForPhoneAsync(string customerPhone, DateTime scheduledAt, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                return false;
            }

            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var normalizedPhone = customerPhone.Trim();

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();

            foreach (var row in rows)
            {
                string GetCell(int idx) => row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";

                var existingPhone = GetCell(3);
                if (!string.Equals(existingPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseSheetDateTime(GetCell(4), out var reservationStart))
                {
                    continue;
                }

                var reservationEnd = reservationStart.AddHours(ReservationDuration.Hours);
                if (reservationStart < scheduledAt.AddHours(ReservationDuration.Hours) && reservationEnd > scheduledAt)
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
                scheduledAt.ToString(ReservationDateTime.Format),
                "",
                reservation.RemindBeforeHour ? "Да" : "Нет"
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

        public async Task MarkReminderSentAsync(int sheetRowNumber, CancellationToken ct)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var range = $"Брони!H{sheetRowNumber}";

            var update = service.Spreadsheets.Values.Update(
                new ValueRange { Values = new List<IList<object>> { new List<object> { "Да" } } },
                spreadsheetId,
                range);
            update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await update.ExecuteAsync(ct);
        }

        public async Task<IReadOnlyList<ReminderCandidate>> GetReminderCandidatesAsync(CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();
            var result = new List<ReminderCandidate>(rows.Count);

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                string GetCell(int idx) =>
                    row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";

                var tablesId = GetCell(1);
                var customerName = GetCell(2);
                var customerPhone = GetCell(3);
                var scheduledAt = GetCell(4);
                var remindCell = GetCell(6); // G
                var sentCell = GetCell(7);   // H

                // пустая/битая строка
                if (string.IsNullOrWhiteSpace(tablesId) &&
                    string.IsNullOrWhiteSpace(scheduledAt) &&
                    string.IsNullOrWhiteSpace(remindCell))
                {
                    continue;
                }

                result.Add(new ReminderCandidate
                {
                    SheetRowNumber = i + 2, // A2 = первая строка данных
                    TablesId = tablesId,
                    CustomerName = customerName,
                    CustomerPhone = customerPhone,
                    ScheduledAt = scheduledAt,
                    RemindBeforeHourCell = remindCell,
                    ReminderSentCell = sentCell
                });
            }

            return result;
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
                return CredentialFactory
                    .FromStream<ServiceAccountCredential>(stream)
                    .ToGoogleCredential()
                    .CreateScoped(Scopes);
            }

            var jsonPath = _config["GoogleSheets:CredentialsJsonPath"];
            if (!string.IsNullOrWhiteSpace(jsonPath))
            {
                using var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read);
                return CredentialFactory
                    .FromStream<ServiceAccountCredential>(stream)
                    .ToGoogleCredential()
                    .CreateScoped(Scopes);
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
