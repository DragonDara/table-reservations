using table_reservations.Configuration;
using table_reservations.Services;

namespace table_reservations.Models.Tenancy
{
    /// <summary>
    /// Strictly allow-listed, browser-safe public configuration for the resolved
    /// tenant. Returned by <c>GET /api/tenant/public-config</c>. This DTO must never
    /// carry backend secrets such as spreadsheet ids, credentials, or sheet schema.
    /// </summary>
    public sealed class PublicTenantConfigResponse
    {
        public string OrganizationId { get; init; } = string.Empty;
        public BusinessType BusinessType { get; init; }
        public string Locale { get; init; } = "ru";
        public string DisplayName { get; init; } = string.Empty;
        public string DocumentTitle { get; init; } = string.Empty;
        public string LayoutVariant { get; init; } = string.Empty;
        public PublicBookingTimeDto BookingTime { get; init; } = new();

        public PublicThemeDto Theme { get; init; } = new();
        public PublicAssetsDto Assets { get; init; } = new();
        public PublicContentDto Content { get; init; } = new();
        public PublicLinksDto Links { get; init; } = new();
        public PublicFeaturesDto Features { get; init; } = new();
        public Dictionary<string, string> BusinessUi { get; init; } = new();

        /// <summary>
        /// Maps the backend <see cref="OrganizationOptions"/> to the public DTO,
        /// copying only allow-listed fields. Secrets are intentionally excluded.
        /// </summary>
        public static PublicTenantConfigResponse FromOrganization(OrganizationOptions org)
        {
            ArgumentNullException.ThrowIfNull(org);

            var f = org.Frontend ?? new FrontendOptions();

            return new PublicTenantConfigResponse
            {
                OrganizationId = org.Id,
                BusinessType = org.BusinessType,
                Locale = string.IsNullOrWhiteSpace(f.Locale) ? "ru" : f.Locale,
                DisplayName = string.IsNullOrWhiteSpace(f.DisplayName) ? org.DisplayName : f.DisplayName,
                DocumentTitle = f.DocumentTitle,
                LayoutVariant = string.IsNullOrWhiteSpace(f.LayoutVariant)
                    ? org.BusinessType.ToString().ToLowerInvariant()
                    : f.LayoutVariant,
                BookingTime = new PublicBookingTimeDto
                {
                    StartTime = org.BookingTime.StartTime,
                    EndTime = org.BookingTime.EndTime,
                    SlotDurationMinutes = org.BookingTime.SlotDurationMinutes,
                    AvailableTimeSlots = BookingTimeSchedule.GetAvailableSlots(org.BookingTime).ToArray()
                },
                Theme = new PublicThemeDto
                {
                    Background = f.Theme.Background,
                    Surface = f.Theme.Surface,
                    Text = f.Theme.Text,
                    Muted = f.Theme.Muted,
                    Accent = f.Theme.Accent,
                    Border = f.Theme.Border,
                    Warning = f.Theme.Warning,
                    FontFamily = f.Theme.FontFamily,
                    HeadingFontFamily = f.Theme.HeadingFontFamily,
                    BorderRadius = f.Theme.BorderRadius
                },
                Assets = new PublicAssetsDto
                {
                    Logo = f.Assets.Logo,
                    Favicon = f.Assets.Favicon,
                    HeroImage = f.Assets.HeroImage,
                    HeroBackground = f.Assets.HeroBackground,
                    Gallery = f.Assets.Gallery ?? Array.Empty<string>()
                },
                Content = new PublicContentDto
                {
                    HeroEyebrow = f.Content.HeroEyebrow,
                    HeroTitle = f.Content.HeroTitle,
                    HeroAccent = f.Content.HeroAccent,
                    HeroDescription = f.Content.HeroDescription,
                    PrimaryCta = f.Content.PrimaryCta,
                    SecondaryCta = f.Content.SecondaryCta,
                    FooterCopyright = f.Content.FooterCopyright,
                    FooterTagline = f.Content.FooterTagline
                },
                Links = new PublicLinksDto
                {
                    Menu = f.Links.Menu,
                    Map = f.Links.Map,
                    Phone = f.Links.Phone,
                    WhatsApp = f.Links.WhatsApp,
                    Instagram = f.Links.Instagram,
                    Threads = f.Links.Threads
                },
                Features = new PublicFeaturesDto
                {
                    ShowRating = f.Features.ShowRating,
                    ShowHowItWorks = f.Features.ShowHowItWorks,
                    ShowMenuLink = f.Features.ShowMenuLink,
                    ShowReminderOption = f.Features.ShowReminderOption,
                    ShowSocialLinks = f.Features.ShowSocialLinks
                },
                BusinessUi = f.BusinessUi is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(f.BusinessUi)
            };
        }
    }

    public sealed class PublicBookingTimeDto
    {
        public string StartTime { get; init; } = string.Empty;
        public string EndTime { get; init; } = string.Empty;
        public int SlotDurationMinutes { get; init; }
        public string[] AvailableTimeSlots { get; init; } = Array.Empty<string>();
    }

    public sealed class PublicThemeDto
    {
        public string? Background { get; init; }
        public string? Surface { get; init; }
        public string? Text { get; init; }
        public string? Muted { get; init; }
        public string? Accent { get; init; }
        public string? Border { get; init; }
        public string? Warning { get; init; }
        public string? FontFamily { get; init; }
        public string? HeadingFontFamily { get; init; }
        public string? BorderRadius { get; init; }
    }

    public sealed class PublicAssetsDto
    {
        public string? Logo { get; init; }
        public string? Favicon { get; init; }
        public string? HeroImage { get; init; }
        public string? HeroBackground { get; init; }
        public string[] Gallery { get; init; } = Array.Empty<string>();
    }

    public sealed class PublicContentDto
    {
        public string? HeroEyebrow { get; init; }
        public string? HeroTitle { get; init; }
        public string? HeroAccent { get; init; }
        public string? HeroDescription { get; init; }
        public string? PrimaryCta { get; init; }
        public string? SecondaryCta { get; init; }
        public string? FooterCopyright { get; init; }
        public string? FooterTagline { get; init; }
    }

    public sealed class PublicLinksDto
    {
        public string? Menu { get; init; }
        public string? Map { get; init; }
        public string? Phone { get; init; }
        public string? WhatsApp { get; init; }
        public string? Instagram { get; init; }
        public string? Threads { get; init; }
    }

    public sealed class PublicFeaturesDto
    {
        public bool ShowRating { get; init; }
        public bool ShowHowItWorks { get; init; }
        public bool ShowMenuLink { get; init; }
        public bool ShowReminderOption { get; init; }
        public bool ShowSocialLinks { get; init; }
    }
}
