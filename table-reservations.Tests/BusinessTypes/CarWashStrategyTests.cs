using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services.BusinessTypes;

namespace table_reservations.Tests.BusinessTypes;

public class CarWashStrategyTests
{
    private readonly CarWashStrategy _strategy = new();

    private static string FutureAt(int hour, int minute = 0)
    {
        var date = ReservationDateTime.KazakhstanNow().Date.AddDays(3);
        var scheduled = date.AddHours(hour).AddMinutes(minute);
        return scheduled.ToString(ReservationDateTime.Format);
    }

    private static ReservationInfo ValidRequest() => new()
    {
        PlateNumber = "777GUW06",
        CustomerPhone = "+77010000000",
        WashServiceType = "Комплекс",
        ScheduledAt = FutureAt(9),
        RemindBeforeHour = true
    };

    [Fact]
    public void Type_IsCarWash()
    {
        Assert.Equal(BusinessType.CarWash, _strategy.Type);
    }

    [Fact]
    public void ValidateCreate_ValidRequest_ReturnsValid()
    {
        var result = _strategy.ValidateCreate(ValidRequest());

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ValidateCreate_NullRequest_ReturnsInvalid()
    {
        var result = _strategy.ValidateCreate(null!);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_MissingPlateNumber_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.PlateNumber = "";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_MissingPhone_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.CustomerPhone = "";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_MissingWashServiceType_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.WashServiceType = "";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_InvalidDateFormat_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.ScheduledAt = "nope";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_TimeInThePast_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.ScheduledAt = ReservationDateTime.KazakhstanNow()
            .AddHours(-1)
            .ToString(ReservationDateTime.Format);

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(6)]  // early morning - car wash is open all day
    [InlineData(8)]  // outside restaurant hours, still valid here
    [InlineData(23)]
    public void ValidateCreate_AnyHour_ReturnsValid(int hour)
    {
        var request = ValidRequest();
        request.ScheduledAt = FutureAt(hour);

        var result = _strategy.ValidateCreate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void BuildReservationRow_ValidRequest_MapsCarWashSchema()
    {
        var request = ValidRequest();
        var scheduledAt = ReservationDateTime.KazakhstanNow().Date.AddDays(3).AddHours(9);

        var row = _strategy.BuildReservationRow(request, scheduledAt);

        // Schema: id, plate number, reservation time, phone number, wash service type.
        Assert.Equal(5, row.Count);
        Assert.Equal(request.PlateNumber, row[1]);
        Assert.Equal(scheduledAt.ToString(ReservationDateTime.Format), row[2]);
        Assert.Equal(request.CustomerPhone, row[3]);
        Assert.Equal(request.WashServiceType, row[4]);
    }
}
