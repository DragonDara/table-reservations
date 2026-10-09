namespace table_reservations.Models;

public sealed record PosAvailabilitySnapshot(DateTimeOffset CheckedAt, IReadOnlySet<string> OccupiedTableIds);