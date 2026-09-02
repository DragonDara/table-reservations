using Microsoft.AspNetCore.Mvc;
using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Services;

namespace table_reservations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReservationsController : ControllerBase
    {
        private readonly IGoogleSheetsService _sheets;
        private readonly IWhatsAppNotificationService _whatsApp;

        public ReservationsController(IGoogleSheetsService sheets, IWhatsAppNotificationService whatsApp)
        {
            _sheets = sheets;
            _whatsApp = whatsApp;
        }

        [HttpPost]
        public async Task<IActionResult> CreateReservation([FromBody] ReservationInfo request, CancellationToken ct)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.TablesId) ||
                string.IsNullOrWhiteSpace(request.CustomerName) ||
                string.IsNullOrWhiteSpace(request.CustomerPhone) ||
                string.IsNullOrWhiteSpace(request.ScheduledAt) ||
                string.IsNullOrWhiteSpace(request.Section))
            {
                return BadRequest("Некорректные данные бронирования.");
            }

            if (!ReservationDateTime.TryParse(request.ScheduledAt, out var scheduledAt))
            {
                return BadRequest($"Некорректный формат dateTime. Ожидается {ReservationDateTime.Format}.");
            }

            var minAllowedTime = ReservationDateTime.KazakhstanNow().AddMinutes(5);
            if (scheduledAt < minAllowedTime)
            {
                return BadRequest($"Минимальное время брони — {minAllowedTime:HH:mm}. Выберите время позже.");
            }

            // Бар работает каждый день с 12:00 до 04:00 следующего дня.
            // Значит "рабочее" время суток — это [12:00, 24:00) или [00:00, 04:00).
            var time = scheduledAt.TimeOfDay;
            var opensAt = new TimeSpan(12, 0, 0);
            var closesAt = new TimeSpan(4, 0, 0);
            var isWithinWorkingHours = time >= opensAt || time < closesAt;
            if (!isWithinWorkingHours)
            {
                return BadRequest("Бронь доступна только на время работы бара: с 12:00 до 04:00.");
            }

            if (!_sheets.TryParseTableIds(request.TablesId, out var tableIds) || tableIds.Length == 0)
            {
                return BadRequest("Некорректный номер столика.");
            }

            if (tableIds.Length != tableIds.Distinct().Count())
            {
                return BadRequest("В одной броне нельзя указывать один и тот же номер столика несколько раз.");
            }

            var activeReservations = await _sheets.FindAllActiveReservationsByPhoneAsync(request.CustomerPhone, ct);
            var primaryActive = activeReservations
                .OrderBy(r => r.ScheduledAtValue)
                .FirstOrDefault();

            if (primaryActive is not null && !request.Overwrite)
            {
                return Conflict(new
                {
                    code = "EXISTING_RESERVATION",
                    message = "У вас уже есть актуальная бронь. Хотите перезаписать?",
                    existing = new
                    {
                        scheduledAt = primaryActive.ScheduledAt,
                        tablesId = primaryActive.TablesId,
                        customerName = primaryActive.CustomerName
                    }
                });
            }

            int? excludeRow = request.Overwrite && primaryActive is not null
                ? primaryActive.SheetRowNumber
                : null;

            if (await _sheets.IsReservationTakenAsync(
                    request.TablesId,
                    scheduledAt,
                    excludeRow,
                    ct))
            {
                return Conflict(new
                {
                    code = "TABLE_TAKEN",
                    message = "Один из выбранных столов уже занят на указанное время."
                });
            }

            object? update = null;

            if (request.Overwrite && primaryActive is not null)
            {
                await _sheets.OverwriteReservationAsync(
                    primaryActive.SheetRowNumber,
                    request,
                    scheduledAt,
                    ct);

                foreach (var extra in activeReservations.Where(r => r.SheetRowNumber != primaryActive.SheetRowNumber))
                {
                    await _sheets.ClearReservationRowAsync(extra.SheetRowNumber, ct);
                }

                update = new { overwrittenRow = primaryActive.SheetRowNumber };
            }
            else
            {
                var appendResp = await _sheets.AppendReservationAsync(request, scheduledAt, ct);
                update = appendResp.Updates;
            }

            var tables = await _sheets.GetTablesAsync(scheduledAt: scheduledAt, ct: ct);
            var typeLabel = string.Join(", ",
                tables
                    .Where(t => tableIds.Contains(t.Id))
                    .Select(t => t.Type == TableType.VIP ? "VIP" : "Обычный")
                    .Distinct());

            var (customerSent, adminSent) = await _whatsApp.SendReservationNotificationsAsync(
                request, scheduledAt, typeLabel, ct);

            return Ok(new
            {
                message = request.Overwrite ? "Бронь перезаписана." : "Бронь создана.",
                overwritten = request.Overwrite && primaryActive is not null,
                whatsAppSent = customerSent,
                adminWhatsAppSent = adminSent,
                update
            });
        }
    }
}
