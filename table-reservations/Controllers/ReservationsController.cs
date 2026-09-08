using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.BusinessTypes;
using table_reservations.Services.Tenancy;

namespace table_reservations.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReservationsController(
    IReservationRepository reservations, IWhatsAppNotificationService whatsApp,
    TenantContext tenant, IBusinessTypeStrategyResolver strategies,
    ILogger<ReservationsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetReservations([FromQuery] string? date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) || day == DateOnly.MinValue || day == DateOnly.MaxValue)
            return BadRequest(new { message = "Укажите корректную дату в формате yyyy-MM-dd." });

        return Ok(await reservations.GetReservationsAsync(day, ct));
    }

    [HttpPost]
    public async Task<IActionResult> CreateReservation([FromBody] ReservationInfo request, CancellationToken ct)
    {
        var strategy = strategies.Resolve(tenant.BusinessType);
        var validation = strategy.ValidateCreate(request);
        if (!validation.IsValid) return BadRequest(new { message = validation.Error });
        var result = await reservations.BookAsync(request, validation.ScheduledAt, ct);
        // Notification failures must not turn a committed booking into an apparent failure.
        var customerSent = false;
        var adminSent = false;
        try
        {
            IReadOnlyList<TableInfo> tables = tenant.BusinessType == BusinessType.Restaurant
                ? await reservations.GetTablesAsync(validation.ScheduledAt, ct) : [];
            (customerSent, adminSent) = await whatsApp.SendReservationNotificationsAsync(
                request, validation.ScheduledAt, strategy.BuildNotificationLabel(request, tables), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reservation {ReservationId} committed, notification failed.", result.Id);
        }
        return Ok(new {
            success = true, reservationId = result.Id, result.Overwritten,
            result.BoxId, result.TotalKzt, result.DurationMinutes,
            message = result.Overwritten ? "Бронь перезаписана." : "Бронь создана.",
            whatsAppSent = customerSent, adminWhatsAppSent = adminSent,
            update = new { reservationId = result.Id }
        });
    }
}
