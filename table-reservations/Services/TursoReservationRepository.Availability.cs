using System.Globalization;
using table_reservations.Constants;
using table_reservations.Models;

namespace table_reservations.Services;

public sealed partial class TursoReservationRepository
{
    private async Task<State> AvailabilityStateAsync(CancellationToken ct)
    {
        var state = await StateAsync(db, false, ct);
        return UsesPos ? WithPos(state, await Pos.GetSnapshotAsync(ct: ct)) : state;
    }

    private State WithPos(State state, PosAvailabilitySnapshot snapshot)
    {
        var mappings = tenant.Organization!.Pos.TableMappings;
        var inventory = snapshot.Tables.Select(table => table.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasDuplicateTableMappings = mappings.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != mappings.Count;
        var resources = state.Resources.Where(resource =>
            mappings.ContainsKey(int.Parse(resource.Id, CultureInfo.InvariantCulture))).ToArray();
        var hasUnknownTableMappings = resources.Any(resource =>
            !inventory.Contains(mappings[int.Parse(resource.Id, CultureInfo.InvariantCulture)]));
        if (hasDuplicateTableMappings || hasUnknownTableMappings)
            throw new BookingException("Не удалось сопоставить столики с кассой.", 503, "POS_UNAVAILABLE");
        var resourceIds = resources.Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        var rows = state.Reservations.Where(reservation => resourceIds.Contains(reservation.ResourceId)).ToList();
        var now = ReservationDateTime.KazakhstanNow();
        var closing = BookingRules.BusinessDayClosing(now, Hours);
        foreach (var resource in resources)
        {
            var externalId = mappings[int.Parse(resource.Id, CultureInfo.InvariantCulture)];
            if (snapshot.OccupiedTableIds.Contains(externalId) && closing > now)
                rows.Add(new("pos-order:" + externalId, resource.Id, "", "", "", now, closing, false));
            foreach (var reservation in snapshot.Reservations.Where(reserve => reserve.TableIds.Contains(externalId, StringComparer.OrdinalIgnoreCase)))
            {
                if (reservation.DurationInMinutes <= 0)
                    throw new BookingException("Не удалось проверить длительность брони в кассе.", 503, "POS_UNAVAILABLE");
                rows.Add(new("pos-reserve:" + reservation.ReservationId, resource.Id, "", "", "", reservation.EstimatedStartTime,
                    reservation.EstimatedStartTime.AddMinutes(reservation.DurationInMinutes), false));
            }
        }
        return new(resources, rows, snapshot.CheckedAt);
    }
}