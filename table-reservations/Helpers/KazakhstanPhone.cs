using System.Text.RegularExpressions;
using table_reservations.Models;

namespace table_reservations.Helpers;

/// <summary>
/// Kazakhstan mobile numbers: +7 and a known DEF code (70x / 747 / 77x).
/// </summary>
public static partial class KazakhstanPhone
{
    public const string InvalidMessage =
        "Укажите казахстанский мобильный номер: +7 700 000 00 00.";

    [GeneratedRegex(@"^\+7(70[0-8]|747|77[1578])\d{7}$")]
    private static partial Regex MobilePattern();

    public static string Normalize(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 10) digits = "7" + digits;
        if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
        return "+" + digits;
    }

    public static bool IsValidMobile(string normalizedPhone) =>
        MobilePattern().IsMatch(normalizedPhone);

    public static string NormalizeAndValidate(string phone)
    {
        var normalized = Normalize(phone);
        if (!IsValidMobile(normalized))
            throw new BookingException(InvalidMessage);
        return normalized;
    }
}
