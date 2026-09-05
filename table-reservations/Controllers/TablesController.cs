using Microsoft.AspNetCore.Mvc;
using table_reservations.Models;
using table_reservations.Constants;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.Tenancy;

namespace table_reservations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TablesController : ControllerBase
    {   
        private readonly IReservationRepository _reservations;
        private readonly TenantContext _tenant;

        public TablesController(IReservationRepository reservations, TenantContext tenant)
        {
            _reservations = reservations;
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

    var tables = await _reservations.GetTablesAsync(scheduledAt: targetTime, ct: ct);
    return Ok(tables);
}

        [HttpGet("slots")]
        public async Task<IActionResult> GetSlots([FromQuery] string date, CancellationToken ct)
        {
            if (_tenant.BusinessType != BusinessType.Restaurant) return NotFound();
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day))
                return BadRequest(new { message = "Укажите дату в формате yyyy-MM-dd." });
            return Ok((await _reservations.GetAvailableSlotsAsync(day, ReservationDateTime.KazakhstanNow(), ct))
                .Select(BookingRules.Wire));
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

            var isTaken = await _reservations.IsReservationTakenAsync(tableId.ToString(), dateTime, ct: ct);

            return Ok(new
            {
                id = tableId.ToString(),
                seats = 0,
                status = isTaken ? "occupied" : "free",
            });
        }
    }
}
