using System.Globalization;
using table_reservations.Configuration;
using table_reservations.Services;

namespace table_reservations.Constants
{
    public static class RestaurantSlotSchedule
    {
        public const int BookingDays = 7;
        public const int MinimumLeadMinutes = 5;

        public static bool IsBookableDate(DateOnly date, DateTime now)
        {
            var today = DateOnly.FromDateTime(now);
            return date >= today && date < today.AddDays(BookingDays);
        }

        public static IReadOnlyList<DateTime> GetCandidateSlots(
            DateOnly date, DateTime now, BookingTimeOptions bookingTime)
        {
            if (!IsBookableDate(date, now))
            {
                return Array.Empty<DateTime>();
            }

            var minimumStart = now.AddMinutes(MinimumLeadMinutes);
            var configuredSlots = BookingTimeSchedule.GetAvailableSlots(bookingTime);
            var opensAt = TimeOnly.ParseExact(bookingTime.StartTime, "HH:mm", CultureInfo.InvariantCulture);
            var slots = new List<DateTime>();

            foreach (var configuredSlot in configuredSlots)
            {
                var time = TimeOnly.ParseExact(configuredSlot, "HH:mm", CultureInfo.InvariantCulture);
                // The selected date identifies the opening day of the shift.
                var slotDate = time < opensAt ? date.AddDays(1) : date;
                var slot = slotDate.ToDateTime(time);
                if (slot >= minimumStart)
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
