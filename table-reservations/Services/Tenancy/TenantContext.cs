using table_reservations.Configuration;
using table_reservations.Models.Tenancy;

namespace table_reservations.Services.Tenancy
{
    /// <summary>
    /// Scoped holder for the organization (tenant) resolved for the current request.
    /// Populated by <c>TenantResolutionMiddleware</c> and consumed by services that
    /// need per-tenant configuration (spreadsheet id, credentials, schema, rules).
    /// </summary>
    public sealed class TenantContext
    {
        public OrganizationOptions? Organization { get; private set; }

        public bool IsResolved => Organization is not null;

        public string OrganizationId =>
            Organization?.Id ?? throw new InvalidOperationException("Tenant has not been resolved for the current request.");

        public BusinessType BusinessType =>
            Organization?.BusinessType ?? throw new InvalidOperationException("Tenant has not been resolved for the current request.");

        public void Set(OrganizationOptions organization) =>
            Organization = organization ?? throw new ArgumentNullException(nameof(organization));
    }
}
