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
            CancellationToken ct,
            [FromQuery] int duration = 1
            )
        {
            if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
            {
                return BadRequest("Укажите date и time");
            }

            if (duration < 1 || duration > 3)
            {
                return BadRequest("duration должна быть от 1 до 3");
            }

            var tables = await _sheets.GetTablesAsync(date, time, duration, ct);

            if (tables.Count == 0)
            {
                return NotFound("Данные о столах не найдены.");
            }

            return Ok(tables);
        }
    }
}
