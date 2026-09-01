using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using table_reservations.Helpers;
using table_reservations.Models;
using table_reservations.Services.Tenancy;

namespace table_reservations.Pos;


// Контроллер работает ТОЛЬКО с PosBookingService сервисом и 
// ReservationInfo, что пришла с сайта — весь маппинг в CreateReservationRequest

public class PosBookingService
{
    private readonly IPosAdapter _pos;
    private readonly string _restaurantId;

    public PosBookingService(IHttpClientFactory httpClientFactory, TenantContext tenant)
    {
        var organization = tenant.Organization
            ?? throw new InvalidOperationException("Tenant must be resolved before creating a POS service.");
        var options = organization.Pos;

        if (!options.Enabled)
        {
            throw new InvalidOperationException($"POS is not enabled for organization '{organization.Id}'.");
        }

        if (!string.Equals(options.Provider, "iiko", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"POS provider '{options.Provider}' is not supported for organization '{organization.Id}'.");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || string.IsNullOrWhiteSpace(options.ApiLogin)
            || string.IsNullOrWhiteSpace(options.OrganizationId))
        {
            throw new InvalidOperationException(
                $"Complete iiko settings are required for organization '{organization.Id}'.");
        }

        var http = httpClientFactory.CreateClient("TenantPos");
        http.BaseAddress = baseUri;
        _pos = new AuthenticatingPosAdapter(
            new IikoAdapter(http, options.ApiLogin, options.OrganizationId));
        _restaurantId = options.OrganizationId;
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
