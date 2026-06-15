using table_reservations.Constants;

namespace table_reservations.Models
{
    public class ReservationInfo
    {
        public int TableId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string DateTime { get; set; } = string.Empty;
        public int Duration { get; set; } = 1;
        public string Section { get; set; } = string.Empty;
        public TableType Type { get; set; } = TableType.Обычный;
    }
}
