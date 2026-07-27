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
        public async Task<IActionResult> GetTables(CancellationToken ct)
        {

            var tables = await _sheets.GetTablesAsync(ct);

            return Ok(tables);
        }
    }
}
