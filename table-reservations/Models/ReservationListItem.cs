namespace table_reservations.Models;

// Public schedule fields only. Customer contact details and plates stay private.
public sealed record ReservationListItem(
    string ScheduledAt, string TablesId, string WashServiceType, string? EndsAt = null, string? BoxId = null);
