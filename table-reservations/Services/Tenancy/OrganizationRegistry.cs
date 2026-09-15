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
                org.Telegram ??= new OrganizationTelegramOptions();
                var code = org.Telegram.ConnectionCode;
                if (!string.IsNullOrEmpty(code) && (code.Length > 128 || code.Any(char.IsWhiteSpace)))
                    throw new InvalidOperationException($"Telegram ConnectionCode for organization '{org.Id}' must have 1-128 characters without whitespace.");
                org.BookingTime ??= new BookingTimeOptions();
                try
                {
                    _ = BookingTimeSchedule.GetAvailableSlots(org.BookingTime);
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException(
                        $"Invalid booking time configuration for organization '{org.Id}'. {ex.Message}",
                        ex);
                }
            }

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
