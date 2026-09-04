using table_reservations.Configuration;
using table_reservations.Services;

namespace table_reservations.Tests;

public class BookingTimeScheduleTests
{
    [Fact]
    public void GetAvailableSlots_RegularWindow_ReturnsStartInclusiveEndExclusiveSlots()
    {
        var options = new BookingTimeOptions
        {
            StartTime = "08:00",
            EndTime = "12:00",
            SlotDurationMinutes = 60
        };

        var slots = BookingTimeSchedule.GetAvailableSlots(options);

        Assert.Equal(new[] { "08:00", "09:00", "10:00", "11:00" }, slots);
    }

    [Fact]
    public void GetAvailableSlots_OvernightWindow_WrapsAcrossMidnight()
    {
        var options = new BookingTimeOptions
        {
            StartTime = "22:00",
            EndTime = "02:00",
            SlotDurationMinutes = 60
        };

        var slots = BookingTimeSchedule.GetAvailableSlots(options);

        Assert.Equal(new[] { "22:00", "23:00", "00:00", "01:00" }, slots);
    }

    [Fact]
    public void GetAvailableSlots_UsesConfiguredDuration()
    {
        var options = new BookingTimeOptions
        {
            StartTime = "08:00",
            EndTime = "10:00",
            SlotDurationMinutes = 30
        };

        var slots = BookingTimeSchedule.GetAvailableSlots(options);

        Assert.Equal(new[] { "08:00", "08:30", "09:00", "09:30" }, slots);
    }

    [Fact]
    public void GetAvailableSlots_ReservationDeadlineStopsSlotsBeforeClosingTime()
    {
        var options = new BookingTimeOptions
        {
            StartTime = "12:00",
            EndTime = "20:00",
            ReservationDeadline = "18:00",
            SlotDurationMinutes = 60
        };

        var slots = BookingTimeSchedule.GetAvailableSlots(options);

        Assert.Equal(new[] { "12:00", "13:00", "14:00", "15:00", "16:00", "17:00" }, slots);
    }

    [Fact]
    public void GetAvailableSlots_OvernightDeadlineStopsBeforeClosingTime()
    {
        var options = new BookingTimeOptions
        {
            StartTime = "12:00",
            EndTime = "04:00",
            ReservationDeadline = "02:00",
            SlotDurationMinutes = 60
        };

        var slots = BookingTimeSchedule.GetAvailableSlots(options);

        Assert.Equal("01:00", slots[^1]);
        Assert.DoesNotContain("02:00", slots);
        Assert.DoesNotContain("03:00", slots);
    }

    [Theory]
    [InlineData(8, 0, true)]
    [InlineData(9, 0, true)]
    [InlineData(9, 30, false)]
    [InlineData(12, 0, false)]
    public void IsAvailable_AcceptsOnlyGeneratedSlots(int hour, int minute, bool expected)
    {
        var options = new BookingTimeOptions
        {
            StartTime = "08:00",
            EndTime = "12:00",
            SlotDurationMinutes = 60
        };
        var scheduledAt = new DateTime(2026, 9, 3, hour, minute, 0);

        Assert.Equal(expected, BookingTimeSchedule.IsAvailable(options, scheduledAt));
    }

    [Theory]
    [InlineData("8:00", "12:00", 60)]
    [InlineData("08:00", "noon", 60)]
    [InlineData("08:00", "12:00", 0)]
    public void GetAvailableSlots_InvalidConfiguration_Throws(
        string startTime,
        string endTime,
        int slotDurationMinutes)
    {
        var options = new BookingTimeOptions
        {
            StartTime = startTime,
            EndTime = endTime,
            SlotDurationMinutes = slotDurationMinutes
        };

        Assert.Throws<InvalidOperationException>(() => BookingTimeSchedule.GetAvailableSlots(options));
    }

    [Theory]
    [InlineData("08:00", "20:00", "not-a-time")]
    [InlineData("08:00", "20:00", "21:00")]
    [InlineData("12:00", "04:00", "05:00")]
    public void GetAvailableSlots_InvalidReservationDeadline_Throws(
        string startTime,
        string endTime,
        string reservationDeadline)
    {
        var options = new BookingTimeOptions
        {
            StartTime = startTime,
            EndTime = endTime,
            ReservationDeadline = reservationDeadline,
            SlotDurationMinutes = 60
        };

        Assert.Throws<InvalidOperationException>(() => BookingTimeSchedule.GetAvailableSlots(options));
    }
}
