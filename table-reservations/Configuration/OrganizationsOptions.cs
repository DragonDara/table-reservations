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
    /// Configuration for a single tenant (organization). Each organization is
    /// backed by its own Google Sheets spreadsheet and Google credentials, and
    /// declares the business type that selects its reservation rules and schema.
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

        /// <summary>Google Sheets spreadsheet id backing this organization.</summary>
        public string SpreadsheetId { get; set; } = string.Empty;

        /// <summary>Inline service-account credentials JSON (dev). Prefer secrets/Key Vault in prod.</summary>
        public string? CredentialsJson { get; set; }

        /// <summary>Path to a service-account credentials JSON file.</summary>
        public string? CredentialsJsonPath { get; set; }

        /// <summary>Sheet names, ranges, and column layout for this organization's schema.</summary>
        public SheetSchemaOptions Sheets { get; set; } = new();

        /// <summary>
        /// Public, front-end-facing branding/content/theme settings for this organization.
        /// Everything here is safe to expose to the browser; secrets (spreadsheet id,
        /// credentials, sheet schema) must never be placed in this section.
        /// </summary>
        public FrontendOptions Frontend { get; set; } = new();
    }

    /// <summary>
    /// Public frontend configuration for an organization. Bound from the
    /// <c>Frontend</c> child section and mapped to a strictly allow-listed public
    /// response. Never contains spreadsheet ids, credentials, or sheet schema.
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

    /// <summary>
    /// Describes the Google Sheets layout for an organization. Defaults match the
    /// original single-tenant restaurant schema so existing tenants keep working
    /// without extra configuration. Car-wash tenants override sheet names, ranges,
    /// and column indexes to match their (id, plate, time, phone, wash type) layout.
    /// </summary>
    public sealed class SheetSchemaOptions
    {
        public string TablesRange { get; set; } = "Столики!A2:C100";
        public string ReservationsRange { get; set; } = "Брони!A2:H10000";
        public string ReservationsAppendRange { get; set; } = "Брони!A:H";

        /// <summary>Sheet (tab) name that stores reservations; used for targeted cell updates.</summary>
        public string ReservationsSheetName { get; set; } = "Брони";

        /// <summary>Column letter used to mark that a reminder has been sent.</summary>
        public string ReminderSentColumnLetter { get; set; } = "H";

        // Zero-based column indexes within a reservation row.
        public int TableIdsColumn { get; set; } = 1;
        public int CustomerNameColumn { get; set; } = 2;
        public int CustomerPhoneColumn { get; set; } = 3;
        public int ScheduledAtColumn { get; set; } = 4;
        public int RemindBeforeHourColumn { get; set; } = 6;
        public int ReminderSentColumn { get; set; } = 7;
        public int ResourceColumn { get; set; } = 1;
        public int ServiceTypeColumn { get; set; } = 4;
    }
}
