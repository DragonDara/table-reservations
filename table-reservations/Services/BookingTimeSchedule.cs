using System.Globalization;
using table_reservations.Configuration;

namespace table_reservations.Services;

/// <summary>
/// Parses organization booking settings and produces the exact daily slots
/// accepted by both the API and the browser.
/// </summary>
public static class BookingTimeSchedule
{
    private const string TimeFormat = "HH:mm";
    private const int MinutesPerDay = 24 * 60;

    public static IReadOnlyList<string> GetAvailableSlots(BookingTimeOptions options)
    {
        var (startMinutes, endMinutes, slotDuration) = Parse(options);
        var slots = new List<string>();

        for (var minute = startMinutes; minute < endMinutes; minute += slotDuration)
        {
            slots.Add(FormatMinutes(minute % MinutesPerDay));
        }

        return slots;
    }

    public static bool IsAvailable(BookingTimeOptions options, DateTime scheduledAt)
    {
        var selectedTime = scheduledAt.ToString(TimeFormat, CultureInfo.InvariantCulture);
        return GetAvailableSlots(options).Contains(selectedTime, StringComparer.Ordinal);
    }

    public static string Describe(BookingTimeOptions options) =>
        $"{options.StartTime}–{options.EndTime}, шаг {options.SlotDurationMinutes} мин.";

    private static (int StartMinutes, int EndMinutes, int SlotDuration) Parse(BookingTimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TimeOnly.TryParseExact(
                options.StartTime,
                TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var start))
        {
            throw new InvalidOperationException(
                $"Booking start time '{options.StartTime}' must use the {TimeFormat} format.");
        }

        if (!TimeOnly.TryParseExact(
                options.EndTime,
                TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var end))
        {
            throw new InvalidOperationException(
                $"Booking end time '{options.EndTime}' must use the {TimeFormat} format.");
        }

        if (options.SlotDurationMinutes <= 0 || options.SlotDurationMinutes > MinutesPerDay)
        {
            throw new InvalidOperationException(
                "Booking slot duration must be between 1 and 1440 minutes.");
        }

        var startMinutes = start.Hour * 60 + start.Minute;
        var endMinutes = end.Hour * 60 + end.Minute;
        if (endMinutes <= startMinutes)
        {
            endMinutes += MinutesPerDay;
        }

        return (startMinutes, endMinutes, options.SlotDurationMinutes);
    }

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:00}:{minutes % 60:00}";
}
