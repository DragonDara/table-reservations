using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;

namespace table_reservations.Services;

public interface ITelegramBotClient
{
    Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct);
    Task<bool> IsAdministratorAsync(long chatId, long userId, CancellationToken ct);
}

public sealed class TelegramBotClient(HttpClient http, IOptions<TelegramOptions> options,
    ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public async Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        // Telegram accepts at most 4096 characters. Preserve all text without splitting surrogate pairs.
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(4096, text.Length - offset);
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
            if (await CallAsync("sendMessage", new { chat_id = chatId, text = text.Substring(offset, length) }, ct) is null)
                return false;
            offset += length;
        }
        return text.Length > 0;
    }

    public async Task<bool> IsAdministratorAsync(long chatId, long userId, CancellationToken ct)
    {
        var result = await CallAsync("getChatMember", new { chat_id = chatId, user_id = userId }, ct);
        return result is { } member && member.TryGetProperty("status", out var status)
            && status.GetString() is "creator" or "administrator";
    }

    private async Task<JsonElement?> CallAsync(string method, object payload, CancellationToken ct)
    {
        if (!options.Value.IsConfigured) return null;
        try
        {
            using var response = await http.PostAsJsonAsync(
                $"https://api.telegram.org/bot{options.Value.BotToken}/{method}", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram {Method} failed with HTTP {Status}.", method, (int)response.StatusCode);
                return null;
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
                && json.RootElement.TryGetProperty("result", out var result)) return result.Clone();
            logger.LogWarning("Telegram {Method} returned an unsuccessful response.", method);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Exception messages and URLs can contain the bot token; never log them.
            logger.LogWarning("Telegram {Method} failed ({ErrorType}).", method, ex.GetType().Name);
        }
        return null;
    }
}
