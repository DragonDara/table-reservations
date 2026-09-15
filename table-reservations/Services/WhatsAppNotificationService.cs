using System.Net.Http.Json;
using System.Text.Json.Serialization;
using table_reservations.Models;
using table_reservations.Services.Tenancy;

namespace table_reservations.Services
{
    public class WhatsAppNotificationService : IWhatsAppNotificationService
    {
        private readonly HttpClient _http;
        private readonly ILogger<WhatsAppNotificationService> _logger;
        private readonly TenantContext _tenant;
        private readonly ReservationNotificationMessages _messages;

        public WhatsAppNotificationService(
            HttpClient http,
            ILogger<WhatsAppNotificationService> logger,
            TenantContext tenant)
        {
            _http = http;
            _logger = logger;
            _tenant = tenant;
            _messages = new(tenant);
        }

        public async Task<(bool CustomerSent, bool AdminSent)> SendReservationNotificationsAsync(
            ReservationInfo reservation,
            DateTime dateTime,
            string tableTypeLabel,
            CancellationToken ct = default)
        {
            var customerChatId = ToChatId(reservation.CustomerPhone);
            if (customerChatId == null)
                _logger.LogWarning("Некорректный телефон клиента: {Phone}", reservation.CustomerPhone);

            var adminPhone = _tenant.Organization?.WhatsApp.AdminPhone;
            var adminChatId = string.IsNullOrWhiteSpace(adminPhone) ? null : ToChatId(adminPhone);
            if (adminChatId == null && !string.IsNullOrWhiteSpace(adminPhone))
                _logger.LogWarning("Некорректный AdminPhone: {Phone}", adminPhone);

            var customerTask = customerChatId != null
                ? SendMessageAsync(customerChatId, _messages.Customer(reservation, dateTime, tableTypeLabel), ct)
                : Task.FromResult(false);

            var adminTask = adminChatId != null
                ? SendMessageAsync(adminChatId, _messages.Admin(reservation, dateTime, tableTypeLabel), ct)
                : Task.FromResult(false);

            await Task.WhenAll(customerTask, adminTask);

            var customerSent = await customerTask;
            var adminSent = await adminTask;

            if (customerSent)
                _logger.LogInformation("WhatsApp клиенту отправлен: {Phone}", reservation.CustomerPhone);
            if (adminSent)
                _logger.LogInformation("WhatsApp админу отправлен: {Phone}", adminPhone);

            return (customerSent, adminSent);
        }

        private async Task<bool> SendMessageAsync(string chatId, string message, CancellationToken ct)
        {
            var url = BuildSendMessageUrl();
            if (url is null)
            {
                _logger.LogInformation(
                    "WhatsApp is not configured for organization {OrganizationId}; notification skipped.",
                    _tenant.OrganizationId);
                return false;
            }
            var payload = new GreenApiSendMessageRequest { ChatId = chatId, Message = message };

            try
            {
                using var response = await _http.PostAsJsonAsync(url, payload, ct);
                if (response.IsSuccessStatusCode) return true;
                _logger.LogWarning("Green API failed with HTTP {StatusCode}.", (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                                       ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Green API delivery failed ({ErrorType}).", ex.GetType().Name);
            }
            return false;
        }

        private string? BuildSendMessageUrl()
        {
            var options = _tenant.Organization?.WhatsApp;
            if (options is null || !options.IsConfigured)
            {
                return null;
            }

            return $"{options.ApiUrl!.TrimEnd('/')}/waInstance{options.IdInstance}/sendMessage/{options.ApiTokenInstance}";
        }

        public async Task<bool> SendReminderBeforeHourAsync(
            ReservationInfo reservation,
            DateTime dateTime,
            CancellationToken ct = default
            )
        {
            var chatId = ToChatId(reservation.CustomerPhone);
            if (chatId == null) return false;

            var text = _messages.Reminder(reservation, dateTime);

            return await SendMessageAsync(chatId, text, ct);
        }

        /// <summary>
        /// "8 (700) 123-45-67" → "77001234567@c.us"
        /// </summary>
        private static string? ToChatId(string phone)
        {
            var digits = new string(phone.Where(char.IsDigit).ToArray());

            if (digits.StartsWith('8') && digits.Length == 11)
                digits = "7" + digits[1..];

            if (digits.Length == 10)
                digits = "7" + digits;

            if (digits.Length != 11 || !digits.StartsWith('7'))
                return null;

            return $"{digits}@c.us";
        }

        // DTO для Green API — можно оставить private внутри этого файла
        private sealed class GreenApiSendMessageRequest
        {
            [JsonPropertyName("chatId")]
            public string ChatId { get; set; } = string.Empty;

            [JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;
        }
    }
}
