using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using table_reservations.Configuration;
using table_reservations.Controllers;
using table_reservations.Models.Tenancy;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests.Controllers;

public class TenantConfigControllerTests
{
    [Theory]
    [InlineData("thetochka", BusinessType.Restaurant)]
    [InlineData("thetochka-carwasher", BusinessType.CarWash)]
    public void PublicConfig_VariesCacheByTenantAndReturnsResolvedOrganization(string id, BusinessType type)
    {
        var tenant = new TenantContext();
        tenant.Set(new OrganizationOptions { Id = id, BusinessType = type });
        var controller = new TenantConfigController(tenant)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = Assert.IsType<OkObjectResult>(controller.GetPublicConfig().Result);
        var config = Assert.IsType<PublicTenantConfigResponse>(result.Value);

        Assert.Equal(id, config.OrganizationId);
        Assert.Equal(type, config.BusinessType);
        Assert.Equal("X-Organization-Id", controller.Response.Headers.Vary.ToString());
    }
}
