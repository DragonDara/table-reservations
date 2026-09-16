# Уведомления о бронях в Telegram

У каждой организации есть собственный постоянный секрет подключения. Он хранится
в `Organizations.Items[].Telegram.ConnectionCode` в `appsettings.json`:

```json
{
  "Id": "thetochka",
  "Telegram": {
    "ConnectionCode": "thetochka-УНИКАЛЬНЫЙ_СЕКРЕТ"
  }
}
```

Секреты организаций должны быть разными, не содержать пробелов и иметь не более
128 символов. Они сравниваются с учётом регистра, не возвращаются публичным API
и не записываются в журнал. Секрет передаётся только сотрудникам соответствующего
бизнеса.

## Подключение группы

1. Владелец создаёт Telegram-группу или выбирает существующую.
2. Добавляет в неё `@reservremindbot`. Боту нужны права отправлять сообщения.
3. Любой сотрудник, знающий секрет организации, отправляет внутри группы:

   ```text
   /connect СЕКРЕТ_ОРГАНИЗАЦИИ
   ```

   В группе с несколькими ботами можно указать имя:

   ```text
   /connect@reservremindbot СЕКРЕТ_ОРГАНИЗАЦИИ
   ```

Сервер находит организацию по секрету и сохраняет идентификатор группы в Turso.
Проверки статуса администратора через Telegram API нет: секрет является полной
авторизацией подключения.

Одна организация имеет одну группу. Повторный `/connect` с тем же секретом в
другой группе переносит туда будущие уведомления. Одна группа не может быть
подключена к нескольким организациям.

Для отключения в подключённой группе нужен тот же секрет:

```text
/disconnect СЕКРЕТ_ОРГАНИЗАЦИИ
```

Поддерживаются обычные группы и супергруппы. Личные чаты и каналы игнорируются.

## Настройка Telegram-бота

Глобальные настройки бота задаются отдельно от секретов организаций:

```json
"Telegram": {
  "BotToken": "ТОКЕН_ОТ_BOTFATHER",
  "BotUsername": "reservremindbot",
  "WebhookSecret": "СЛУЧАЙНЫЙ_WEBHOOK_СЕКРЕТ"
}
```

- `BotUsername` указывается без `@` и является username, а не отображаемым именем.
- `WebhookSecret` содержит 32–256 букв, цифр, `_` или `-`. Клиенты его не вводят;
  Telegram передаёт его серверу в `X-Telegram-Bot-Api-Secret-Token`.
- Токен и webhook-секрет рекомендуется задавать секретами окружения
  `Telegram__BotToken` и `Telegram__WebhookSecret`.

Перед первым запуском создайте таблицы:

```powershell
dotnet run --project table-reservations --no-launch-profile -- --migrate
```

## Однократная регистрация webhook

Приложение не регистрирует webhook автоматически. После развертывания один раз
вызовите Telegram `setWebhook`, указав публичный HTTPS URL backend. В DigitalOcean
маршрут должен вести к ASP.NET API:

```text
https://YOUR_BACKEND/api/integrations/telegram/webhook
```

Пример PowerShell:

```powershell
$body = @{
    url = 'https://YOUR_BACKEND/api/integrations/telegram/webhook'
    secret_token = $env:Telegram__WebhookSecret
    allowed_updates = @('message')
} | ConvertTo-Json

$endpoint = 'https://api.telegram.org/bot' + $env:Telegram__BotToken + '/setWebhook'
Invoke-RestMethod -Method Post -Uri $endpoint -ContentType 'application/json' -Body $body
```

Проверьте регистрацию без вывода токена в журнал:

```powershell
$endpoint = 'https://api.telegram.org/bot' + $env:Telegram__BotToken + '/getWebhookInfo'
Invoke-RestMethod -Uri $endpoint
```

Webhook не требует tenant-домен или `X-Organization-Id`: tenant определяется только
из секрета команды `/connect`. Сам HTTP-запрос всё равно проверяется глобальным
`WebhookSecret`.

## Доставка

- В группу копируются WhatsApp-сообщения о новой брони и включённые напоминания.
- Ошибка Telegram не отменяет бронь и не блокирует WhatsApp.
- Успех напоминаний хранится отдельно по организации, брони и каналу, поэтому при
  частичном сбое повторяется только неуспешный канал.
- Ответ создания брони содержит `telegramSent`.
- При преобразовании группы в супергруппу сохранённый chat ID обновляется.

Проверка реализации без внешних отправок:

```powershell
dotnet test table-reservations.slnx
```
