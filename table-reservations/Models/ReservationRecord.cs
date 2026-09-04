namespace table_reservations.Models
{
    /// <summary>
    /// Persistence entity mirroring a row of the <c>reservations</c> table in Turso.
    /// Replaces the previous sheet-row / cell based representation.
    /// </summary>
    public sealed class ReservationRecord
    {
        public long Id { get; set; }
        public string OrganizationId { get; set; } = string.Empty;
        public string TableIds { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public DateTime ScheduledAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool RemindBeforeHour { get; set; }
        public bool ReminderSent { get; set; }
        public string PlateNumber { get; set; } = string.Empty;
        public string WashServiceType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
