using Microsoft.AspNetCore.Mvc;
using table_reservations.Models;
using table_reservations.Constants;
using table_reservations.Services;

namespace table_reservations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TablesController : ControllerBase
    {   
        private readonly IGoogleSheetsService _sheets;
        
        public TablesController(IGoogleSheetsService sheets) => _sheets = sheets;

        [HttpGet]
public async Task<IActionResult> GetTables([FromQuery] string? scheduledAt, CancellationToken ct)
{
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
    }
}
