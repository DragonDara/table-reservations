using table_reservations.Models.Tenancy;

namespace table_reservations.Configuration
{
    /// <summary>
    /// Root options bound from the <c>Organizations</c> section of configuration.
    /// Holds the list of tenants (organizations) supported by the API.
    /// </summary>
    public sealed class OrganizationsOptions
    {
        public const string SectionName = "Organizations";

        public List<OrganizationOptions> Items { get; set; } = new();
    }

    /// <summary>
    /// Controls which DNS suffixes are authoritative for tenant subdomains.
    /// A hostname is never treated as a tenant host unless it exactly matches
    /// &lt;tenant&gt;.&lt;base-domain&gt;.
    /// </summary>
    public sealed class TenantRoutingOptions
    {
        public const string SectionName = "TenantRouting";

        public string[] BaseDomains { get; set; } = new[] { "bron.cafe" };
    }

    /// <summary>
    /// Configuration for a tenant using the Turso reservation repository and
    /// the business type that selects its reservation rules.
    /// </summary>
    public sealed class OrganizationOptions
    {
        /// <summary>Stable unique identifier used by the <c>X-Organization-Id</c> header.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Human friendly name (for logs / diagnostics).</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Subdomains that map to this organization (e.g. "theveil", "thetochka").</summary>
        public string[] Subdomains { get; set; } = Array.Empty<string>();

        /// <summary>Business type that selects the pluggable reservation strategy.</summary>
        public BusinessType BusinessType { get; set; } = BusinessType.Restaurant;

        /// <summary>False for venues whose booking setup is not ready.</summary>
        public bool BookingEnabled { get; set; } = true;

        /// <summary>Organization-specific booking window and slot interval.</summary>
        public BookingTimeOptions BookingTime { get; set; } = new();

        /// <summary>Weekly venue hours in local venue time, separate from reservation start rules.</summary>
        public List<OpeningHoursOptions> OpeningHours { get; set; } = new();

        /// <summary>Explicit table namespace for an additional restaurant in the shared database.</summary>
        public RestaurantStorageOptions? RestaurantStorage { get; set; }

        /// <summary>Tenant-specific WhatsApp delivery settings.</summary>
        public WhatsAppOptions WhatsApp { get; set; } = new();

        public OrganizationTelegramOptions Telegram { get; set; } = new();

        /// <summary>Tenant-specific rating provider settings.</summary>
        public RatingOptions Rating { get; set; } = new();

        /// <summary>Tenant-specific POS provider settings.</summary>
        public PosOptions Pos { get; set; } = new();

        /// <summary>
        /// Public, front-end-facing branding/content/theme settings for this organization.
        /// Everything here is safe to expose to the browser; backend secrets (tokens,
        /// credentials) must never be placed in this section.
        /// </summary>
        public FrontendOptions Frontend { get; set; } = new();
    }

    public sealed class RestaurantStorageOptions
    {
        public string TablePrefix { get; set; } = string.Empty;

        public static bool IsValidPrefix(string? prefix) => prefix is not null
            && System.Text.RegularExpressions.Regex.IsMatch(prefix, @"\A[a-z][a-z0-9_]{0,39}_\z")
            && !prefix.StartsWith("sqlite_", StringComparison.Ordinal);
    }

    public sealed class OpeningHoursOptions
    {
        /// <summary>The day on which the venue opens, including for overnight hours.</summary>
        public DayOfWeek Day { get; set; }
        public string OpenTime { get; set; } = string.Empty;
        /// <summary>HH:mm; a time at or before opening means the following calendar day.</summary>
        public string CloseTime { get; set; } = string.Empty;
    }

    public sealed class BookingTimeOptions
    {
        /// <summary>First available slot, formatted as HH:mm.</summary>
        public string StartTime { get; set; } = "12:00";

        /// <summary>
        /// Exclusive end of the booking window, formatted as HH:mm. An end time
        /// earlier than the start time represents a window crossing midnight.
        /// </summary>
        public string EndTime { get; set; } = "04:00";

        /// <summary>
        /// Exclusive cutoff for reservation start times, formatted as HH:mm.
        /// When omitted, <see cref="EndTime"/> is used for backward compatibility.
        /// The cutoff must fall within the configured start/end window.
        /// </summary>
        public string? ReservationDeadline { get; set; }

