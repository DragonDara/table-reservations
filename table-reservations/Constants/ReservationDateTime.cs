using System.Globalization;
using System.Runtime.CompilerServices;

namespace table_reservations.Constants
{
    public static class ReservationDateTime
    {
        public const string Format = "dd/MM/yyyy HH:mm";

        private static readonly string[] InputFormats =
    {
        "dd/MM/yyyy HH:mm",      // Sheets / старые данные
        "dd.MM.yyyy HH:mm",      // Sheets, если ячейка отформатирована как дата (точки по рус. локали)
        "yyyy-MM-ddTHH:mm",      // datetime-local
        "yyyy-MM-ddTHH:mm:ss",   // на всякий
        "yyyy-MM-dd HH:mm",
    };

        public static bool TryParse(string value, out DateTime result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = default;
                return false;
            }
            return DateTime.TryParseExact(
                value.Trim(),
                InputFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out result);
        }
    }
}
