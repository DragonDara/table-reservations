using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using table_reservations.Controllers;

namespace table_reservations.Tests.Controllers;

public class ReservationsControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("08.09.2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-9-8")]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public async Task ListRejectsInvalidDatesBeforeAccessingStorage(string? date)
    {
        var controller = new ReservationsController(null!, null!, null!, null!, NullLogger<ReservationsController>.Instance);
        Assert.IsType<BadRequestObjectResult>(await controller.GetReservations(date, CancellationToken.None));
    }
}
