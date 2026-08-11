using Google.Apis.Sheets.v4.Data;
using table_reservations.Models;

namespace table_reservations.Pos
{
    // Декоратор: гарантирует аутентификацию перед каждым запросом
    public class AuthenticatingPosAdapter : IPosAdapter
    {
        private readonly IPosAdapter _inner;
        private DateTime _lastAuthAt = DateTime.MinValue;
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50); // токен iiko живёт ~60 мин, берём с запасом

        public AuthenticatingPosAdapter(IPosAdapter inner) => _inner = inner;

        public string ProviderName => _inner.ProviderName;

        private async Task EnsureAuthAsync(CancellationToken ct)
        {
            if (DateTime.UtcNow - _lastAuthAt > TokenLifetime)
            {
                await _inner.AuthenticateAsync(ct);
                _lastAuthAt = DateTime.UtcNow;
            }
        }

        public Task AuthenticateAsync(CancellationToken ct = default) => _inner.AuthenticateAsync(ct);

        public async Task<List<PosTable>> GetTablesAsync(CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.GetTablesAsync(ct);
        }

        public async Task<List<ActiveOrderDto>> GetActiveOrdersAsync(CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.GetActiveOrdersAsync(ct);
        }

        public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.CreateOrderAsync(request, ct);
        }

        public async Task<List<ReservationInfoDto>> GetReservationsAsync(CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.GetReservationsAsync(ct);
        }

        public async Task<OrderStatusResult> GetOrderStatusAsync(string externalOrderId, CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.GetOrderStatusAsync(externalOrderId, ct);
        }

        public async Task<OrderResult> CancelOrderAsync(string externalOrderId, CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.CancelOrderAsync(externalOrderId, ct);
        }

        public async Task<ReservationResult> ReserveTableAsync(CreateReservationRequest request, CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.ReserveTableAsync(request, ct);
        }

        public async Task<ReservationResult> CancelReservationAsync(string externalReservationId, CancellationToken ct = default)
        {
            await EnsureAuthAsync(ct);
            return await _inner.CancelReservationAsync(externalReservationId, ct);
        }
    }
}