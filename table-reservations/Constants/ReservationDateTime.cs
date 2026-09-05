using System.Globalization;
using System.Runtime.CompilerServices;

namespace table_reservations.Constants
{
    public static class ReservationDateTime
    {
        public const string Format = "dd/MM/yyyy HH:mm";
        // Future bookings use Kazakhstan's nationwide UTC+05:00 (since March 2024).
        // A fixed zone avoids stale Windows/ICU data reporting Almaty as UTC+06:00.
        public static readonly TimeZoneInfo KazakhstanZone =
            TimeZoneInfo.CreateCustomTimeZone("KazakhstanBooking", TimeSpan.FromHours(5), "Kazakhstan", "Kazakhstan");

        private static readonly string[] InputFormats =
        {
            "dd/MM/yyyy HH:mm",      // Sheets / старые данные
            "dd.MM.yyyy HH:mm",      // Sheets, если ячейка отформатирована как дата (точки по рус. локали)
            "yyyy-MM-ddTHH:mm",      // datetime-local
            "yyyy-MM-ddTHH:mm:ss",   // на всякий
            "yyyy-MM-dd HH:mm",
        };

        public static DateTime KazakhstanNow()
        {
            var utcNow = DateTime.UtcNow;
            return TimeZoneInfo.ConvertTimeFromUtc(utcNow, KazakhstanZone);
        }

        public static bool TryParse(string value, out DateTime result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = default;
                return false;
            }

            if (!DateTime.TryParseExact(
                value.Trim(),
                InputFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
            {
                result = default;
                return false;
            }

            result = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            return true;
        }
    }
}
