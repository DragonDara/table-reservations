using table_reservations.Constants;
using table_reservations.Configuration;
using table_reservations.Services;

namespace table_reservations.Tests.Constants;

public class RestaurantSlotScheduleTests
{
    private static BookingTimeOptions HalfHourSchedule => new()
    {
        StartTime = "12:00", EndTime = "04:00", SlotDurationMinutes = 30
    };

    [Fact]
    public void GetCandidateSlots_ReturnsHalfHourSlotsDuringOpeningHours()
    {
        var now = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Unspecified);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(new DateOnly(2026, 9, 4), now, HalfHourSchedule);

        Assert.Equal(new DateTime(2026, 9, 4, 12, 0, 0), slots.First());
        Assert.Equal(new DateTime(2026, 9, 5, 3, 30, 0), slots.Last());
        Assert.Equal(32, slots.Count);
        Assert.All(slots.Zip(slots.Skip(1)), pair =>
            Assert.Equal(TimeSpan.FromMinutes(30), pair.Second - pair.First));
    }

    [Fact]
    public void GetCandidateSlots_OvernightHoursBelongToFollowingCalendarDate()
    {
        var now = new DateTime(2026, 9, 4, 18, 0, 0, DateTimeKind.Unspecified);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(new DateOnly(2026, 9, 5), now, HalfHourSchedule);

        Assert.Equal(new DateTime(2026, 9, 5, 12, 0, 0), slots.First());
        Assert.Contains(new DateTime(2026, 9, 6, 0, 0, 0), slots);
        Assert.Contains(new DateTime(2026, 9, 6, 3, 30, 0), slots);
        Assert.DoesNotContain(new DateTime(2026, 9, 6, 4, 0, 0), slots);
    }

    [Fact]
    public void GetCandidateSlots_EnforcesFiveMinuteLeadTime()
    {
        var now = new DateTime(2026, 9, 4, 12, 27, 0, DateTimeKind.Unspecified);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(new DateOnly(2026, 9, 4), now, HalfHourSchedule);

        Assert.DoesNotContain(new DateTime(2026, 9, 4, 12, 30, 0), slots);
        Assert.Equal(new DateTime(2026, 9, 4, 13, 0, 0), slots.First());
    }

    [Theory]
    [InlineData("08:15", "12:00", null, 45, 5, 11, 15, 0)]
    [InlineData("12:00", "04:00", null, 60, 16, 3, 0, 1)]
    [InlineData("12:00", "04:00", "02:00", 60, 14, 1, 0, 1)]
    public void GetCandidateSlots_RespectsTenantHoursDurationAndDeadline(
        string start, string end, string? deadline, int interval,
        int count, int lastHour, int lastMinute, int lastDayOffset)
    {
        var options = new BookingTimeOptions
        {
            StartTime = start, EndTime = end,
            ReservationDeadline = deadline, SlotDurationMinutes = interval
        };
        var date = new DateOnly(2026, 12, 31);
        var now = new DateTime(2026, 12, 30, 18, 0, 0);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(date, now, options);

        Assert.Equal(count, slots.Count);
        Assert.Equal(date.AddDays(lastDayOffset).ToDateTime(new TimeOnly(lastHour, lastMinute)), slots.Last());
        Assert.All(slots, slot => Assert.True(BookingTimeSchedule.IsAvailable(options, slot)));
        Assert.All(slots, slot => Assert.Equal(DateTimeKind.Unspecified, slot.Kind));
    }

    [Fact]
    public void GetCandidateSlots_LateEveningKeepsFollowingMorningWithLeadTime()
    {
        var now = new DateTime(2026, 12, 31, 23, 56, 0);

        var slots = RestaurantSlotSchedule.GetCandidateSlots(new DateOnly(2026, 12, 31), now, HalfHourSchedule);

        Assert.Equal(new DateTime(2027, 1, 1, 0, 30, 0), slots.First());
        Assert.Equal(new DateTime(2027, 1, 1, 3, 30, 0), slots.Last());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void GetCandidateSlots_OutsideBookingWindowReturnsNoSlots(int dayOffset)
    {
        var now = new DateTime(2026, 9, 4, 10, 0, 0);
        var date = DateOnly.FromDateTime(now).AddDays(dayOffset);

        Assert.Empty(RestaurantSlotSchedule.GetCandidateSlots(date, now, HalfHourSchedule));
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
