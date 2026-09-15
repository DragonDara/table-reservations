using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Controllers;
using table_reservations.Data;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests;

public class TelegramNotificationsTests
{
    private const string Code = "tochka-2026";
    private const string Secret = "test-webhook-secret-with-32-characters";
    private static readonly DateTime At = new(2026, 9, 20, 15, 0, 0);
    private static readonly ReservationInfo Booking = new() {
        CustomerName = "<Клиент>", CustomerPhone = "+77010000001", TablesId = "2", PlateNumber = "123ABC",
        WashServiceType = "Комплекс", ScheduledAt = "2026-09-20T15:00"
    };
    private static IOptions<TelegramOptions> Settings(bool enabled = true) => Options.Create(new TelegramOptions {
        BotToken = enabled ? "123:test-token" : null, BotUsername = "booking_bot", WebhookSecret = Secret
    });
    private static OrganizationOptions Organization(string id = "one", BusinessType type = BusinessType.Restaurant) => new() {
        Id = id, DisplayName = id, BusinessType = type,
        Telegram = new() { ConnectionCode = Code },
        WhatsApp = new() { ApiUrl = "https://wa.example", IdInstance = "1", ApiTokenInstance = "token", AdminPhone = "+77010000002" }
    };
    private static TenantContext Tenant(OrganizationOptions organization)
    {
        var tenant = new TenantContext(); tenant.Set(organization); return tenant;
    }
    private sealed class Bot : ITelegramBotClient
    {
        public bool Admin = true;
        public bool Send = true;
        public int AdminChecks;
        public List<(long ChatId, string Text)> Messages = [];
        public Task<bool> IsAdministratorAsync(long chatId, long userId, CancellationToken ct)
        { AdminChecks++; return Task.FromResult(Admin); }
        public Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct)
        { Messages.Add((chatId, text)); return Task.FromResult(Send); }
    }
    private sealed class WhatsApp : IWhatsAppNotificationService
    {
        public bool Fail;
        public bool ReminderSuccess = true;
        public int Reservations;
        public int Reminders;
        public Task<(bool CustomerSent, bool AdminSent)> SendReservationNotificationsAsync(ReservationInfo r, DateTime at, string label, CancellationToken ct)
        { Reservations++; return Fail ? Task.FromException<(bool, bool)>(new HttpRequestException()) : Task.FromResult((true, true)); }
        public Task<bool> SendReminderBeforeHourAsync(ReservationInfo r, DateTime at, CancellationToken ct)
        { Reminders++; return Task.FromResult(ReminderSuccess); }
    }
    private static async Task<TelegramSubscriptionStore> Store(ReservationRepositoryTests.Database db)
    {
        await new DatabaseInitializer(db, NullLogger<DatabaseInitializer>.Instance).MigrateAsync();
        return new(db);
    }
    private static ReservationNotificationService Service(TenantContext tenant, TelegramSubscriptionStore store, Bot bot, WhatsApp wa, bool enabled = true) =>
        new(wa, bot, store, tenant, new(tenant), Settings(enabled), NullLogger<ReservationNotificationService>.Instance);

    [Theory]
    [InlineData(BusinessType.Restaurant)]
    [InlineData(BusinessType.CarWash)]
    public async Task CopiesBothMessagesToOnlyTheSelectedBusinessGroupEvenWhenWhatsAppFails(BusinessType type)
    {
        using var db = new ReservationRepositoryTests.Database();
        var store = await Store(db);
        await store.ConnectAsync("one", -1001, "First", default);
        await store.ConnectAsync("two", -1002, "Second", default);
        var tenant = Tenant(Organization(type: type));
        var bot = new Bot(); var wa = new WhatsApp { Fail = true };
        var result = await Service(tenant, store, bot, wa).SendReservationNotificationsAsync(Booking, At, "Комплекс", default);
        Assert.True(result.TelegramSent);
        Assert.False(result.CustomerSent);
        Assert.Equal(1, wa.Reservations);
        Assert.Equal(2, bot.Messages.Count);
        Assert.All(bot.Messages, m => Assert.Equal(-1001, m.ChatId));
        var messages = new ReservationNotificationMessages(tenant);
        Assert.Equal(messages.Customer(Booking, At, "Комплекс"), bot.Messages[0].Text);
        Assert.Equal(messages.Admin(Booking, At, "Комплекс"), bot.Messages[1].Text);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UnconnectedOrFailingTelegramDoesNotPreventWhatsApp(bool connected, bool succeeds)
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        if (connected) await store.ConnectAsync("one", -1001, "First", default);
        var bot = new Bot { Send = succeeds }; var wa = new WhatsApp();
        var result = await Service(Tenant(Organization()), store, bot, wa).SendReservationNotificationsAsync(Booking, At, "VIP", default);
        Assert.True(result.CustomerSent); Assert.True(result.AdminSent); Assert.False(result.TelegramSent);
        if (!connected) Assert.Empty(bot.Messages);
    }

    [Fact]
    public async Task DisabledTelegramDoesNotNeedNewTables()
    {
        var wa = new WhatsApp(); var bot = new Bot();
        var service = Service(Tenant(Organization()), null!, bot, wa, enabled: false);
        Assert.True((await service.SendReservationNotificationsAsync(Booking, At, "VIP", default)).CustomerSent);
        Assert.True(await service.SendReminderAsync("booking", Booking, At, default));
        Assert.Empty(bot.Messages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReminderRetriesOnlyFailedChannelAcrossServiceInstances(bool telegramFails)
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        await store.ConnectAsync("one", -1001, "First", default);
        var bot = new Bot { Send = !telegramFails }; var wa = new WhatsApp { ReminderSuccess = telegramFails };
        var tenant = Tenant(Organization());
        Assert.False(await Service(tenant, store, bot, wa).SendReminderAsync("booking", Booking, At, default));
        bot.Send = true; wa.ReminderSuccess = true;
        Assert.True(await Service(tenant, new(db), bot, wa).SendReminderAsync("booking", Booking, At, default));
        Assert.Equal(telegramFails ? 1 : 2, wa.Reminders);
        Assert.Equal(telegramFails ? 2 : 1, bot.Messages.Count);
        Assert.Equal(new ReservationNotificationMessages(tenant).Reminder(Booking, At), bot.Messages[0].Text);
    }

    [Fact]
    public async Task TelegramOnlyRemindersCanComplete()
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        await store.ConnectAsync("one", -1001, "First", default);
        var org = Organization(); org.WhatsApp = new();
        var wa = new WhatsApp(); var bot = new Bot();
        Assert.True(await Service(Tenant(org), store, bot, wa).SendReminderAsync("booking", Booking, At, default));
        Assert.Equal(0, wa.Reminders); Assert.Single(bot.Messages);
    }

    private static TelegramWebhookController Controller(TelegramSubscriptionStore store, Bot bot, string secret = Secret)
    {
        var controller = new TelegramWebhookController(Settings(),
            new OrganizationRegistry(Options.Create(new OrganizationsOptions { Items = [Organization()] })), store, bot) {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = secret;
        return controller;
    }
    private static TelegramUpdate Update(string text, string type = "supergroup", long chatId = -1001, bool anonymous = false) => new() {
        Message = new() { Chat = new() { Id = chatId, Type = type, Title = "Business" },
            From = new() { Id = 42 }, Text = text, SenderChat = anonymous ? new() { Id = chatId } : null }
    };

    [Fact]
    public async Task WebhookRejectsForgedSecretBeforeAccessingDatabaseOrBot()
    {
        var bot = new Bot();
        Assert.IsType<UnauthorizedResult>(await Controller(null!, bot, "forged").Receive(Update("/connect " + Code), default));
        Assert.Equal(0, bot.AdminChecks); Assert.Empty(bot.Messages);
    }

    [Fact]
    public async Task AdministratorCanReuseConfiguredCodeToReconnectAndMoveGroup()
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        var bot = new Bot(); var controller = Controller(store, bot);
        await controller.Receive(Update("/connect@booking_bot " + Code), default);
        Assert.Equal(-1001, await new TelegramSubscriptionStore(db).GetChatIdAsync("one", default));
        Assert.Contains("подключены", bot.Messages[^1].Text);
        await controller.Receive(Update("/connect " + Code), default);
        Assert.Equal(-1001, await store.GetChatIdAsync("one", default));
        await controller.Receive(Update("/connect " + Code, chatId: -1002), default);
        Assert.Equal(-1002, await store.GetChatIdAsync("one", default));
        await controller.Receive(Update("/disconnect@booking_bot", chatId: -1002), default);
        Assert.Null(await store.GetChatIdAsync("one", default));
        await controller.Receive(Update("/connect " + Code), default);
        Assert.Equal(-1001, await store.GetChatIdAsync("one", default));
    }

    [Theory]
    [InlineData("private", true, false, "/connect ")]
    [InlineData("channel", true, false, "/connect ")]
    [InlineData("supergroup", false, false, "/connect ")]
    [InlineData("supergroup", true, true, "/connect ")]
    [InlineData("supergroup", true, false, "/connect@other_bot ")]
    public async Task UnauthorizedConnectionDoesNotChangeSubscription(string type, bool admin, bool anonymous, string command)
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        var bot = new Bot { Admin = admin };
        await Controller(store, bot).Receive(Update(command + Code, type, anonymous: anonymous), default);
        Assert.Null(await store.GetChatIdAsync("one", default));
    }

    [Fact]
    public async Task GroupsCannotBeSharedBetweenBusinesses()
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        Assert.True(await store.ConnectAsync("one", -1001, "First", default));
        Assert.False(await store.ConnectAsync("two", -1001, "First", default));
        Assert.True(await store.ConnectAsync("two", -1002, "Second", default));
        Assert.True(await store.ConnectAsync("one", -1003, "Third", default));
        Assert.Equal(-1003, await store.GetChatIdAsync("one", default));
        Assert.Equal(-1002, await store.GetChatIdAsync("two", default));
        await store.DisconnectAsync(-1001, default);
        Assert.Equal(-1003, await store.GetChatIdAsync("one", default));
    }

    [Fact]
    public async Task GroupUpgradeKeepsSubscription()
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        await store.ConnectAsync("one", -123, "Group", default);
        await Controller(store, new()).Receive(new() { Message = new() {
            Chat = new() { Id = -123, Type = "group" }, MigrateToChatId = -100123
        } }, default);
        Assert.Equal(-100123, await store.GetChatIdAsync("one", default));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":1}}", true)]
    [InlineData(HttpStatusCode.OK, "{\"ok\":false}", false)]
    [InlineData(HttpStatusCode.Forbidden, "{}", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", false)]
    [InlineData(HttpStatusCode.OK, "invalid json", false)]
    public async Task BotUsesPlainTextAndChecksTelegramResponse(HttpStatusCode status, string response, bool expected)
    {
        using var http = new HttpClient(new Handler(async request => {
            Assert.Equal("https://api.telegram.org/bot123:test-token/sendMessage", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(-100123, json.RootElement.GetProperty("chat_id").GetInt64());
            Assert.Equal("<Клиент> & текст", json.RootElement.GetProperty("text").GetString());
            Assert.False(json.RootElement.TryGetProperty("parse_mode", out _));
            return new(status) { Content = new StringContent(response) };
        }));
        var client = new TelegramBotClient(http, Settings(), NullLogger<TelegramBotClient>.Instance);
        Assert.Equal(expected, await client.SendMessageAsync(-100123, "<Клиент> & текст", default));
    }

    [Fact]
    public async Task BotNetworkFailuresReturnFalse()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("test")));
        Assert.False(await new TelegramBotClient(http, Settings(), NullLogger<TelegramBotClient>.Instance).SendMessageAsync(-1001, "text", default));
    }

    [Fact]
    public void PublicConfigContainsNoTelegramSettings()
    {
        var json = JsonSerializer.Serialize(PublicTenantConfigResponse.FromOrganization(Organization()));
        Assert.DoesNotContain(Code, json);
        Assert.DoesNotContain("ConnectionCode", json);
    }

    [Theory]
    [InlineData("/connect wrong-code")]
    [InlineData("/connect TOCHKA-2026")]
    [InlineData("/connect")]
    [InlineData("/connect tochka-2026 extra")]
    public async Task InvalidCodeCannotChangeExistingGroup(string command)
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        await store.ConnectAsync("one", -1001, "First", default);
        await Controller(store, new()).Receive(Update(command, chatId: -1002), default);
        Assert.Equal(-1001, await store.GetChatIdAsync("one", default));
    }

    [Theory]
    [InlineData("with space")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    public void ConfigRejectsCodesContainingWhitespace(string code)
    {
        var org = Organization(); org.Telegram.ConnectionCode = code;
        Assert.Throws<InvalidOperationException>(() =>
            new OrganizationRegistry(Options.Create(new OrganizationsOptions { Items = [org] })));
    }

    [Fact]
    public void ConfigRejectsDuplicateCodesWithoutExposingThemInError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new OrganizationRegistry(
            Options.Create(new OrganizationsOptions { Items = [Organization("one"), Organization("two")] })));
        Assert.DoesNotContain(Code, error.Message);
    }

    [Fact]
    public async Task CodeSelectsItsOwnTenantAndEmptyCodeDisablesConnection()
    {
        using var db = new ReservationRepositoryTests.Database(); var store = await Store(db);
        var one = Organization("one"); var two = Organization("two"); var disabled = Organization("disabled");
        two.Telegram.ConnectionCode = "carwash-code"; disabled.Telegram.ConnectionCode = "";
        var registry = new OrganizationRegistry(Options.Create(new OrganizationsOptions { Items = [one, two, disabled] }));
        var bot = new Bot();
        var controller = new TelegramWebhookController(Settings(), registry, store, bot) {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = Secret;
        await controller.Receive(Update("/connect carwash-code"), default);
        Assert.Equal(-1001, await store.GetChatIdAsync("two", default));
        Assert.Null(await store.GetChatIdAsync("one", default));
        Assert.Null(await store.GetChatIdAsync("disabled", default));
    }
}
