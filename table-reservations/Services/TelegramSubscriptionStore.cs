using table_reservations.Data;

namespace table_reservations.Services;

public sealed class TelegramSubscriptionStore(ITursoClient db)
{
    public static readonly string[] Schema = [
        "CREATE TABLE IF NOT EXISTS telegram_groups (organization_id TEXT PRIMARY KEY, chat_id INTEGER NOT NULL UNIQUE CHECK(chat_id < 0), title TEXT NOT NULL)",
        "CREATE TABLE IF NOT EXISTS notification_deliveries (organization_id TEXT NOT NULL, reservation_id TEXT NOT NULL, channel TEXT NOT NULL, sent_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY(organization_id,reservation_id,channel))"
    ];

    public async Task<long?> GetChatIdAsync(string organizationId, CancellationToken ct)
    {
        var result = await db.QueryAsync("SELECT chat_id FROM telegram_groups WHERE organization_id = ?", [organizationId], ct);
        return result.Rows.FirstOrDefault()?.GetInt64("chat_id");
    }

    public Task<bool> ConnectAsync(string organizationId, long chatId, string title, CancellationToken ct) =>
        db.TransactionAsync(async (connection, token) =>
        {
            if ((await connection.QueryAsync("SELECT organization_id FROM telegram_groups WHERE chat_id = ? AND organization_id <> ?",
                    [chatId, organizationId], token)).Rows.Count > 0) return false;
            await connection.ExecuteAsync("""
                INSERT INTO telegram_groups (organization_id,chat_id,title) VALUES (?,?,?)
                ON CONFLICT(organization_id) DO UPDATE SET chat_id=excluded.chat_id,title=excluded.title
                """, [organizationId, chatId, title], token);
            return true;
        }, ct);

    public Task<long> DisconnectAsync(long chatId, CancellationToken ct) =>
        db.ExecuteAsync("DELETE FROM telegram_groups WHERE chat_id = ?", [chatId], ct);

    public Task<long> MigrateChatAsync(long oldChatId, long newChatId, CancellationToken ct) =>
        db.ExecuteAsync("UPDATE telegram_groups SET chat_id = ? WHERE chat_id = ?", [newChatId, oldChatId], ct);

    public async Task<bool> WasDeliveredAsync(string organizationId, string reservationId, string channel, CancellationToken ct) =>
        (await db.QueryAsync("SELECT 1 FROM notification_deliveries WHERE organization_id = ? AND reservation_id = ? AND channel = ?",
            [organizationId, reservationId, channel], ct)).Rows.Count > 0;

    public Task<long> MarkDeliveredAsync(string organizationId, string reservationId, string channel, CancellationToken ct) =>
        db.ExecuteAsync("INSERT OR IGNORE INTO notification_deliveries (organization_id,reservation_id,channel) VALUES (?,?,?)",
            [organizationId, reservationId, channel], ct);
}
