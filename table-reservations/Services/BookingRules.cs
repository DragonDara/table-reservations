using System.Globalization;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Models;

namespace table_reservations.Services;

public static class BookingRules
{
    public static readonly TimeZoneInfo Zone = ReservationDateTime.KazakhstanZone;
    public static string Store(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public static DateTime Read(string utc) => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(DateTime.ParseExact(utc, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Utc), Zone);
    public static string Wire(DateTime local) => local.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    public static bool Overlaps(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd) => start < otherEnd && otherStart < end;

    public static IReadOnlyList<DateTime> Slots(DateOnly date, BookingTimeOptions hours, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (date < today || date >= today.AddDays(7)) throw new BookingException("Доступна запись на ближайшие 7 дней.");
        var start = TimeOnly.ParseExact(hours.StartTime, "HH:mm", CultureInfo.InvariantCulture);
        return BookingTimeSchedule.GetAvailableSlots(hours).Select(value =>
        {
            var time = TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
            return date.AddDays(time < start ? 1 : 0).ToDateTime(time);
        }).Where(slot => slot >= now.AddMinutes(5)).ToArray();
    }
    public static bool FitsHours(DateTime start, int minutes, BookingTimeOptions hours)
    {
        var open = TimeOnly.ParseExact(hours.StartTime, "HH:mm", CultureInfo.InvariantCulture);
        var close = TimeOnly.ParseExact(hours.EndTime, "HH:mm", CultureInfo.InvariantCulture);
        var day = DateOnly.FromDateTime(start);
        if (close <= open && TimeOnly.FromDateTime(start) < open) day = day.AddDays(-1);
        var closing = day.AddDays(close <= open ? 1 : 0).ToDateTime(close);
        return start >= day.ToDateTime(open) && start.AddMinutes(minutes) <= closing;
    }
    public static void ValidateStart(DateTime start, BookingTimeOptions hours, DateTime now)
    {
        var day = DateOnly.FromDateTime(start);
        var open = TimeOnly.ParseExact(hours.StartTime, "HH:mm", CultureInfo.InvariantCulture);
        if (TimeOnly.FromDateTime(start) < open) day = day.AddDays(-1);
        if (!Slots(day, hours, now).Contains(start)) throw new BookingException("Выбранное время недоступно.");
    }

    public static CarWashQuote Quote(CarWashCatalog catalog, string categoryId, string[]? serviceIds, int? defaultMinutes)
    {
        if (!catalog.Categories.Any(c => c.Id == categoryId)) throw new BookingException("Выберите категорию автомобиля.");
        if (serviceIds is null || serviceIds.Length == 0 || serviceIds.Length > 30 || serviceIds.Any(string.IsNullOrWhiteSpace) || serviceIds.Distinct().Count() != serviceIds.Length)
            throw new BookingException("Выберите услуги без повторений.");
        var selected = serviceIds.ToHashSet(StringComparer.Ordinal);
        // A package already pays for its included services: don't charge those twice.
        foreach (var item in catalog.PackageItems.Where(item => selected.Contains(item.PackageServiceId)))
            selected.Remove(item.IncludedServiceId);
        var services = selected.Select(id => catalog.Services.FirstOrDefault(s => s.Id == id && s.VehicleCategoryId == categoryId)
            ?? throw new BookingException("Одна из услуг недоступна для выбранной категории.")).ToArray();
        int? duration;
        if (selected.SetEquals(["complex_wash"])) duration = 90;
        else if (selected.SetEquals(["body_wash", "interior_polish", "tire_blackening"])) duration = 60;
        else if (selected.SetEquals(["body_wash", "interior_polish"])) duration = 50;
        else if (selected.Contains("complex_wash"))
        {
            var extras = services.Where(s => s.Id != "complex_wash").ToArray();
            var extraMinutes = extras.All(s => s.DurationMinutes > 0)
                ? checked(extras.Sum(s => s.DurationMinutes!.Value)) : defaultMinutes;
            duration = extraMinutes.HasValue ? checked(90 + extraMinutes.Value) : null;
        }
        else if (services.All(s => s.DurationMinutes > 0)) duration = checked(services.Sum(s => s.DurationMinutes!.Value));
        else duration = defaultMinutes;
        if (duration is null or <= 0 or > 1440) throw new BookingException("Для выбранных услуг ещё не настроено время записи.", 409, "DURATION_NOT_CONFIGURED");
        return new(services, checked(services.Sum(s => s.PriceKzt)), duration.Value);
    }
}
