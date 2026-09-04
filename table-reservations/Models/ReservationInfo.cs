namespace table_reservations.Models
{
    public class ReservationInfo : ReservationRequestBase
    {
        public string TablesId { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string? PlateNumber { get; set; }
        public string? WashServiceType { get; set; }

        /// <summary>
        /// Если true — заменить актуальную бронь по этому номеру телефона.
        /// </summary>
        public bool Overwrite { get; set; } = false;
    }
}
