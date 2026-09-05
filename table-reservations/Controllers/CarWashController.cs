using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Models;
using table_reservations.Services;

namespace table_reservations.Controllers;

[ApiController]
[Route("api/carwash")]
public class CarWashController(IReservationRepository reservations, IOptions<TursoOptions> options) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct) =>
        Ok(await reservations.GetCarWashCatalogAsync(ct));

    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] CarWashSelection selection, CancellationToken ct) =>
        Ok(BookingRules.Quote(await reservations.GetCarWashCatalogAsync(ct),
            selection.VehicleCategoryId, selection.ServiceIds, options.Value.DefaultCarWashMinutes));

    public record AvailabilityRequest(string Date, string VehicleCategoryId, string[] ServiceIds);

    [HttpPost("availability")]
    public async Task<IActionResult> Availability([FromBody] AvailabilityRequest request, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return BadRequest(new { message = "Укажите дату в формате yyyy-MM-dd." });
        return Ok(await reservations.GetCarWashAvailabilityAsync(date, new(request.VehicleCategoryId, request.ServiceIds), ct));
    }
}
