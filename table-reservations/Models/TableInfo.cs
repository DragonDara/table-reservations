using table_reservations.Constants;

namespace table_reservations.Models
{
    public class TableInfo
    {
        public int Id { get; set; }
        public TableType Type { get; set; } = TableType.Обычный;
        public int Capacity { get; set; }
        public string Status { get; set; } = TableStatuses.Free;
    }
}
