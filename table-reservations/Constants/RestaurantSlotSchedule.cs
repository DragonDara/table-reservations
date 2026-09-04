namespace table_reservations.Constants
{
    public static class RestaurantSlotSchedule
    {
        public const int BookingDays = 7;
        public const int IntervalMinutes = 30;
        public const int MinimumLeadMinutes = 5;

        private static readonly TimeSpan OpensAt = new(12, 0, 0);
        private static readonly TimeSpan ClosesAt = new(4, 0, 0);

        public static bool IsBookableDate(DateOnly date, DateTime now)
        {
            var today = DateOnly.FromDateTime(now);
            return date >= today && date < today.AddDays(BookingDays);
        }

        public static IReadOnlyList<DateTime> GetCandidateSlots(DateOnly date, DateTime now)
        {
            if (!IsBookableDate(date, now))
            {
                return Array.Empty<DateTime>();
            }

            var minimumStart = now.AddMinutes(MinimumLeadMinutes);
            var startOfDay = date.ToDateTime(TimeOnly.MinValue);
            var slots = new List<DateTime>();

            for (var minutes = 0; minutes < 24 * 60; minutes += IntervalMinutes)
            {
                var slot = startOfDay.AddMinutes(minutes);
                var time = slot.TimeOfDay;
                if ((time >= OpensAt || time < ClosesAt) && slot >= minimumStart)
                {
                    slots.Add(DateTime.SpecifyKind(slot, DateTimeKind.Unspecified));
                }
            }

            return slots;
        }

        public static bool HasAvailableTable(
            DateTime slot,
            IEnumerable<IEnumerable<DateTime>> reservationsByTable)
        {
            var requestedEnd = slot.AddHours(ReservationDuration.Hours);
            return reservationsByTable.Any(reservations =>
                reservations.All(existingStart =>
                {
                    var existingEnd = existingStart.AddHours(ReservationDuration.Hours);
                    return existingStart >= requestedEnd || existingEnd <= slot;
                }));
        }
    }
}
