namespace table_reservations.Models
{
    public sealed class ReminderCandidate
    {
        public long Id { get; init; }
        public ReservationInfo Reservation { get; init; } = new();
        public bool RemindBeforeHour { get; init; }
        public bool ReminderSent { get; init; }
    }
}
