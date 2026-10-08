namespace table_reservations.Models;

public sealed record PosAvailabilitySnapshot(DateTimeOffset CheckedAt, IReadOnlyList<PosTable> Tables,
    IReadOnlyList<ReservationInfoDto> Reservations, IReadOnlySet<string> OccupiedTableIds);