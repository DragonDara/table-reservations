using Microsoft.AspNetCore.Mvc;
using table_reservations.Models.Tenancy;
using table_reservations.Services.Tenancy;

namespace table_reservations.Controllers
{
    /// <summary>
    /// Serves browser-safe public configuration for the tenant resolved by
    /// <c>TenantResolutionMiddleware</c>. Only allow-listed branding/content/theme
    /// values are returned; spreadsheet ids, credentials, and sheet schema are never
    /// exposed here.
    /// </summary>
    [ApiController]
    [Route("api/tenant")]
    public sealed class TenantConfigController : ControllerBase
    {
        private readonly TenantContext _tenant;

        public TenantConfigController(TenantContext tenant)
        {
            _tenant = tenant;
        }

        [HttpGet("public-config")]
        public ActionResult<PublicTenantConfigResponse> GetPublicConfig()
        {
            if (!_tenant.IsResolved || _tenant.Organization is null)
            {
                // Middleware should have short-circuited unresolved API requests, but
                // guard here so this endpoint never leaks another tenant's config.
                return NotFound(new { error = "The specified organization is not configured." });
            }

            var response = PublicTenantConfigResponse.FromOrganization(_tenant.Organization);

            Response.Headers.CacheControl = "public, max-age=60";

            return Ok(response);
        }
    }
}
