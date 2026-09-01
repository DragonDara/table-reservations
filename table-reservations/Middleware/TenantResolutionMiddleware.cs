using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Services.Tenancy;

namespace table_reservations.Middleware
{
    /// <summary>
    /// Resolves the current organization (tenant) for each request and populates the
    /// scoped <see cref="TenantContext"/>. Resolution order:
    /// <list type="number">
    /// <item>The left-most label of the request Host (subdomain), e.g. "theveil" in "theveil.bron.cafe".</item>
    /// <item>The <c>X-Organization-Id</c> header as a fallback, honored only when the host has no
    /// subdomain and the app is running in the Development environment (localhost).</item>
    /// </list>
    /// API requests (paths starting with <c>/api</c>) require a resolved tenant and are
    /// short-circuited with <c>400</c> when none can be determined. Non-API requests
    /// (static files, SPA fallback) are allowed through unresolved.
    /// </summary>
    public sealed class TenantResolutionMiddleware
    {
        private const string OrganizationIdHeader = "X-Organization-Id";

        private readonly RequestDelegate _next;
        private readonly ILogger<TenantResolutionMiddleware> _logger;
        private readonly bool _isDevelopment;
        private readonly string[] _baseDomains;

        public TenantResolutionMiddleware(
            RequestDelegate next,
            ILogger<TenantResolutionMiddleware> logger,
            IHostEnvironment environment,
            IOptions<TenantRoutingOptions> routingOptions)
        {
            _next = next;
            _logger = logger;
            _isDevelopment = environment.IsDevelopment();
            _baseDomains = (routingOptions.Value.BaseDomains ?? Array.Empty<string>())
                .Select(NormalizeHost)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (_baseDomains.Length == 0)
            {
                throw new InvalidOperationException("At least one tenant base domain must be configured.");
            }
        }

        public async Task InvokeAsync(HttpContext context, OrganizationRegistry registry, TenantContext tenant)
        {
            var resolution = ResolveOrganization(
                context,
                registry,
                _isDevelopment,
                _baseDomains,
                out var organization);
            if (resolution == TenantResolution.Resolved)
            {
                tenant.Set(organization);
            }
            else if (IsApiRequest(context.Request.Path))
            {
                _logger.LogWarning(
                    "Could not resolve organization for {Path}. Host: {Host}, Header: {Header}",
                    context.Request.Path,
                    context.Request.Host.Host,
                    context.Request.Headers[OrganizationIdHeader].ToString());

                var unknownTenant = resolution == TenantResolution.Unknown;
                context.Response.StatusCode = unknownTenant
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = unknownTenant
                        ? "The specified organization is not configured."
                        : "Organization identity is required. Provide a subdomain or the X-Organization-Id header."
                });
                return;
            }

            await _next(context);
        }

        private static TenantResolution ResolveOrganization(
            HttpContext context,
            OrganizationRegistry registry,
            bool isDevelopment,
            IReadOnlyCollection<string> baseDomains,
            out Configuration.OrganizationOptions organization)
        {
            var subdomain = ExtractSubdomain(context.Request.Host.Host, baseDomains);
            if (subdomain is not null)
            {
                return registry.TryGetBySubdomain(subdomain, out organization)
                    ? TenantResolution.Resolved
                    : TenantResolution.Unknown;
            }

            // Localhost/dev-only fallback: outside Development the tenant must come from the
            // subdomain, so the X-Organization-Id header is ignored to keep production strict.
            if (!isDevelopment)
            {
                organization = default!;
                return TenantResolution.Missing;
            }

            var headerId = context.Request.Headers[OrganizationIdHeader].ToString();
            if (string.IsNullOrWhiteSpace(headerId))
            {
                organization = default!;
                return TenantResolution.Missing;
            }

            // In development the header may carry either the organization id or one of
            // its subdomains (e.g. "thetochka" or "thetochka-carwash"), so try both.
            if (registry.TryGetById(headerId, out organization)
                || registry.TryGetBySubdomain(headerId, out organization))
            {
                return TenantResolution.Resolved;
            }

            return TenantResolution.Unknown;
        }

        private static string? ExtractSubdomain(
            string? host,
            IReadOnlyCollection<string> baseDomains)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            var normalizedHost = NormalizeHost(host);
            var labels = normalizedHost.Split('.', StringSplitOptions.RemoveEmptyEntries);

            // Local development: browsers resolve "*.localhost" to 127.0.0.1 automatically,
            // so "theveil.localhost" should map to the "theveil" tenant.
            // The last label being "localhost" means every preceding label is a subdomain, e.g.
            // "theveil.localhost" -> "theveil". A bare "localhost" has no subdomain.
            if (string.Equals(labels[^1], "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return labels.Length < 2 ? null : NormalizeCandidate(labels[0]);
            }

            foreach (var baseDomain in baseDomains)
            {
                var suffix = $".{baseDomain}";
                if (!normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = normalizedHost[..^suffix.Length];
                // Only one tenant label is supported. This prevents unrelated or
                // nested domains from being interpreted as trusted tenant hosts.
                if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains('.'))
                {
                    return null;
                }

                return NormalizeCandidate(candidate);
            }

            return null;
        }

        private static string NormalizeHost(string host) =>
            host.Trim().TrimEnd('.').ToLowerInvariant();

        private static string? NormalizeCandidate(string candidate)
        {
            return string.Equals(candidate, "www", StringComparison.OrdinalIgnoreCase) ? null : candidate;
        }

        private static bool IsApiRequest(PathString path) =>
            path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

        private enum TenantResolution
        {
            Missing,
            Unknown,
            Resolved
        }
    }
}
