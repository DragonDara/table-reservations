namespace table_reservations.Models;

public sealed record VehicleCategory(string Id, string Name);
public sealed record CarWashService(string Id, string Name, bool IsPackage, string VehicleCategoryId, long PriceKzt, int? DurationMinutes);
public sealed record PackageItem(string PackageServiceId, string IncludedServiceId);
public sealed record CarWashCatalog(IReadOnlyList<VehicleCategory> Categories, IReadOnlyList<CarWashService> Services, IReadOnlyList<PackageItem> PackageItems);
public sealed record CarWashSelection(string VehicleCategoryId, string[] ServiceIds);
public sealed record CarWashQuote(IReadOnlyList<CarWashService> Services, long TotalKzt, int DurationMinutes);
public sealed record CarWashAvailability(CarWashQuote Quote, IReadOnlyList<string> Slots);
public sealed record BookingResult(string Id, bool Overwritten, string? BoxId = null, long? TotalKzt = null, int? DurationMinutes = null);
public sealed class BookingException(string message, int status = 400, string? code = null, ActiveReservationInfo? existing = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
    public ActiveReservationInfo? Existing { get; } = existing;
}
