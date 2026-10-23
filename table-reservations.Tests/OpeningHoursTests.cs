using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using table_reservations.Configuration;
using table_reservations.Models.Tenancy;
using table_reservations.Services.Tenancy;

namespace table_reservations.Tests;

public class OpeningHoursTests
{
    [Fact]
    public void DisabledVeilSchedulePublishesOvernightHoursWithoutBookingSlots()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tenant-settings.json"))
            .Build();
        var options = configuration.GetSection(OrganizationsOptions.SectionName).Get<OrganizationsOptions>()!;
        var registry = new OrganizationRegistry(Options.Create(options));
        Assert.True(registry.TryGetById("secret-veil", out var veil));
        veil.BookingEnabled = false;
        var config = PublicTenantConfigResponse.FromOrganization(veil);

        Assert.False(config.BookingEnabled);
        Assert.Empty(config.BookingTime.AvailableTimeSlots);
        Assert.Empty(config.BookingTime.StartTime);
        Assert.Empty(config.BookingTime.EndTime);
        Assert.Equal(7, config.OpeningHours.Length);
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var hours = Assert.Single(config.OpeningHours, hours => hours.Day == (int)day);
            Assert.Equal("12:00", hours.OpenTime);
            Assert.Equal(day is DayOfWeek.Friday or DayOfWeek.Saturday ? "03:00" : "02:00", hours.CloseTime);
            Assert.True(hours.ClosesNextDay);
        }
        Assert.All(registry.All.Where(org => org.Id != "secret-veil"), org => Assert.Empty(org.OpeningHours));
    }

    [Theory]
    [InlineData("24:00", "02:00", 1)]
    [InlineData("12:00", "3:00", 1)]
    [InlineData("12:00", "03:60", 1)]
    [InlineData("12:00", "02:00", 7)]
    public void InvalidWeekdayOrTimeIsRejectedAtStartup(string open, string close, int day)
    {
        var org = new OrganizationOptions
        {
            Id = "invalid-hours",
            OpeningHours = [new() { Day = (DayOfWeek)day, OpenTime = open, CloseTime = close }]
        };
        var error = Assert.Throws<InvalidOperationException>(() => Registry(org));
        Assert.Contains("opening hours", error.Message);
        Assert.Contains(org.Id, error.Message);
    }

    [Fact]
    public void DuplicateOpeningWeekdaysAreRejected()
    {
        var monday = new OpeningHoursOptions { Day = DayOfWeek.Monday, OpenTime = "12:00", CloseTime = "02:00" };
        Assert.Throws<InvalidOperationException>(() => Registry(new OrganizationOptions
        {
            Id = "duplicate-hours", OpeningHours = [monday, monday]
        }));
    }

    [Fact]
    public void DaytimeHoursDoNotClaimClosingIsTomorrow()
    {
        var org = new OrganizationOptions
        {
            Id = "daytime",
            OpeningHours = [new() { Day = DayOfWeek.Sunday, OpenTime = "08:00", CloseTime = "20:00" }]
        };
        Registry(org);
        Assert.False(Assert.Single(PublicTenantConfigResponse.FromOrganization(org).OpeningHours).ClosesNextDay);
    }

    private static OrganizationRegistry Registry(OrganizationOptions org) =>
        new(Options.Create(new OrganizationsOptions { Items = [org] }));
}
