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
            CancellationToken ct = default)
        {
            var service = CreateService();
            var spreadsheetId = GetSpreadsheetId();
            var strategy = _strategyResolver.Resolve(_tenant.BusinessType);

            var response = await service.Spreadsheets.Values
                .Get(spreadsheetId, ReservationsRange)
                .ExecuteAsync(ct);

            return (response.Values ?? new List<IList<object>>())
                .Any(row => strategy.HasConflict(reservation, scheduledAt, row, Schema));
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

                string GetCell(int idx) =>
                    row.Count > idx ? row[idx]?.ToString()?.Trim() ?? "" : "";

                var strategy = _strategyResolver.Resolve(_tenant.BusinessType);
                var candidate = strategy.MapReminderCandidate(row, i + 2, Schema);
                if (candidate is not null)
                {
                    result.Add(candidate);
                }
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
