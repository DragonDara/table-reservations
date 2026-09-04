using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Services.BusinessTypes;
using table_reservations.Services.Tenancy;

namespace table_reservations.Services
{
    public class GoogleSheetsService : IGoogleSheetsService
    {
        private const string ApplicationName = "TableReservationsAPI";

        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };
        private static readonly SheetSchemaOptions DefaultSchema = new();

        private readonly IConfiguration _config;
        private readonly TenantContext _tenant;
        private readonly IBusinessTypeStrategyResolver _strategyResolver;

        public GoogleSheetsService(
            IConfiguration config,
            TenantContext tenant,
            IBusinessTypeStrategyResolver strategyResolver)
        {
            _config = config;
            _tenant = tenant;
            _strategyResolver = strategyResolver;
        }

        private SheetSchemaOptions Schema => _tenant.Organization?.Sheets ?? DefaultSchema;

        private string TablesRange => Schema.TablesRange;
        private string ReservationsRange => Schema.ReservationsRange;
        private string ReservationsAppendRange => Schema.ReservationsAppendRange;

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
                    // TableIds / Start columns per tenant schema
                    if (!TryParseTableIds(GetCell(Schema.TableIdsColumn), out var reservationTableIds) || !reservationTableIds.Contains(table.Id))
                    {
                        continue;
                    }

                    if (!TryParseSheetDateTime(GetCell(Schema.ScheduledAtColumn), out var reservationStart))
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

