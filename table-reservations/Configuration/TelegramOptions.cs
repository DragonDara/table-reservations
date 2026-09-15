namespace table_reservations.Configuration;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";
    public string? BotToken { get; set; }
    public string? BotUsername { get; set; }
    public string? WebhookSecret { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}

public sealed class OrganizationTelegramOptions
{
    // Reusable code chosen by the operator and given privately to this business.
    public string? ConnectionCode { get; set; }
}
