using Hopon.Api.Services;
using Xunit;

namespace Hopon.Api.Tests;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("0781234567", "+27781234567")]
    [InlineData("078 123 4567", "+27781234567")]
    [InlineData("+27 78 123 4567", "+27781234567")]
    [InlineData("27781234567", "+27781234567")]
    [InlineData("0027781234567", "+27781234567")]
    [InlineData("(078) 123-4567", "+27781234567")]
    [InlineData("+27781234567", "+27781234567")]
    [InlineData(" 061 234 5678 ", "+27612345678")]
    [InlineData("0821234567", "+27821234567")]
    public void Common_mobile_formats_normalize_to_e164(string input, string expected)
    {
        Assert.True(PhoneNumberNormalizer.TryNormalizeSouthAfrican(input, out var result));
        Assert.Equal(expected, result);
        Assert.True(PhoneNumberNormalizer.TryNormalizeSouthAfrican(result, out var repeated));
        Assert.Equal(result, repeated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("078abc4567")]
    [InlineData("078123456")]
    [InlineData("07812345678")]
    [InlineData("+2778123456")]
    [InlineData("+277812345678")]
    [InlineData("0111234567")]
    [InlineData("0211234567")]
    [InlineData("0311234567")]
    [InlineData("0411234567")]
    [InlineData("0511234567")]
    [InlineData("0911234567")]
    [InlineData("0011234567")]
    [InlineData("+267781234567")]
    [InlineData("+270781234567")]
    [InlineData("781234567")]
    [InlineData("++27781234567")]
    [InlineData("078+234567")]
    [InlineData("+27781234567 ext 1")]
    [InlineData("078.123.4567")]
    [InlineData("０７８１２３４５６７")]
    [InlineData("078\n1234567")]
    public void Invalid_numbers_are_rejected(string? input)
    {
        Assert.False(PhoneNumberNormalizer.TryNormalizeSouthAfrican(input, out var result));
        Assert.Equal(string.Empty, result);
    }
}
