using table_reservations.Configuration;
using table_reservations.Models;
using table_reservations.Services;

namespace table_reservations.Tests;

public class BookingRulesTests
{
    private static CarWashCatalog Catalog() => new(
        [new("car", "Car")],
        new[] { "body_wash", "interior_polish", "tire_blackening", "complex_wash", "engine_wash", "paper_mat" }
            .Select(id => new CarWashService(id, id, id == "complex_wash", "car", 1000, null)).ToArray(),
        [new("complex_wash", "body_wash"), new("complex_wash", "interior_polish"),
         new("complex_wash", "tire_blackening"), new("complex_wash", "paper_mat")]);

    [Theory]
    [InlineData("body_wash,interior_polish", 50)]
    [InlineData("body_wash,interior_polish,tire_blackening", 60)]
    [InlineData("complex_wash", 90)]
    [InlineData("engine_wash", 60)]
    [InlineData("complex_wash,engine_wash", 150)]
    public void UsesSessionDurationRules(string ids, int minutes) =>
        Assert.Equal(minutes, BookingRules.Quote(Catalog(), "car", ids.Split(','), 60).DurationMinutes);

    [Fact]
    public void PackageDoesNotChargeIncludedServicesTwice()
    {
        var quote = BookingRules.Quote(Catalog(), "car", ["body_wash", "complex_wash", "interior_polish", "paper_mat"], 60);
        Assert.Equal("complex_wash", Assert.Single(quote.Services).Id);
        Assert.Equal(1000, quote.TotalKzt);
        Assert.Equal(90, quote.DurationMinutes);
    }

    [Theory]
    [InlineData("", "body_wash")]
    [InlineData("car", "unknown")]
    [InlineData("car", "body_wash,body_wash")]
    [InlineData("car", "")]
    public void RejectsInvalidSelection(string category, string ids) =>
        Assert.Throws<BookingException>(() => BookingRules.Quote(Catalog(), category, ids.Split(','), 60));

    [Fact]
    public void UnconfiguredDurationFailsClosedWhenDefaultDisabled() =>
        Assert.Equal("DURATION_NOT_CONFIGURED",
            Assert.Throws<BookingException>(() => BookingRules.Quote(Catalog(), "car", ["engine_wash"], null)).Code);

    [Fact]
    public void ConfiguredIndividualDurationsAreAdded()
    {
        var catalog = Catalog();
        catalog = catalog with { Services = catalog.Services.Select(s => s with { DurationMinutes = 25 }).ToArray() };
        Assert.Equal(50, BookingRules.Quote(catalog, "car", ["engine_wash", "paper_mat"], null).DurationMinutes);
    }

    [Fact]
    public void FullVisitMustFitBeforeClosing()
    {
        var hours = new BookingTimeOptions { StartTime = "08:00", EndTime = "20:00", SlotDurationMinutes = 60 };
        var date = new DateTime(2026, 9, 7, 19, 0, 0);
        Assert.True(BookingRules.FitsHours(date, 60, hours));
        Assert.False(BookingRules.FitsHours(date, 90, hours));
        Assert.False(BookingRules.FitsHours(date.Date.AddHours(7), 60, hours));
    }

    [Fact]
    public void IntervalOverlapIncludesPartialOverlapButNotAdjacentVisits()
    {
        var start = new DateTime(2026, 9, 7, 10, 0, 0);
        Assert.True(BookingRules.Overlaps(start, start.AddMinutes(90), start.AddHours(1), start.AddHours(2)));
        Assert.False(BookingRules.Overlaps(start, start.AddMinutes(60), start.AddHours(1), start.AddHours(2)));
    }

    [Fact]
    public void DateStorageRoundTripsKazakhstanMidnight()
    {
        var date = new DateTime(2026, 9, 7, 0, 0, 0);
        Assert.Equal("2026-09-06 19:00:00", BookingRules.Store(date));
        Assert.Equal(date, BookingRules.Read(BookingRules.Store(date)));
    }

    [Fact]
    public void BookingHorizonAndLeadTimeAreEnforced()
    {
        var now = new DateTime(2026, 9, 7, 8, 56, 0);
        var hours = new BookingTimeOptions { StartTime = "08:00", EndTime = "20:00", SlotDurationMinutes = 60 };
        Assert.DoesNotContain(now.Date.AddHours(9), BookingRules.Slots(DateOnly.FromDateTime(now), hours, now));
        Assert.Throws<BookingException>(() => BookingRules.Slots(DateOnly.FromDateTime(now).AddDays(7), hours, now));
    }
}
