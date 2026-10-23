using table_reservations.Configuration;
using table_reservations.Models;
using table_reservations.Models.Tenancy;

namespace table_reservations.Data;

/// <summary>Names come only from validated server configuration, never request input.</summary>
public sealed record RestaurantStorageTables(string Tables, string Reservations, string Reminders)
{
    public static RestaurantStorageTables Resolve(OrganizationOptions organization, TursoOptions turso)
    {
        if (organization.BusinessType != BusinessType.Restaurant)
            throw new BookingException("Хранилище этой организации не настроено.", 404);
        if (organization.RestaurantStorage is { } storage)
            return FromPrefix(storage.TablePrefix);
        if (string.Equals(organization.Id, turso.RestaurantOrganizationId, StringComparison.OrdinalIgnoreCase))
            return new("tables", "table_reservations", "reservation_reminders");
        throw new BookingException("Хранилище этой организации не настроено.", 404);
    }

    public static RestaurantStorageTables FromPrefix(string prefix)
    {
        if (!RestaurantStorageOptions.IsValidPrefix(prefix))
            throw new InvalidOperationException("Invalid restaurant table prefix.");
        return new(prefix + "tables", prefix + "table_reservations", prefix + "reservation_reminders");
    }
}
