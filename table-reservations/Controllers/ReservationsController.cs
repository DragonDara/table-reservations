using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
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

            var minAllowedTime = DateTime.UtcNow.AddHours(5).AddMinutes(5);
                if (scheduledAt < minAllowedTime)
                {
                    return BadRequest($"Минимальное время брони — {minAllowedTime:HH:mm}. Выберите время позже.");
                }

            if (!_sheets.TryParseTableIds(request.TablesId, out var tableIds) || tableIds.Length == 0)
            {
                return BadRequest("Нет номера столика.");
            }

            if (await _sheets.IsReservationTakenAsync(
                    request.TablesId,
                    scheduledAt,
                    ct))
            {
                return Conflict("Этот стол уже занят на указанное время.");
            }

            var appendResp = await _sheets.AppendReservationAsync(request, scheduledAt, ct);

            var tables = await _sheets.GetTablesAsync(ct);
            var typeLabel = string.Join(", ",
                tables
                    .Where(t => tableIds.Contains(t.Id))
                    .Select(t => t.Type == TableType.VIP ? "VIP" : "Обычный")
                    .Distinct());

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
