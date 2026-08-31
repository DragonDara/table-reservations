using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Middleware;
using table_reservations.Models.Tenancy;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests.Middleware;

public class TenantResolutionMiddlewareTests
{
    private static OrganizationRegistry BuildRegistry() => new(Options.Create(new OrganizationsOptions
    {
        Items = new List<OrganizationOptions>
        {
            new()
            {
                Id = "theveil",
                Subdomains = new[] { "theveil" },
                BusinessType = BusinessType.Restaurant
            },
            new()
            {
                Id = "sparkle-wash",
                Subdomains = new[] { "sparkle-wash" },
                BusinessType = BusinessType.CarWash
            }
        }
    }));

    private static (TenantResolutionMiddleware Middleware, bool[] NextCalled) BuildMiddleware(
        string environmentName = "Development")
    {
        var nextCalled = new[] { false };
        RequestDelegate next = _ =>
        {
            nextCalled[0] = true;
            return Task.CompletedTask;
        };

        var environment = new FakeHostEnvironment { EnvironmentName = environmentName };

        var middleware = new TenantResolutionMiddleware(
            next,
            NullLogger<TenantResolutionMiddleware>.Instance,
            environment);

        return (middleware, nextCalled);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "table-reservations.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static DefaultHttpContext ApiContext(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/reservations";
        context.Request.Host = new HostString(host);
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task ApiRequest_NoSubdomainNoHeader_Returns400()
    {
        var (middleware, nextCalled) = BuildMiddleware();
        var context = ApiContext("bron.cafe"); // no subdomain, no header
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, BuildRegistry(), tenant);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(nextCalled[0]);
        Assert.False(tenant.IsResolved);
    }

    [Fact]
    public async Task ApiRequest_UnknownSubdomain_Returns404()
    {
        var (middleware, nextCalled) = BuildMiddleware();
        var context = ApiContext("ghost.bron.cafe");
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, BuildRegistry(), tenant);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled[0]);
        Assert.False(tenant.IsResolved);
    }

    [Fact]
    public async Task ApiRequest_UnknownHeader_Returns404()
    {
        var (middleware, nextCalled) = BuildMiddleware();
        var context = ApiContext("bron.cafe");
        context.Request.Headers["X-Organization-Id"] = "does-not-exist";
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, BuildRegistry(), tenant);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled[0]);
        Assert.False(tenant.IsResolved);
    }

    [Fact]
    public async Task ApiRequest_ResolvedBySubdomain_PassesThroughAndSetsTenant()
    {
        var (middleware, nextCalled) = BuildMiddleware();
        var context = ApiContext("theveil.bron.cafe");
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, BuildRegistry(), tenant);

        Assert.True(nextCalled[0]);
        Assert.True(tenant.IsResolved);
        Assert.Equal("theveil", tenant.OrganizationId);
        Assert.Equal(BusinessType.Restaurant, tenant.BusinessType);
    }

    [Fact]
    public async Task ApiRequest_ResolvedByHeader_InDevelopment_PassesThroughAndSetsTenant()
    {
        var (middleware, nextCalled) = BuildMiddleware("Development");
        var context = ApiContext("bron.cafe");
        context.Request.Headers["X-Organization-Id"] = "sparkle-wash";
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, BuildRegistry(), tenant);

        Assert.True(nextCalled[0]);
        Assert.True(tenant.IsResolved);
        Assert.Equal("sparkle-wash", tenant.OrganizationId);
        Assert.Equal(BusinessType.CarWash, tenant.BusinessType);
    }

    [Fact]
    public async Task ApiRequest_HeaderIgnored_OutsideDevelopment_Returns400()
    {
        var (middleware, nextCalled) = BuildMiddleware("Production");
        var context = ApiContext("bron.cafe");
        context.Request.Headers["X-Organization-Id"] = "sparkle-wash";
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, BuildRegistry(), tenant);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(nextCalled[0]);
        Assert.False(tenant.IsResolved);
    }

    [Fact]
    public async Task NonApiRequest_Unresolved_PassesThroughWithoutTenant()
    {
        var (middleware, nextCalled) = BuildMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/index.html";
        context.Request.Host = new HostString("bron.cafe");
        context.Response.Body = new MemoryStream();
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, BuildRegistry(), tenant);

        Assert.True(nextCalled[0]);
        Assert.False(tenant.IsResolved);
    }
}
