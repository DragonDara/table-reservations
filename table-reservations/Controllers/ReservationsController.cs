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

            if (await _sheets.IsPhoneAlreadyReservedAsync(request.CustomerPhone, ct))
            {
                return Conflict("На этот номер телефона уже есть бронь. Один номер телефона = один заказ.");
            }

            if (await _sheets.HasConflictAsync(request, scheduledAt, ct))
            {
                return Conflict("Выбранный ресурс уже занят на указанное время.");
            }

            if (await _sheets.HasReservationForPhoneAsync(
                    request.CustomerPhone,
                    scheduledAt,
                    ct))
            {
                return Conflict("На данный номер уже есть бронь.");
            }

            var appendResp = await _sheets.AppendReservationAsync(request, scheduledAt, ct);

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
                message = "Бронь создана.",
                whatsAppSent = customerSent,
                adminWhatsAppSent = adminSent,
                update = appendResp.Updates
            });
        }

    }
}
