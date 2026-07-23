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
        public async Task<IActionResult> GetTables(
            [FromQuery] string date,
            [FromQuery] string time,
            [FromQuery] int duration = 1,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
            {
                return BadRequest("Укажите date и time");
            }

            if (duration < 1 || duration > 5)
            {
                return BadRequest("duration должна быть от 1 до 5");
            }

            if (!ReservationDateTime.TryParse($"{date} {time}", out _))
            {
                return BadRequest($"Некорректный формат date/time. Ожидается {ReservationDateTime.Format}.");
            }

            var tables = await _sheets.GetTablesAsync(date, time, duration, ct);

            if (tables.Count == 0)
            {
                return Ok(Array.Empty<TableInfo>());
            }

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

            var isTaken = await _sheets.IsReservationTakenAsync(tableId, dateTime, 1, ct);

            return Ok(new
            {
                id = tableId.ToString(),
                seats = 0,
                status = isTaken ? "occupied" : "free",
            });
        }
    }
}
