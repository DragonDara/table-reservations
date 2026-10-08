using System.Collections.Concurrent;
using System.Text.Json;
using table_reservations.Configuration;
using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Services;
using table_reservations.Services.Tenancy;

namespace table_reservations.Pos;

public interface IRestaurantPosClient
{
    Task<PosAvailabilitySnapshot> GetSnapshotAsync(CancellationToken ct = default);
    Task<ReservationResult> CreateAsync(CreateReservationRequest request, CancellationToken ct = default);
    Task<ReservationResult> GetAsync(string id, CancellationToken ct = default);
    Task<ReservationResult> CancelAsync(string id, CancellationToken ct = default);
}

public sealed class RestaurantPosSessions(IHttpClientFactory clients)
{
    internal sealed class Session(IPosAdapter adapter)
    {
        internal IPosAdapter Adapter { get; } = adapter;
        internal SemaphoreSlim Gate { get; } = new(1);
    }

    private readonly ConcurrentDictionary<string, Session> sessions = new(StringComparer.OrdinalIgnoreCase);

    internal Session Get(OrganizationOptions organization)
    {
        var settings = organization.Pos;
        if (!settings.Enabled || settings.Provider != "iiko"
            || !Guid.TryParse(settings.AppId, out var appId) || appId == Guid.Empty
            || string.IsNullOrWhiteSpace(settings.ClientSecret) || string.IsNullOrWhiteSpace(settings.ApiKey)
            || !Guid.TryParse(settings.OrganizationId, out _) || !Guid.TryParse(settings.TerminalGroupId, out _)
            || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || settings.TableMappings.Count == 0
            || settings.TableMappings.Any(pair => pair.Key <= 0 || !Guid.TryParse(pair.Value, out var id) || id == Guid.Empty)
            || settings.TableMappings.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.TableMappings.Count)
            throw new BookingException("Настройки связи с кассой не заполнены. Онлайн-бронирование временно недоступно.", 503, "POS_UNAVAILABLE");
        return sessions.GetOrAdd(organization.Id, _ => {
            var http = clients.CreateClient("TenantPos");
            http.BaseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
            return new(new AuthenticatingPosAdapter(new IikoAdapter(http, settings.AppId!, settings.ClientSecret!,
                settings.ApiKey!, settings.OrganizationId!, settings.TerminalGroupId)));
        });
    }
}

public sealed class RestaurantPosClient(RestaurantPosSessions sessions, TenantContext tenant,
    ILogger<RestaurantPosClient> logger) : IRestaurantPosClient
{
    private async Task<T> UseAsync<T>(Func<RestaurantPosSessions.Session, Task<T>> action, CancellationToken ct)
    {
        var session = sessions.Get(tenant.Organization!);
        await session.Gate.WaitAsync(ct);
        try { return await action(session); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning("POS unavailable for {OrganizationId}: {ErrorType}, endpoint {Endpoint}, HTTP {StatusCode}, reason {FailureReason}",
                tenant.OrganizationId, ex.GetType().Name, ex.Data["IikoEndpoint"],
                ex is HttpRequestException httpError ? (int?)httpError.StatusCode : null, ex.Data["IikoFailureReason"]);
            throw new BookingException("Не удалось проверить данные кассы. Попробуйте позже.", 503, "POS_UNAVAILABLE");
        }
        finally { session.Gate.Release(); }
    }

    public Task<PosAvailabilitySnapshot> GetSnapshotAsync(CancellationToken ct = default) => UseAsync(async session => {
        var now = ReservationDateTime.KazakhstanNow();
        var snapshot = await session.Adapter.GetAvailabilityAsync(now.Date.AddDays(-90), now.Date.AddDays(9), ct);
        if (DateTimeOffset.UtcNow - snapshot.CheckedAt >= TimeSpan.FromSeconds(30))
            throw new HttpRequestException("POS snapshot expired during refresh.");
        return snapshot;
    }, ct);

    public Task<ReservationResult> CreateAsync(CreateReservationRequest request, CancellationToken ct = default) =>
        UseAsync(session => session.Adapter.ReserveTableAsync(request, ct), ct);

    public Task<ReservationResult> GetAsync(string id, CancellationToken ct = default) => UseAsync(session => session.Adapter.GetReservationAsync(id, ct), ct);

    public Task<ReservationResult> CancelAsync(string id, CancellationToken ct = default) =>
        UseAsync(session => session.Adapter.CancelReservationAsync(id, ct), ct);
}