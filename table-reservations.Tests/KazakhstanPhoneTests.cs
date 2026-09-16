using table_reservations.Helpers;
using table_reservations.Models;

namespace table_reservations.Tests;

public class KazakhstanPhoneTests
{
    [Theory]
    [InlineData("+77010000000")]
    [InlineData("+77001234567")]
    [InlineData("+77471234567")]
    [InlineData("+77751234567")]
    [InlineData("+77771234567")]
    [InlineData("8 (701) 000-00-00", "+77010000000")]
    [InlineData("7010000000", "+77010000000")]
    public void NormalizeAndValidate_AcceptsKazakhstanMobiles(string input, string? expected = null)
    {
        var phone = KazakhstanPhone.NormalizeAndValidate(input);
        Assert.Equal(expected ?? KazakhstanPhone.Normalize(input), phone);
        Assert.True(KazakhstanPhone.IsValidMobile(phone));
    }

    [Theory]
    [InlineData("+79001234567")] // Russia
    [InlineData("+77271234567")] // Almaty landline
    [InlineData("+77091234567")] // unknown DEF
    [InlineData("+7701000000")]  // too short
    [InlineData("+770100000000")] // too long
    [InlineData("")]
    [InlineData("abc")]
    public void NormalizeAndValidate_RejectsInvalidNumbers(string input)
    {
        var ex = Assert.Throws<BookingException>(() => KazakhstanPhone.NormalizeAndValidate(input));
        Assert.Equal(KazakhstanPhone.InvalidMessage, ex.Message);
    }
}
