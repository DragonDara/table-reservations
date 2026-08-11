using System.Collections.Generic;
using System.Linq;
using table_reservations.Constants;
using table_reservations.Models;

namespace table_reservations.Helpers;

// Маппинг между "внешним" миром (POS-адаптеры: iiko, Paloma и т.д.)
// и "внутренним" миром приложения (доменная модель TableInfo).
//
// ВАЖНО: не подменяем TableInfo полностью данными от POS —
// у POS нет понятия Type (VIP/Обычный) и NextReservationHours,
// это бизнес-логика самого приложения. POS даёт только
// актуальную занятость стола в реальном времени.
//
// PosTable (а не просто Table) — потому что имя "Table" конфликтует
// с Google.Apis.Sheets.v4.Data.Table, который тоже подключён в проекте.
public static class TableMapper
{
    // Сопоставление идёт по номеру стола:
    // PosTable.Number  <->  TableInfo.Id
    // (оба должны совпадать с физическим номером стола в заведении)
    public static List<TableInfo> ApplyPosAvailability(
        List<TableInfo> domainTables,
        List<PosTable> posTables)
    {
        var posByNumber = posTables.ToDictionary(t => t.Number);

        foreach (var table in domainTables)
        {
            if (posByNumber.TryGetValue(table.Id, out var posTable))
            {
                // "limited" (частично занят, например, скоро освободится)
                // POS не умеет отдавать — такой статус выставляется только
                // твоей собственной логикой (NextReservationHours и т.п.)
                table.Status = posTable.IsAvailable
                    ? TableStatuses.Free
                    : TableStatuses.Occupied;
            }
            // Если POS не вернул стол с таким номером — оставляем статус как есть
            // (например, стол существует только в Google Sheets, ещё не заведён в кассе)
        }

        return domainTables;
    }
}