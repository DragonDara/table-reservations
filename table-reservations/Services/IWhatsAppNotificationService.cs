using table_reservations.Models;

namespace table_reservations.Services
{
    public interface IWhatsAppNotificationService
    {
        Task<(bool CustomerSent, bool AdminSent)> SendReservationNotificationsAsync(
            ReservationInfo reservation,
            DateTime dateTime,
            CancellationToken ct = default);
    }
}
