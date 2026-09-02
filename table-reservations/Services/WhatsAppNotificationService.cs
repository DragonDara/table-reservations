using System.Net.Http.Json;
using System.Text.Json.Serialization;
using table_reservations.Constants;
using table_reservations.Models;

namespace table_reservations.Services
{
    public class WhatsAppNotificationService : IWhatsAppNotificationService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<WhatsAppNotificationService> _logger;

        public WhatsAppNotificationService(
            HttpClient http,
            IConfiguration config,
            ILogger<WhatsAppNotificationService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
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

            var adminPhone = _config["GreenApi:AdminPhone"];
            var adminChatId = string.IsNullOrWhiteSpace(adminPhone) ? null : ToChatId(adminPhone);
            if (adminChatId == null && !string.IsNullOrWhiteSpace(adminPhone))
                _logger.LogWarning("Некорректный AdminPhone: {Phone}", adminPhone);

            var customerTask = customerChatId != null
                ? SendMessageAsync(customerChatId, BuildCustomerMessage(reservation, dateTime, tableTypeLabel), ct)
                : Task.FromResult(false);

            var adminTask = adminChatId != null
                ? SendMessageAsync(adminChatId, BuildAdminMessage(reservation, dateTime, tableTypeLabel), ct)
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
            var payload = new GreenApiSendMessageRequest { ChatId = chatId, Message = message };

            using var response = await _http.PostAsJsonAsync(url, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Green API {StatusCode}: {Body}", (int)response.StatusCode, body);
                return false;
            }

            return true;
        }

        private string BuildSendMessageUrl()
        {
            var apiUrl = _config["GreenApi:ApiUrl"]
                ?? throw new InvalidOperationException("GreenApi:ApiUrl is not configured.");
            var idInstance = _config["GreenApi:IdInstance"]
                ?? throw new InvalidOperationException("GreenApi:IdInstance is not configured.");
            var apiToken = _config["GreenApi:ApiTokenInstance"]
                ?? throw new InvalidOperationException("GreenApi:ApiTokenInstance is not configured.");

            return $"{apiUrl.TrimEnd('/')}/waInstance{idInstance}/sendMessage/{apiToken}";
        }

        private static string BuildCustomerMessage(ReservationInfo reservation, DateTime dateTime, string tableTypeLabel)
        {
            return $"""
                Здравствуйте, {reservation.CustomerName}!

                Ваша бронь подтверждена:
                Стол №{reservation.TablesId}
                Секция: {reservation.Section}
                Тип столика: {tableTypeLabel}
                Дата и время: {dateTime.ToString(ReservationDateTime.Format)}

                Ждём вас!
                """;
        }

        private static string BuildAdminMessage(ReservationInfo reservation, DateTime dateTime, string tableTypeLabel)
        {
            return $"""
                Новая бронь!

                Клиент: {reservation.CustomerName}
                Телефон: {reservation.CustomerPhone}
                Стол №{reservation.TablesId}
                Секция: {reservation.Section}
                Тип столика: {tableTypeLabel}
                Дата и время: {dateTime.ToString(ReservationDateTime.Format)}
                """;
        }


        public async Task<bool> SendReminderBeforeHourAsync(
            ReservationInfo reservation,
            DateTime dateTime,
            CancellationToken ct = default
            )
        {
            var chatId = ToChatId(reservation.CustomerPhone);
            if (chatId == null) return false;

            var text =
             $"""
                Здравствуйте, {reservation.CustomerName}!

                Напоминаем, что у вас есть бронь в нашем заведении TheTochka в {dateTime.ToString(ReservationDateTime.Format)}
                Ваш столик №{reservation.TablesId}                

                Ждём вас!
                """;

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
