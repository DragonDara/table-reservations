using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services.Tenancy;

namespace table_reservations.Services;

public sealed class ReservationNotificationMessages(TenantContext tenant)
{
    public string Customer(ReservationInfo reservation, DateTime dateTime, string typeLabel)
    {
        if (tenant.BusinessType == BusinessType.CarWash)
        {
            return $"""
                Ваша запись подтверждена:
                Автомобиль: {reservation.PlateNumber}
                Услуга: {typeLabel}
                Дата и время: {dateTime.ToString(ReservationDateTime.Format)}

                Ждём вас в {tenant.Organization!.DisplayName}!
                """;
        }

        return $"""
            Здравствуйте, {reservation.CustomerName}!

            Ваша бронь подтверждена:
            Стол №{reservation.TablesId}
            Секция: {reservation.Section}
            Тип столика: {typeLabel}
            Дата и время: {dateTime.ToString(ReservationDateTime.Format)}

            Ждём вас!
            """;
    }

    public string Admin(ReservationInfo reservation, DateTime dateTime, string typeLabel)
    {
        if (tenant.BusinessType == BusinessType.CarWash)
        {
            return $"""
                Новая запись на автомойку!

                Телефон: {reservation.CustomerPhone}
                Автомобиль: {reservation.PlateNumber}
                Услуга: {typeLabel}
                Дата и время: {dateTime.ToString(ReservationDateTime.Format)}
                """;
        }

        return $"""
            Новая бронь!

            Клиент: {reservation.CustomerName}
            Телефон: {reservation.CustomerPhone}
            Стол №{reservation.TablesId}
            Секция: {reservation.Section}
            Тип столика: {typeLabel}
            Дата и время: {dateTime.ToString(ReservationDateTime.Format)}
            """;
    }


    public string Reminder(ReservationInfo reservation, DateTime dateTime)
    {
        var organizationName = tenant.Organization!.DisplayName;
        return tenant.BusinessType == BusinessType.CarWash
            ? $"""
                Напоминаем о записи в {organizationName} в {dateTime.ToString(ReservationDateTime.Format)}.
                Автомобиль: {reservation.PlateNumber}
                Услуга: {reservation.WashServiceType}

                Ждём вас!
                """
            : $"""
                Здравствуйте, {reservation.CustomerName}!

                Напоминаем, что у вас есть бронь в {organizationName} в {dateTime.ToString(ReservationDateTime.Format)}.
                Ваш столик №{reservation.TablesId}

                Ждём вас!
                """;

    }
}
