using Google.Apis.Sheets.v4.Data;
using table_reservations.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace table_reservations.Pos;

public interface IPosAdapter
{
    string ProviderName { get; }

    // Авторизация. У iiko это Bearer-токен с истечением через ~60 минут,
    // у Paloma — статичный API-ключ, у r_keeper — сессионный ключ.
    Task AuthenticateAsync(CancellationToken ct = default);
    Task<List<PosTable>> GetTablesAsync(CancellationToken ct = default);
    Task<List<ActiveOrderDto>> GetActiveOrdersAsync(CancellationToken ct = default);

    Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default);

    Task<List<ReservationInfoDto>> GetReservationsAsync(CancellationToken ct = default);
    Task<OrderStatusResult> GetOrderStatusAsync(string externalOrderId, CancellationToken ct = default);

    // Отменить заказ.
    // "отменить", потому что кассы не позволяют физически удалить заказ,
    // только перевести его в статус "отменён".
    Task<OrderResult> CancelOrderAsync(string externalOrderId, CancellationToken ct = default);

    Task<ReservationResult> ReserveTableAsync(CreateReservationRequest request, CancellationToken ct = default);

    Task<ReservationResult> CancelReservationAsync(string externalReservationId, CancellationToken ct = default);
}