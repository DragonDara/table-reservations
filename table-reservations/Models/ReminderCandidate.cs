namespace table_reservations.Models
{
    public sealed class ReminderCandidate
    {
        public string Id { get; init; } = string.Empty;
        public ReservationInfo Reservation { get; init; } = new();
        public bool RemindBeforeHour { get; init; }
        public bool ReminderSent { get; init; }
    }
}
