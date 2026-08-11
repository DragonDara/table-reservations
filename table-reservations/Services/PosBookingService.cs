using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using table_reservations.Helpers;
using table_reservations.Models;

namespace table_reservations.Pos;


// Контроллер работает ТОЛЬКО с PosBookingService сервисом и 
// ReservationInfo, что пришла с сайта — весь маппинг в CreateReservationRequest

public class PosBookingService
{
    private readonly IPosAdapter _pos;
    private readonly string _restaurantId;

    public PosBookingService(PosAdapterFactory factory, IConfiguration config)
    {
        // PosProvider appsetting присвоенно iiko , но можно поменять на другой адаптер.
        var providerName = config["PosProvider"] ?? "iiko";
        _pos = factory.Get(providerName);

        // RestaurantId (organizationId в терминах iiko) — тоже из appsetting
        _restaurantId = config["Iiko:OrganizationId"]
            ?? throw new InvalidOperationException("Iiko:OrganizationId не задан в конфигурации.");
    }

    // контроллер вызывает именно его.
    // domain—ReservationInfo, что пришла с сайта 
    
    public async Task<ReservationResult> ReserveTableAsync(
        ReservationInfo domain,
        DateTime scheduledAt,
        string[] tableIds,
        IEnumerable<TableInfo> selectedTables,
        CancellationToken ct = default)
    {
        var posRequest = ReservationMapper.ToPosRequest(
            domain: domain,
            restaurantId: _restaurantId,
            scheduledAt: scheduledAt,
            tableIds: tableIds,
            selectedTables: selectedTables);

        return await _pos.ReserveTableAsync(posRequest, ct);
    }

   
    public Task<ReservationResult> CancelReservationAsync(string externalReservationId, CancellationToken ct = default)
    {
        return _pos.CancelReservationAsync(externalReservationId, ct);
    }


    public Task<List<PosTable>> GetTablesAsync(CancellationToken ct = default)
    {
        return _pos.GetTablesAsync(ct);
    }
}