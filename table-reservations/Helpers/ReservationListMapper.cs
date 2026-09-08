using System.Globalization;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Models.Tenancy;

namespace table_reservations.Helpers;

public static class ReservationListMapper
{
    public static IReadOnlyList<ReservationListItem> Map(
        IEnumerable<IList<object>> rows, SheetSchemaOptions schema, BusinessType businessType, DateOnly date)
    {
        var result = new List<ReservationListItem>();
        foreach (var row in rows)
        {
            string Cell(int index) => index >= 0 && index < row.Count
                ? row[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;

            if (!ReservationDateTime.TryParse(Cell(schema.ScheduledAtColumn), out var start)
                || DateOnly.FromDateTime(start) != date)
                continue;

            result.Add(new ReservationListItem(
                start.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
                businessType == BusinessType.Restaurant ? Cell(schema.TableIdsColumn) : string.Empty,
                businessType == BusinessType.CarWash ? Cell(schema.ServiceTypeColumn) : string.Empty));
        }

        return result.OrderBy(item => item.ScheduledAt, StringComparer.Ordinal).ToArray();
    }
}
