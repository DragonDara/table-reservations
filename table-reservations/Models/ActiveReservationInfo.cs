namespace table_reservations.Models
{
    public class ActiveReservationInfo
    {
        public string Id { get; set; } = string.Empty;
        public string TablesId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string ScheduledAt { get; set; } = string.Empty;
        public DateTime ScheduledAtValue { get; set; }
    }
}