        /// <summary>Number of minutes between available booking slots.</summary>
        public int SlotDurationMinutes { get; set; } = 60;
    }

    public sealed class WhatsAppOptions
    {
        public string? ApiUrl { get; set; }
        public string? IdInstance { get; set; }
        public string? ApiTokenInstance { get; set; }
        public string? AdminPhone { get; set; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiUrl)
            && !string.IsNullOrWhiteSpace(IdInstance)
            && !string.IsNullOrWhiteSpace(ApiTokenInstance);
    }

    public sealed class RatingOptions
    {
        public bool Enabled { get; set; }
        public string? ApifyToken { get; set; }
        public string? PlaceUrl { get; set; }
        public string? Mode { get; set; }
        public double? Value { get; set; }
        public int? ReviewCount { get; set; }
    }

    public sealed class PosOptions
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; } = "iiko";
        public string? BaseUrl { get; set; }
        public string? ApiLogin { get; set; }
        public string? OrganizationId { get; set; }
    }

    /// <summary>
    /// Public frontend configuration for an organization. Bound from the
    /// <c>Frontend</c> child section and mapped to a strictly allow-listed public
    /// response. Never contains backend tokens or credentials.
    /// </summary>
    public sealed class FrontendOptions
    {
        /// <summary>Human friendly public name shown in the UI.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Value used for the browser tab / document title.</summary>
        public string DocumentTitle { get; set; } = string.Empty;

        /// <summary>BCP-47 locale used for the document language, e.g. "ru", "en".</summary>
        public string Locale { get; set; } = "ru";

        /// <summary>Small allow-listed layout hint (e.g. "restaurant", "carwash").</summary>
        public string LayoutVariant { get; set; } = string.Empty;

        public FrontendThemeOptions Theme { get; set; } = new();
        public FrontendAssetsOptions Assets { get; set; } = new();
        public FrontendContentOptions Content { get; set; } = new();
        public FrontendLinksOptions Links { get; set; } = new();
        public FrontendFeatureOptions Features { get; set; } = new();

        /// <summary>Free-form, business-type-specific display data (safe values only).</summary>
        public Dictionary<string, string> BusinessUi { get; set; } = new();
    }

    /// <summary>Semantic theme tokens applied to CSS custom properties on the client.</summary>
    public sealed class FrontendThemeOptions
    {
        public string? Background { get; set; }
        public string? Surface { get; set; }
        public string? Text { get; set; }
        public string? Muted { get; set; }
        public string? Accent { get; set; }
        public string? Border { get; set; }
        public string? Warning { get; set; }
        public string? FontFamily { get; set; }
        public string? HeadingFontFamily { get; set; }
        public string? BorderRadius { get; set; }
    }

    /// <summary>Public asset URLs (relative public paths or validated HTTPS URLs).</summary>
    public sealed class FrontendAssetsOptions
    {
        public string? Logo { get; set; }
        public string? Favicon { get; set; }
        public string? HeroImage { get; set; }
        public string? HeroBackground { get; set; }
        public string[] Gallery { get; set; } = Array.Empty<string>();
    }

    /// <summary>Public textual content rendered as text nodes on the client.</summary>
    public sealed class FrontendContentOptions
    {
        public string? HeroEyebrow { get; set; }
        public string? HeroTitle { get; set; }
        public string? HeroAccent { get; set; }
        public string? HeroDescription { get; set; }
        public string? PrimaryCta { get; set; }
        public string? SecondaryCta { get; set; }
        public string? FooterCopyright { get; set; }
        public string? FooterTagline { get; set; }
    }

    /// <summary>Public links/contact info. Missing links hide their UI elements.</summary>
    public sealed class FrontendLinksOptions
    {
        public string? Menu { get; set; }
        public string? Map { get; set; }
        public string? Phone { get; set; }
        public string? WhatsApp { get; set; }
        public string? Instagram { get; set; }
        public string? Threads { get; set; }
    }

    /// <summary>Boolean feature switches controlling optional UI sections.</summary>
    public sealed class FrontendFeatureOptions
    {
        public bool ShowRating { get; set; } = true;
        public bool ShowHowItWorks { get; set; } = true;
        public bool ShowMenuLink { get; set; } = true;
        public bool ShowReminderOption { get; set; } = true;
        public bool ShowSocialLinks { get; set; } = true;
    }

}
