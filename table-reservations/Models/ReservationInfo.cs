namespace table_reservations.Models
{
    public class ReservationInfo
    {
        public int TableId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string DateTime { get; set; } = string.Empty;
        public int Duration { get; set; } = 1;
    }
}
