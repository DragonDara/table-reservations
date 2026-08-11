using System.Linq;
using table_reservations.Models;

namespace table_reservations.Helpers;

// Маппинг между "внешним" миром (POS-адаптеры: iiko, Paloma и т.д.)
// и "внутренним" миром приложения (Google Sheets / доменная модель).
// Ни один POS-адаптер не должен знать о ReservationInfo,
// ни один сервис Google Sheets не должен знать о ReservationInfoDto.
public static class ReservationMapper
{
    // Формат, в котором даты хранятся в колонке "ScheduledAt" в Google Sheets.
    // Подгони под реальный формат, который использует ReservationDateTime.
    private const string ScheduledAtFormat = "dd.MM.yyyy HH:mm";

    // POS -> Домен: бронь, пришедшая из кассы, приводим к виду,
    // который можно записать в Google Sheets.
    public static ReservationInfo ToDomain(ReservationInfoDto dto)
    {
        return new ReservationInfo
        {
            TablesId = string.Join(",", dto.TableIds),
            CustomerName = dto.CustomerName,
            CustomerPhone = dto.CustomerPhone,
            ScheduledAt = dto.EstimatedStartTime.ToString(ScheduledAtFormat),
            Section = string.Empty, // iiko DTO не содержит секцию/зал напрямую — заполняется отдельно, если нужно
            RemindBeforeHour = false
        };
    }

    // Домен -> POS: когда бронь создаётся у нас (например, через ReservationsController)
    // и её нужно продублировать в кассу.
    // guestsCount не хранится на сайте отдельным полем — считаем его
    // как сумму мест (Seats) у выбранных столов.
    public static CreateReservationRequest ToPosRequest(
        ReservationInfo domain,
        string restaurantId,
        System.DateTime scheduledAt,
        string[] tableIds,
        System.Collections.Generic.IEnumerable<TableInfo> selectedTables)
    {
        var guestsCount = selectedTables.Sum(t => t.Seats);

        return new CreateReservationRequest(
            RestaurantId: restaurantId,
            Customer: new CustomerInfo(domain.CustomerName, domain.CustomerPhone),
            ReservationTime: scheduledAt,
            GuestsCount: guestsCount,
            TableIds: tableIds.ToList()
        );
    }
}