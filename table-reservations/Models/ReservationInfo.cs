namespace table_reservations.Models
{
    public class ReservationInfo : ReservationRequestBase
    {
        public string TablesId { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string? PlateNumber { get; set; }
        public string? WashServiceType { get; set; }
        public string? VehicleCategoryId { get; set; }
        public string[] ServiceIds { get; set; } = [];
    }
}