        public async Task<IReadOnlyList<DateTime>> GetAvailableSlotsAsync(
            DateOnly date,
            DateTime now,
            CancellationToken ct = default)
        {
            var candidates = RestaurantSlotSchedule.GetCandidateSlots(date, now);
            if (candidates.Count == 0)
            {
                return Array.Empty<DateTime>();
            }

            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();

            var tablesResponse = await service.Spreadsheets.Values
                .Get(spreadsheetId, TablesRange)
                .ExecuteAsync(ct);

            var tableIds = (tablesResponse.Values ?? new List<IList<object>>())
                .Select(row => row.Count > 0 ? row[0]?.ToString()?.Trim() : null)
                .Where(value => int.TryParse(value, out var id) && id > 0)
                .Select(value => int.Parse(value!))
                .ToHashSet();

            if (tableIds.Count == 0)
            {
                return Array.Empty<DateTime>();
            }

            var reservationsResponse = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var reservationsByTable = tableIds.ToDictionary(id => id, _ => new List<DateTime>());
            foreach (var row in reservationsResponse.Values ?? new List<IList<object>>())
            {
                string GetCell(int index) =>
                    row.Count > index ? row[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;

                if (!TryParseTableIds(GetCell(Schema.TableIdsColumn), out var reservedIds) ||
                    !TryParseSheetDateTime(GetCell(Schema.ScheduledAtColumn), out var reservationStart))
                {
                    continue;
                }

                foreach (var tableId in reservedIds)
                {
                    if (reservationsByTable.TryGetValue(tableId, out var starts))
                    {
                        starts.Add(reservationStart);
                    }
                }
            }

            return candidates
                .Where(slot => RestaurantSlotSchedule.HasAvailableTable(
                    slot,
                    tableIds.Select(tableId => reservationsByTable[tableId])))
                .ToArray();
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

                // TableIds column per tenant schema
                if (!TryParseTableIds(GetCell(Schema.TableIdsColumn), out var reservationTableIds))
                {
                    continue;
                }

                if (!reservationTableIds.Intersect(requestedIds).Any())
                {
                    continue;
                }

                // Start column per tenant schema
                if (!TryParseSheetDateTime(GetCell(Schema.ScheduledAtColumn), out var reservationStart))
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

        public async Task<bool> HasConflictAsync(
            ReservationInfo reservation,
            DateTime scheduledAt,
            int? excludeSheetRowNumber = null,
            CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var strategy = _strategyResolver.Resolve(_tenant.BusinessType);

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            var rows = response.Values ?? new List<IList<object>>();
            for (var i = 0; i < rows.Count; i++)
            {
                var sheetRowNumber = i + 2;
                if (excludeSheetRowNumber.HasValue && sheetRowNumber == excludeSheetRowNumber.Value)
                {
                    continue;
                }

                if (strategy.HasConflict(reservation, scheduledAt, rows[i], Schema))
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

                var existingPhone = GetCell(Schema.CustomerPhoneColumn);
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

                var existingPhone = GetCell(Schema.CustomerPhoneColumn);
                if (!string.Equals(existingPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseSheetDateTime(GetCell(Schema.ScheduledAtColumn), out var reservationStart))
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
                    TablesId = GetCell(Schema.TableIdsColumn),
                    CustomerName = GetCell(Schema.CustomerNameColumn),
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

                var existingPhone = GetCell(Schema.CustomerPhoneColumn);
                if (!string.Equals(existingPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseSheetDateTime(GetCell(Schema.ScheduledAtColumn), out var reservationStart))
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
                    TablesId = GetCell(Schema.TableIdsColumn),
                    CustomerName = GetCell(Schema.CustomerNameColumn),
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
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var strategy = _strategyResolver.Resolve(_tenant.BusinessType);
            var row = strategy.BuildReservationRow(reservation, scheduledAt).ToList();
            var range = GetReservationRowRange(sheetRowNumber, out var columnCount);

            while (row.Count < columnCount)
            {
                row.Add(string.Empty);
            }

            var valueRange = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    row
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
            var range = GetReservationRowRange(sheetRowNumber, out _);

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

                var existingPhone = GetCell(Schema.CustomerPhoneColumn);
                if (!string.Equals(existingPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseSheetDateTime(GetCell(Schema.ScheduledAtColumn), out var reservationStart))
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

            // Строку формирует стратегия бизнес-типа (ресторан / автомойка).
            var strategy = _strategyResolver.Resolve(_tenant.BusinessType);
            var newRow = strategy.BuildReservationRow(reservation, scheduledAt);

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
            var range = $"{Schema.ReservationsSheetName}!{Schema.ReminderSentColumnLetter}{sheetRowNumber}";

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

                var strategy = _strategyResolver.Resolve(_tenant.BusinessType);
                var candidate = strategy.MapReminderCandidate(row, i + 2, Schema);
                if (candidate is not null)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        private string GetReservationRowRange(int sheetRowNumber, out int columnCount)
        {
            var separatorIndex = ReservationsAppendRange.LastIndexOf('!');
            var sheetName = separatorIndex >= 0
                ? ReservationsAppendRange[..separatorIndex]
                : Schema.ReservationsSheetName;
            var columnRange = separatorIndex >= 0
                ? ReservationsAppendRange[(separatorIndex + 1)..]
                : ReservationsAppendRange;
            var columns = columnRange.Split(':', 2);
            var firstColumn = NormalizeColumnName(columns[0]);
            var lastColumn = NormalizeColumnName(columns.Length > 1 ? columns[1] : columns[0]);

            columnCount = GetColumnNumber(lastColumn) - GetColumnNumber(firstColumn) + 1;
            return $"{sheetName}!{firstColumn}{sheetRowNumber}:{lastColumn}{sheetRowNumber}";
        }

        private static string NormalizeColumnName(string value) =>
            new string(value.Where(char.IsLetter).ToArray()).ToUpperInvariant();

        private static int GetColumnNumber(string columnName)
        {
            var result = 0;
            foreach (var character in columnName)
            {
                result = result * 26 + character - 'A' + 1;
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
            var org = _tenant.Organization;

            var credentialsJson = org is not null
                ? org.CredentialsJson
                : _config["GoogleSheets:CredentialsJson"];
            if (!string.IsNullOrWhiteSpace(credentialsJson))
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(credentialsJson));
                return CredentialFactory
                    .FromStream<ServiceAccountCredential>(stream)
                    .ToGoogleCredential()
                    .CreateScoped(Scopes);
            }

            var jsonPath = org is not null
                ? org.CredentialsJsonPath
                : _config["GoogleSheets:CredentialsJsonPath"];
            if (!string.IsNullOrWhiteSpace(jsonPath))
            {
                using var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read);
                return CredentialFactory
                    .FromStream<ServiceAccountCredential>(stream)
                    .ToGoogleCredential()
                    .CreateScoped(Scopes);
            }

            var scope = org is null ? "GoogleSheets" : $"organization '{org.Id}'";
            throw new InvalidOperationException(
                $"Google Sheets credentials must be configured for {scope}.");
        }

        private string GetSpreadsheetId()
        {
            var org = _tenant.Organization;
            if (org is not null)
            {
                return !string.IsNullOrWhiteSpace(org.SpreadsheetId)
                    ? org.SpreadsheetId
                    : throw new InvalidOperationException(
                        $"Spreadsheet id is not configured for organization '{org.Id}'.");
            }

            return _config["GoogleSheets:SpreadsheetId"]
                ?? throw new InvalidOperationException(
                    "GoogleSheets:SpreadsheetId is not configured.");
        }

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
