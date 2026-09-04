using System;
using System.Collections.Generic;

namespace table_reservations.Models;


// Модели уровня POS-интеграции (не путать с
// доменными моделями ReservationInfo/TableInfo,
// которые используются в Google Sheets).
// Эти типы — общий контракт для ЛЮБОГО POS-адаптера
// (iiko, Paloma, r_keeper и т.д.), поэтому не должны
// содержать ничего специфичного для конкретной кассы.

public record CustomerInfo(
    string Name,
    string Phone
);

public record Table(
    string Id,
    string Name,
    int Seats = 0,
    bool IsAvailable = true,
    int Number = 0,
    string SectionName = ""
);

public record CreateReservationRequest(
    string RestaurantId,
    CustomerInfo Customer,
    DateTime ReservationTime,
    int GuestsCount,
    List<string> TableIds // может быть несколько столов в одной брони (например "3,5")
);

public record ReservationResult(
    bool Success,
    string? ExternalReservationId,
    string? ErrorMessage
);

public record ReservationInfoDto(
    string ReservationId,
    List<string> TableIds,
    string CustomerName,
    string CustomerPhone,
    DateTime EstimatedStartTime,
    int DurationInMinutes,
    int GuestsCount
);

// Один товар в заказе (позиция чека)
public record OrderItem(
    string ExternalProductId, // ID товара в системе кассы (не в БД)
    decimal Quantity,
    decimal Price             // цена за единицу на момент заказа
);

// Запрос на создание заказа — то, что твой код ЗНАЕТ и хочет отправить
public record CreateOrderRequest(
    string RestaurantId,      // у каждой кассы свой ID заведения/организации
    CustomerInfo Customer,
    List<OrderItem> Items,
    string? TableId = null    // nullable — заказ навынос может быть без стола
);

// Результат создания заказа. НЕ exception, а объект-результат.
public record OrderResult(
    bool Success,             // получилось ли создать заказ
    string? ExternalOrderId,  // ID заказа, который вернула касса
    decimal? Total,           // итоговая сумма заказа
    string? ErrorMessage      // текст ошибки, если Success == false
);

// Статус существующего заказа
public record OrderStatusResult(
    bool Success,
    string Status,            // "New", "Cooking", "Ready", "Closed", "Cancelled" — свой enum-словарь
    string? ErrorMessage
);

// Активный заказ — возвращается методом GetActiveOrdersAsync()
public record ActiveOrderDto(
    string OrderId,           // ID заказа в кассовой системе
    string Status,            // "New", "Bill", "Closed", "Deleted" и т.д.
    List<string> TableIds,    // Столы, привязанные к этому заказу
    decimal Sum                // Сумма заказа
);