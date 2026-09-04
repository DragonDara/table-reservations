using table_reservations.Constants;

namespace table_reservations.Tests.Constants;

public class RestaurantSlotScheduleTests
{
    [Fact]
    public void GetCandidateSlots_ReturnsHalfHourSlotsDuringOpeningHours()
    {
        var now = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Unspecified);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(new DateOnly(2026, 9, 4), now);

        Assert.Equal(new DateTime(2026, 9, 4, 12, 0, 0), slots.First());
        Assert.Equal(new DateTime(2026, 9, 4, 23, 30, 0), slots.Last());
        Assert.All(slots.Zip(slots.Skip(1)), pair =>
            Assert.Equal(TimeSpan.FromMinutes(30), pair.Second - pair.First));
    }

    [Fact]
    public void GetCandidateSlots_IncludesEarlyMorningForFutureCalendarDate()
    {
        var now = new DateTime(2026, 9, 4, 18, 0, 0, DateTimeKind.Unspecified);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(new DateOnly(2026, 9, 5), now);

        Assert.Contains(new DateTime(2026, 9, 5, 0, 0, 0), slots);
        Assert.Contains(new DateTime(2026, 9, 5, 3, 30, 0), slots);
        Assert.DoesNotContain(new DateTime(2026, 9, 5, 4, 0, 0), slots);
    }

    [Fact]
    public void GetCandidateSlots_EnforcesFiveMinuteLeadTime()
    {
        var now = new DateTime(2026, 9, 4, 12, 27, 0, DateTimeKind.Unspecified);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(new DateOnly(2026, 9, 4), now);

        Assert.DoesNotContain(new DateTime(2026, 9, 4, 12, 30, 0), slots);
        Assert.Equal(new DateTime(2026, 9, 4, 13, 0, 0), slots.First());
    }

    [Theory]
    [InlineData(2026, 9, 3, false)]
    [InlineData(2026, 9, 4, true)]
    [InlineData(2026, 9, 10, true)]
    [InlineData(2026, 9, 11, false)]
    public void IsBookableDate_UsesSevenCalendarDayWindow(int year, int month, int day, bool expected)
    {
        var now = new DateTime(2026, 9, 4, 23, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(expected, RestaurantSlotSchedule.IsBookableDate(new DateOnly(year, month, day), now));
    }

    [Fact]
    public void HasAvailableTable_ReturnsFalseWhenEveryTableConflicts()
    {
        var slot = new DateTime(2026, 9, 4, 18, 0, 0);
        IEnumerable<IEnumerable<DateTime>> reservations =
        [
            [new DateTime(2026, 9, 4, 17, 0, 0)],
            [new DateTime(2026, 9, 4, 20, 0, 0)],
        ];

        Assert.False(RestaurantSlotSchedule.HasAvailableTable(slot, reservations));
    }

    [Fact]
    public void HasAvailableTable_ReturnsTrueWhenAtLeastOneTableDoesNotConflict()
    {
        var slot = new DateTime(2026, 9, 4, 18, 0, 0);
        IEnumerable<IEnumerable<DateTime>> reservations =
        [
            [new DateTime(2026, 9, 4, 18, 30, 0)],
            [new DateTime(2026, 9, 4, 21, 0, 0)],
        ];

        Assert.True(RestaurantSlotSchedule.HasAvailableTable(slot, reservations));
    }
}
