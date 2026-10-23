using System.Globalization;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Services;

namespace table_reservations.Services.Tenancy
{
    /// <summary>
    /// Singleton registry that indexes configured organizations by id and by
    /// subdomain for fast tenant resolution. Built once from
    /// <see cref="OrganizationsOptions"/>.
    /// </summary>
    public sealed class OrganizationRegistry
    {
        private readonly IReadOnlyDictionary<string, OrganizationOptions> _byId;
        private readonly IReadOnlyDictionary<string, OrganizationOptions> _bySubdomain;

        public OrganizationRegistry(IOptions<OrganizationsOptions> options)
        {
            var items = options.Value.Items ?? new List<OrganizationOptions>();

            if (items.Count == 0)
            {
                throw new InvalidOperationException("At least one organization must be configured.");
            }

            var duplicateId = items
                .Where(o => !string.IsNullOrWhiteSpace(o.Id))
                .GroupBy(o => o.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicateId is not null)
            {
                throw new InvalidOperationException($"Duplicate organization id '{duplicateId}'.");
            }

            if (items.Any(o => string.IsNullOrWhiteSpace(o.Id)))
            {
                throw new InvalidOperationException("Every organization must have a non-empty id.");
            }

            foreach (var org in items)
            {
                if (org.ReservationDurationMinutes is <= 0 or > 1440)
                    throw new InvalidOperationException($"Invalid reservation duration for organization '{org.Id}'. Use 1–1440 minutes.");
                if (org.RestaurantStorage is { } storage
                    && (org.BusinessType != Models.Tenancy.BusinessType.Restaurant
                        || !RestaurantStorageOptions.IsValidPrefix(storage.TablePrefix)))
                    throw new InvalidOperationException($"Invalid restaurant table prefix for organization '{org.Id}'. Use a lowercase SQL identifier ending in underscore.");

                org.OpeningHours ??= new();
                if (org.OpeningHours.GroupBy(hours => hours.Day).Any(days => days.Count() > 1)
                    || org.OpeningHours.Any(hours => !Enum.IsDefined(hours.Day)
                        || !TimeOnly.TryParseExact(hours.OpenTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                        || !TimeOnly.TryParseExact(hours.CloseTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                    throw new InvalidOperationException($"Invalid opening hours for organization '{org.Id}': use unique weekdays and HH:mm times.");

                org.Telegram ??= new OrganizationTelegramOptions();
                var code = org.Telegram.ConnectionCode;
                if (!string.IsNullOrEmpty(code) && (code.Length > 128 || code.Any(char.IsWhiteSpace)))
                    throw new InvalidOperationException($"Telegram ConnectionCode for organization '{org.Id}' must have 1-128 characters without whitespace.");
                org.BookingTime ??= new BookingTimeOptions();
                try
                {
                    _ = BookingTimeSchedule.GetAvailableSlots(org.BookingTime);
                    foreach (var hours in org.OpeningHours)
                        _ = BookingTimeSchedule.GetAvailableSlots(BookingRules.HoursForDay(org, hours.Day)!);
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException(
                        $"Invalid booking time configuration for organization '{org.Id}'. {ex.Message}",
                        ex);
                }
            }

            if (items.Where(o => o.RestaurantStorage is not null)
                .GroupBy(o => o.RestaurantStorage!.TablePrefix, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
                throw new InvalidOperationException("Restaurant table prefixes must be unique for each organization.");

            if (items.Where(o => !string.IsNullOrEmpty(o.Telegram.ConnectionCode))
                .GroupBy(o => o.Telegram.ConnectionCode, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
                throw new InvalidOperationException("Telegram ConnectionCode must be unique for each organization.");

            _byId = items
                .Where(o => !string.IsNullOrWhiteSpace(o.Id))
                .ToDictionary(o => o.Id.Trim(), StringComparer.OrdinalIgnoreCase);

            var bySubdomain = new Dictionary<string, OrganizationOptions>(StringComparer.OrdinalIgnoreCase);
            foreach (var org in items)
            {
                foreach (var subdomain in org.Subdomains ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(subdomain))
                    {
                        var normalizedSubdomain = subdomain.Trim();
                        if (!bySubdomain.TryAdd(normalizedSubdomain, org))
                        {
                            throw new InvalidOperationException(
                                $"Subdomain '{normalizedSubdomain}' is assigned to multiple organizations.");
                        }
                    }
                }
            }

            _bySubdomain = bySubdomain;
        }

        public IReadOnlyCollection<OrganizationOptions> All => _byId.Values.ToArray();

        public bool TryGetById(string? id, out OrganizationOptions organization)
        {
            organization = default!;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return _byId.TryGetValue(id.Trim(), out organization!);
        }

        public bool TryGetBySubdomain(string? subdomain, out OrganizationOptions organization)
        {
            organization = default!;
            if (string.IsNullOrWhiteSpace(subdomain))
            {
                return false;
            }

            return _bySubdomain.TryGetValue(subdomain.Trim(), out organization!);
        }
    }
}
