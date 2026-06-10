using System.Globalization;

namespace table_reservations.Constants
{
    public static class ReservationDateTime
    {
        public const string Format = "dd/MM/yyyy HH:mm";

        public static bool TryParse(string value, out DateTime result) =>
            DateTime.TryParseExact(
                value,
                Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result);
    }
}
