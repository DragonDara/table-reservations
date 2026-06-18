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
                request.TableId <= 0 ||
                string.IsNullOrWhiteSpace(request.CustomerName) ||
                string.IsNullOrWhiteSpace(request.CustomerPhone) ||
                string.IsNullOrWhiteSpace(request.DateTime) ||
                string.IsNullOrWhiteSpace(request.Section))
            {
                return BadRequest("Некорректные данные бронирования.");
            }

            if (!Enum.IsDefined(request.Type))
            {
                return BadRequest("Некорректный тип столика. Допустимые значения: Обычный, VIP.");
            }

            if (!ReservationDateTime.TryParse(request.DateTime, out var dateTime))
            {
                return BadRequest($"Некорректный формат dateTime. Ожидается {ReservationDateTime.Format}.");
            }

            if (dateTime < DateTime.Now.AddMinutes(-5))
            {
                return BadRequest("Некорректные данные бронирования.");
            }

            if (request.Duration < 1 || request.Duration > 5)
            {
                return BadRequest("duration должна быть от 1 до 5.");
            }

            if (await _sheets.IsReservationTakenAsync(
                    request.TableId,
                    dateTime,
                    request.Duration,
                    ct))
            {
                return Conflict("Этот стол уже занят на указанное время.");
            }

            var appendResp = await _sheets.AppendReservationAsync(request, dateTime, ct);

            var (customerSent, adminSent) = await _whatsApp.SendReservationNotificationsAsync(
                request, dateTime, ct);

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
