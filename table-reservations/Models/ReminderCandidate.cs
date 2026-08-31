namespace table_reservations.Models
{
    public sealed class ReminderCandidate
    {
        public int SheetRowNumber { get; init; }
        public ReservationInfo Reservation { get; init; } = new();
        public string RemindBeforeHourCell { get; init; } = string.Empty;
        public string ReminderSentCell { get; init; } = string.Empty;
    }
}
