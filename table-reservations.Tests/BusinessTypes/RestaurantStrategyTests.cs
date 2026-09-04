using table_reservations.Constants;
using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Services.BusinessTypes;

namespace table_reservations.Tests.BusinessTypes;

public class RestaurantStrategyTests
{
    private readonly RestaurantStrategy _strategy = new();

    private static string FutureAt(int hour, int minute = 0)
    {
        // Base off the same clock the strategy uses so tests stay deterministic
        // regardless of the host timezone. Three days ahead clears the +5 min rule.
        var date = ReservationDateTime.KazakhstanNow().Date.AddDays(3);
        var scheduled = date.AddHours(hour).AddMinutes(minute);
        return scheduled.ToString(ReservationDateTime.Format);
    }

    private static ReservationInfo ValidRequest() => new()
    {
        TablesId = "1,2",
        CustomerName = "Иван",
        CustomerPhone = "+77010000000",
        Section = "main",
        ScheduledAt = FutureAt(20),
        RemindBeforeHour = true
    };

    [Fact]
    public void Type_IsRestaurant()
    {
        Assert.Equal(BusinessType.Restaurant, _strategy.Type);
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
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateCreate_MissingTablesId_ReturnsInvalid(string tablesId)
    {
        var request = ValidRequest();
        request.TablesId = tablesId;

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_MissingCustomerName_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.CustomerName = "";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_MissingSection_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.Section = "";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_InvalidDateFormat_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.ScheduledAt = "not-a-date";

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

    [Fact]
    public void ValidateCreate_BusinessHoursAreHandledByTenantSchedule()
    {
        var request = ValidRequest();
        request.ScheduledAt = FutureAt(8);

        var result = _strategy.ValidateCreate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(13)] // afternoon
    [InlineData(23)] // late evening
    [InlineData(2)]  // after midnight, before close
    public void ValidateCreate_WithinWorkingHours_ReturnsValid(int hour)
    {
        var request = ValidRequest();
        request.ScheduledAt = FutureAt(hour);

        var result = _strategy.ValidateCreate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_DuplicateTableIds_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.TablesId = "3,3";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCreate_NonNumericTableId_ReturnsInvalid()
    {
        var request = ValidRequest();
        request.TablesId = "abc";

        var result = _strategy.ValidateCreate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void BuildReservationRow_ValidRequest_MapsRestaurantSchema()
    {
        var request = ValidRequest();
        var scheduledAt = ReservationDateTime.KazakhstanNow().Date.AddDays(3).AddHours(20);

        var row = _strategy.BuildReservationRow(request, scheduledAt);

        Assert.Equal(7, row.Count);
        Assert.Equal("1,2", row[1]);
        Assert.Equal(request.CustomerName, row[2]);
        Assert.Equal(request.CustomerPhone, row[3]);
        Assert.Equal(scheduledAt.ToString(ReservationDateTime.Format), row[4]);
        Assert.Equal("Да", row[6]);
    }
}
