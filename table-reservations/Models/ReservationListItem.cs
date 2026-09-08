namespace table_reservations.Models;

// Reservation details for the employee list.
public sealed record ReservationListItem(
    string ScheduledAt, string TablesId, string WashServiceType, string? EndsAt = null, string? BoxId = null,
    string CustomerName = "", string CustomerPhone = "");
