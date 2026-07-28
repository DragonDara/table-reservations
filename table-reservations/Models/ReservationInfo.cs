using table_reservations.Constants;

namespace table_reservations.Models
{
    public class ReservationInfo
    {
        public string TablesId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string ScheduledAt { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public int RemindBeforeHour { get; set; }
    }
}
