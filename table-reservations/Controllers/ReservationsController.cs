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
    IReservationRepository reservations, IReservationNotificationService notifications,
    TenantContext tenant, IBusinessTypeStrategyResolver strategies,
    ILogger<ReservationsController> logger) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetReservations([FromQuery] string? date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) || day == DateOnly.MinValue || day == DateOnly.MaxValue)
            return BadRequest(new { message = "Укажите корректную дату в формате yyyy-MM-dd." });

        return Ok(await reservations.GetReservationsAsync(day, ct));
    }

    [HttpPost]
    [EndpointDescription("POS-enabled restaurants require a UUID Idempotency-Key. Retry the same payload with the same key. A successful iiko create response is confirmed locally without waiting for terminal approval; HTTP 202 means the dispatch outcome is still unknown.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<RestaurantBookingResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateReservation([FromBody] ReservationInfo request, CancellationToken ct,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        var strategy = strategies.Resolve(tenant.BusinessType);
        var validation = strategy.ValidateCreate(request);
        if (!validation.IsValid) return BadRequest(new { message = validation.Error });
        var result = await reservations.BookWithIdempotencyAsync(request, validation.ScheduledAt, idempotencyKey, ct);
        if (result.PosManaged)
        {
            if (result.Status == "Confirmed") await NotifyPosBookingAsync(result, ct);
            return PosResponse(result);
        }
        // Notification failures must not turn a committed booking into an apparent failure.
        var customerSent = false;
        var adminSent = false;
        var telegramSent = false;
        try
        {
            IReadOnlyList<TableInfo> tables = tenant.BusinessType == BusinessType.Restaurant
                ? await reservations.GetTablesAsync(validation.ScheduledAt, ct) : [];
            var delivery = await notifications.SendReservationNotificationsAsync(
                request, validation.ScheduledAt, strategy.BuildNotificationLabel(request, tables), ct);
            (customerSent, adminSent, telegramSent) = delivery;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reservation {ReservationId} committed, notification failed.", result.Id);
        }
        return Ok(new {
            success = true, reservationId = result.Id,
            result.BoxId, result.TotalKzt, result.DurationMinutes,
            message = "Бронь создана.",
            whatsAppSent = customerSent, adminWhatsAppSent = adminSent,
            telegramSent,
            update = new { reservationId = result.Id }
        });
    }

    [HttpGet("{reservationId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<RestaurantBookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReservationStatus(Guid reservationId, CancellationToken ct)
    {
        var result = await reservations.GetBookingStatusAsync(reservationId.ToString(), ct);
        return result is null ? NotFound() : Ok(new RestaurantBookingResponse(result.Id, result.Status,
            result.Status == "Confirmed"));
    }

    [HttpPost("{reservationId:guid}/check")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<RestaurantBookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<RestaurantBookingResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EndpointDescription("Explicitly check one unresolved POS booking. This endpoint does not schedule retries or poll in the background.")]
    public async Task<IActionResult> CheckReservationStatus(Guid reservationId, CancellationToken ct)
    {
        var result = await reservations.CheckPosBookingAsync(reservationId.ToString(), ct);
        if (result is null) return NotFound();
        if (result.Status == "Confirmed") await NotifyPosBookingAsync(result, ct);
        return PosResponse(result);
    }

    private async Task NotifyPosBookingAsync(BookingResult result, CancellationToken ct)
    {
        try
        {
            await reservations.NotifyPosBookingAsync(result.Id, notifications, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reservation {ReservationId} committed, notification failed.", result.Id);
        }
    }

    private IActionResult PosResponse(BookingResult result)
    {
        var body = new RestaurantBookingResponse(result.Id, result.Status, result.Status == "Confirmed",
            result.Status == "Confirmed" ? "Бронь подтверждена." : result.Status == "Rejected" ? "Бронь отклонена." : "Проверяем результат отправки в кассу.");
        return result.Status == "Processing" ? AcceptedAtAction(nameof(GetReservationStatus), new { reservationId = result.Id }, body)
            : result.Status == "Rejected" ? Conflict(body) : Ok(body);
    }
}
