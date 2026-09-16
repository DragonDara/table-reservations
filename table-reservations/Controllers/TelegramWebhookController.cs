using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Services;
using table_reservations.Services.Tenancy;

namespace table_reservations.Controllers;

[ApiController]
[Route("api/integrations/telegram/webhook")]
public sealed class TelegramWebhookController(IOptions<TelegramOptions> options,
    OrganizationRegistry organizations, TelegramSubscriptionStore subscriptions,
    ITelegramBotClient bot) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Receive([FromBody] TelegramUpdate update, CancellationToken ct)
    {
        var secret = options.Value.WebhookSecret;
        if (!options.Value.IsConfigured || string.IsNullOrWhiteSpace(secret)) return NotFound();
        var supplied = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(supplied)))
            return Unauthorized();

        var message = update.Message;
        if (message?.Chat is not { Id: < 0, Type: "group" or "supergroup" } chat) return Ok();
        // Service updates are authenticated by the webhook secret, not by their sender.
        if (message.MigrateToChatId is < 0)
        {
            await subscriptions.MigrateChatAsync(chat.Id, message.MigrateToChatId.Value, ct);
            return Ok();
        }
        if (message.MigrateFromChatId is < 0)
        {
            await subscriptions.MigrateChatAsync(message.MigrateFromChatId.Value, chat.Id, ct);
            return Ok();
        }
        if (message.From is not { Id: > 0, IsBot: false } || message.SenderChat is not null) return Ok();
        var parts = (message.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Ok();
        var command = parts[0].Split('@', 2);
        if (command.Length == 2 && !string.Equals(command[1], options.Value.BotUsername, StringComparison.OrdinalIgnoreCase)) return Ok();
        if (command[0] is not ("/connect" or "/disconnect")) return Ok();
        if (parts.Length != 2 || parts[1].Length > 128)
        {
            await bot.SendMessageAsync(chat.Id,
                $"Используйте {command[0]}@{options.Value.BotUsername} КОД_БИЗНЕСА", ct);
            return Ok();
        }
        var code = Encoding.UTF8.GetBytes(parts[1]);
        var matches = organizations.All.Where(o => !string.IsNullOrEmpty(o.Telegram.ConnectionCode) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(o.Telegram.ConnectionCode), code)).ToArray();
        if (matches.Length != 1)
        {
            await bot.SendMessageAsync(chat.Id, "Неверный код бизнеса.", ct);
            return Ok();
        }

        var organization = matches[0];
        if (command[0] == "/disconnect")
        {
            var disconnected = await subscriptions.DisconnectAsync(organization.Id, chat.Id, ct);
            await bot.SendMessageAsync(chat.Id, disconnected
                ? $"Уведомления для «{organization.DisplayName}» отключены."
                : "Эта группа не подключена к указанному бизнесу.", ct);
            return Ok();
        }

        var connected = await subscriptions.ConnectAsync(organization.Id, chat.Id, chat.Title ?? "", ct);
        await bot.SendMessageAsync(chat.Id, connected
            ? $"Уведомления для «{organization.DisplayName}» подключены. Сюда будут приходить копии подтверждений, сообщений администратору и напоминаний о бронях. Для отключения используйте /disconnect@{options.Value.BotUsername} КОД_БИЗНЕСА."
            : "Эта группа уже подключена к другому бизнесу.", ct);
        return Ok();
    }
}

public sealed class TelegramUpdate
{
    public TelegramMessage? Message { get; init; }
}
public sealed class TelegramMessage
{
    public TelegramChat? Chat { get; init; }
    public TelegramUser? From { get; init; }
    [JsonPropertyName("sender_chat")] public TelegramChat? SenderChat { get; init; }
    [JsonPropertyName("migrate_to_chat_id")] public long? MigrateToChatId { get; init; }
    [JsonPropertyName("migrate_from_chat_id")] public long? MigrateFromChatId { get; init; }
    public string? Text { get; init; }
}
public sealed class TelegramChat
{
    public long Id { get; init; }
    public string? Type { get; init; }
    public string? Title { get; init; }
}
public sealed class TelegramUser
{
    public long Id { get; init; }
    [JsonPropertyName("is_bot")] public bool IsBot { get; init; }
}
