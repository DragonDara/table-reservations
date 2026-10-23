using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Middleware;
using table_reservations.Models.Tenancy;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests;

public class DisabledTenantTests
{
    [Theory]
    [InlineData("/api/Tables", false)]
    [InlineData("/api/Tables/slots", false)]
    [InlineData("/api/Reservations", false)]
    [InlineData("/api/tenant/public-config", true)]
    [InlineData("/api/rating", true)]
    public async Task PreviewTenantAllowsBrandingButCannotAccessBookingServices(string path, bool allowed)
    {
        var org = new OrganizationOptions { Id = "secret-veil", BookingEnabled = false, Subdomains = ["secret-veil"] };
        var registry = new OrganizationRegistry(Options.Create(new OrganizationsOptions { Items = [org] }));
        var called = false;
        var middleware = new TenantResolutionMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware>.Instance, Options.Create(new TenantRoutingOptions()));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("secret-veil.localhost");
        context.Request.Path = path;
        context.Request.Headers["X-Organization-Id"] = "thetochka";
        context.Response.Body = new MemoryStream();
        var tenant = new TenantContext();

        await middleware.InvokeAsync(context, registry, tenant);

        Assert.Equal("secret-veil", tenant.OrganizationId);
        Assert.Equal(allowed, called);
        Assert.Equal(allowed ? 200 : 503, context.Response.StatusCode);
    }

    [Fact]
    public void PreviewTenantDoesNotPublishInventedBookingHours()
    {
        var config = PublicTenantConfigResponse.FromOrganization(new OrganizationOptions { BookingEnabled = false });
        Assert.False(config.BookingEnabled);
        Assert.Empty(config.BookingTime.AvailableTimeSlots);
        Assert.Empty(config.BookingTime.StartTime);
        Assert.Empty(config.BookingTime.EndTime);
    }

    [Fact]
    public async Task RatingSnapshotDoesNotRequireProviderCredentialsOrNetwork()
    {
        var tenant = new TenantContext();
        tenant.Set(new OrganizationOptions
        {
            Id = "secret-veil",
            Rating = new RatingOptions { Enabled = true, Mode = "Snapshot", Value = 4.7, ReviewCount = 289 }
        });
        var service = new DgisRatingService(new HttpClient(new RejectNetwork()), tenant);
        var (rating, count) = await service.GetRatingAsync();
        Assert.Equal(4.7, rating);
        Assert.Equal(289, count);
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Snapshot must not call an external provider.");
    }
}
