using table_reservations.Models.Tenancy;

namespace table_reservations.Services.BusinessTypes
{
    public interface IBusinessTypeStrategyResolver
    {
        IBusinessTypeStrategy Resolve(BusinessType type);
    }

    /// <summary>
    /// Resolves the <see cref="IBusinessTypeStrategy"/> for a given business type
    /// from the strategies registered in DI.
    /// </summary>
    public sealed class BusinessTypeStrategyResolver : IBusinessTypeStrategyResolver
    {
        private readonly IReadOnlyDictionary<BusinessType, IBusinessTypeStrategy> _strategies;

        public BusinessTypeStrategyResolver(IEnumerable<IBusinessTypeStrategy> strategies)
        {
            _strategies = strategies.ToDictionary(s => s.Type);
        }

        public IBusinessTypeStrategy Resolve(BusinessType type) =>
            _strategies.TryGetValue(type, out var strategy)
                ? strategy
                : throw new InvalidOperationException($"No reservation strategy registered for business type '{type}'.");
    }
}
