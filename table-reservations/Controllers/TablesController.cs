using Microsoft.AspNetCore.Mvc;
using table_reservations.Models;
using table_reservations.Constants;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.Tenancy;
using System.Globalization;

namespace table_reservations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TablesController : ControllerBase
    {   
        private readonly IGoogleSheetsService _sheets;
        private readonly TenantContext _tenant;
        
        public TablesController(IGoogleSheetsService sheets, TenantContext tenant)
        {
            _sheets = sheets;
            _tenant = tenant;
        }

        [HttpGet]
public async Task<IActionResult> GetTables([FromQuery] string? scheduledAt, CancellationToken ct)
{
    if (_tenant.BusinessType != BusinessType.Restaurant)
    {
        return NotFound();
    }

    DateTime? targetTime = null;

    if (!string.IsNullOrWhiteSpace(scheduledAt))
    {
        if (!ReservationDateTime.TryParse(scheduledAt, out var parsed))
        {
            return BadRequest($"Некорректный формат scheduledAt. Ожидается {ReservationDateTime.Format}.");
        }
        targetTime = parsed;
    }

    var tables = await _sheets.GetTablesAsync(scheduledAt: targetTime, ct: ct);
    return Ok(tables);
}

        [HttpGet("{tableId}/availability")]
        public async Task<IActionResult> GetTableAvailability(
            int tableId,
            [FromQuery] string scheduledAt,
            CancellationToken ct)
        {
            if (_tenant.BusinessType != BusinessType.Restaurant)
            {
                return NotFound();
            }

            if (tableId <= 0 || string.IsNullOrWhiteSpace(scheduledAt))
            {
                return BadRequest("Укажите tableId и scheduledAt.");
            }

            if (!ReservationDateTime.TryParse(scheduledAt, out var dateTime))
            {
                return BadRequest($"Некорректный формат scheduledAt. Ожидается {ReservationDateTime.Format}.");
            }

            var isTaken = await _sheets.IsReservationTakenAsync(tableId.ToString(), dateTime, ct: ct);

            return Ok(new
            {
                id = tableId.ToString(),
                seats = 0,
                status = isTaken ? "occupied" : "free",
            });
        }

        [HttpGet("slots")]
        public async Task<IActionResult> GetAvailableSlots(
            [FromQuery] string date,
            CancellationToken ct)
        {
            if (_tenant.BusinessType != BusinessType.Restaurant)
            {
                return NotFound();
            }

            if (!DateOnly.TryParseExact(
                    date,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var requestedDate))
            {
                return BadRequest("Некорректный формат date. Ожидается yyyy-MM-dd.");
            }

            var now = ReservationDateTime.KazakhstanNow();
            if (!RestaurantSlotSchedule.IsBookableDate(requestedDate, now))
            {
                return BadRequest($"Доступна запись только на ближайшие {RestaurantSlotSchedule.BookingDays} дней.");
            }

            var slots = await _sheets.GetAvailableSlotsAsync(requestedDate, now, ct);
            return Ok(slots.Select(slot => slot.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)));
        }
    }
}
