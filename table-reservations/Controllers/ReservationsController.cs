using Microsoft.AspNetCore.Mvc;
using table_reservations.Configuration;
using table_reservations.Models;
using table_reservations.Services;
using table_reservations.Services.BusinessTypes;
using table_reservations.Services.Tenancy;

namespace table_reservations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReservationsController : ControllerBase
    {
        private readonly IGoogleSheetsService _sheets;
        private readonly IWhatsAppNotificationService _whatsApp;
        private readonly TenantContext _tenant;
        private readonly IBusinessTypeStrategyResolver _strategyResolver;

        public ReservationsController(
            IGoogleSheetsService sheets,
            IWhatsAppNotificationService whatsApp,
            TenantContext tenant,
            IBusinessTypeStrategyResolver strategyResolver)
        {
            _sheets = sheets;
            _whatsApp = whatsApp;
            _tenant = tenant;
            _strategyResolver = strategyResolver;
        }

        [HttpPost]
        public async Task<IActionResult> CreateReservation([FromBody] ReservationInfo request, CancellationToken ct)
        {
            if (request == null)
            {
                return BadRequest("Некорректные данные бронирования.");
            }

            // Правила и обязательные поля зависят от типа бизнеса (ресторан / автомойка).
            var strategy = _strategyResolver.Resolve(_tenant.BusinessType);

            var validation = strategy.ValidateCreate(request);
            if (!validation.IsValid)
            {
                return BadRequest(validation.Error);
            }

            var scheduledAt = validation.ScheduledAt;

            var bookingTime = _tenant.Organization?.BookingTime ?? new BookingTimeOptions();
            if (!BookingTimeSchedule.IsAvailable(bookingTime, scheduledAt))
            {
                return BadRequest(
                    $"Выбранное время недоступно. Доступное окно: {BookingTimeSchedule.Describe(bookingTime)}");
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

            if (await _sheets.HasConflictAsync(
                    request,
                    scheduledAt,
                    excludeRow,
                    ct))
            {
                return Conflict(new
                {
                    code = "TABLE_TAKEN",
                    message = "Выбранный ресурс уже занят на указанное время."
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

            IReadOnlyList<TableInfo> tables = Array.Empty<TableInfo>();
            if (strategy.Type == Models.Tenancy.BusinessType.Restaurant)
            {
                tables = await _sheets.GetTablesAsync(scheduledAt: scheduledAt, ct: ct);
            }

            var typeLabel = strategy.BuildNotificationLabel(request, tables);

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
