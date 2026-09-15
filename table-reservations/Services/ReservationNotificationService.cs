using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Models;
using table_reservations.Services.Tenancy;

namespace table_reservations.Services;

public sealed record ReservationNotificationResult(bool CustomerSent, bool AdminSent, bool TelegramSent);

public interface IReservationNotificationService
{
    Task<ReservationNotificationResult> SendReservationNotificationsAsync(ReservationInfo reservation,
        DateTime dateTime, string label, CancellationToken ct);
    Task<bool> SendReminderAsync(string reservationId, ReservationInfo reservation, DateTime dateTime, CancellationToken ct);
}

public sealed class ReservationNotificationService(IWhatsAppNotificationService whatsApp,
    ITelegramBotClient telegram, TelegramSubscriptionStore subscriptions, TenantContext tenant,
    ReservationNotificationMessages messages, IOptions<TelegramOptions> options,
    ILogger<ReservationNotificationService> logger) : IReservationNotificationService
{
    public async Task<ReservationNotificationResult> SendReservationNotificationsAsync(
        ReservationInfo reservation, DateTime dateTime, string label, CancellationToken ct)
    {
        var wa = SendWhatsAppAsync();
        var tg = SafelyAsync(async () =>
        {
            if (!options.Value.IsConfigured) return false;
            var chatId = await subscriptions.GetChatIdAsync(tenant.OrganizationId, ct);
            if (chatId is null) return false;
            // Copy both existing messages, in order, even if WhatsApp is unavailable.
            var customer = await telegram.SendMessageAsync(chatId.Value, messages.Customer(reservation, dateTime, label), ct);
            var admin = await telegram.SendMessageAsync(chatId.Value, messages.Admin(reservation, dateTime, label), ct);
            return customer && admin;
        }, "telegram", ct);
        await Task.WhenAll(wa, tg);
        var result = await wa;
        return new(result.CustomerSent, result.AdminSent, await tg);

        async Task<(bool CustomerSent, bool AdminSent)> SendWhatsAppAsync()
        {
            try { return await whatsApp.SendReservationNotificationsAsync(reservation, dateTime, label, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("WhatsApp notification failed ({ErrorType}).", ex.GetType().Name);
                return (false, false);
            }
        }
    }

    public async Task<bool> SendReminderAsync(string reservationId, ReservationInfo reservation, DateTime dateTime, CancellationToken ct)
    {
        // Preserve the legacy path when Telegram has not been enabled on the server.
        if (!options.Value.IsConfigured)
            return await SafelyAsync(() => whatsApp.SendReminderBeforeHourAsync(reservation, dateTime, ct), "whatsapp", ct);

        var waEnabled = tenant.Organization!.WhatsApp.IsConfigured;
        var wa = waEnabled ? DeliverAsync("whatsapp", () => whatsApp.SendReminderBeforeHourAsync(reservation, dateTime, ct)) : Task.FromResult(true);
        var tg = SafelyAsync(async () =>
        {
            var chatId = await subscriptions.GetChatIdAsync(tenant.OrganizationId, ct);
            if (chatId is null) return waEnabled;
            return await DeliverAsync("telegram", () => telegram.SendMessageAsync(chatId.Value, messages.Reminder(reservation, dateTime), ct));
        }, "telegram", ct);
        await Task.WhenAll(wa, tg);
        return await wa && await tg;

        Task<bool> DeliverAsync(string channel, Func<Task<bool>> send) => SafelyAsync(async () =>
        {
            if (await subscriptions.WasDeliveredAsync(tenant.OrganizationId, reservationId, channel, ct)) return true;
            if (!await send()) return false;
            await subscriptions.MarkDeliveredAsync(tenant.OrganizationId, reservationId, channel, ct);
            return true;
        }, channel, ct);
    }

    private async Task<bool> SafelyAsync(Func<Task<bool>> action, string channel, CancellationToken ct)
    {
        try { return await action(); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Notification for {OrganizationId} via {Channel} failed ({ErrorType}).",
                tenant.OrganizationId, channel, ex.GetType().Name);
            return false;
        }
    }
}
