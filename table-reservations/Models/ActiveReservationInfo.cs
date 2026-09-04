namespace table_reservations.Models
{
    public class ActiveReservationInfo
    {
        public int SheetRowNumber { get; set; }
        public string TablesId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string ScheduledAt { get; set; } = string.Empty;
        public DateTime ScheduledAtValue { get; set; }
    }
}
