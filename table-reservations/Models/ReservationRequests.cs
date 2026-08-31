namespace table_reservations.Models
{
    public abstract class ReservationRequestBase
    {
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string ScheduledAt { get; set; } = string.Empty;
        public bool RemindBeforeHour { get; set; }
    }

    public sealed class RestaurantReservationRequest : ReservationRequestBase
    {
        public string TablesId { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
    }

    public sealed class CarWashReservationRequest : ReservationRequestBase
    {
        public string PlateNumber { get; set; } = string.Empty;
        public string WashServiceType { get; set; } = string.Empty;
    }
}
