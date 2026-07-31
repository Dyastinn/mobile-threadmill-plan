using MyHi.Companion.Core.Formatting;

namespace MyHi.Companion.Tests.Formatting;

public class HexHelpersTests
{
    [Theory]
    [InlineData(new byte[] { }, "")]
    [InlineData(new byte[] { 0x02 }, "02")]
    [InlineData(new byte[] { 0x02, 0x00 }, "02 00")]
    [InlineData(new byte[] { 0x64, 0x00, 0x40, 0x06, 0x0A, 0x00 }, "64 00 40 06 0A 00")]
    public void ToHex_preserves_leading_zeros_uppercase_space_separated(byte[] input, string expected)
    {
        Assert.Equal(expected, HexHelpers.ToHex(input));
    }

    [Theory]
    [InlineData("02 00", new byte[] { 0x02, 0x00 })]
    [InlineData("0200", new byte[] { 0x02, 0x00 })]
    [InlineData("  02   00  ", new byte[] { 0x02, 0x00 })]
    [InlineData("02\t00\n", new byte[] { 0x02, 0x00 })]
    [InlineData("", new byte[] { })]
    [InlineData("   ", new byte[] { })]
    [InlineData("80 02 01", new byte[] { 0x80, 0x02, 0x01 })]
    public void FromHex_accepts_whitespace_variants(string input, byte[] expected)
    {
        Assert.Equal(expected, HexHelpers.FromHex(input));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("020")]
    [InlineData("2 0 0")]
    public void FromHex_rejects_odd_length(string input)
    {
        Assert.Throws<FormatException>(() => HexHelpers.FromHex(input));
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("0G")]
    public void FromHex_rejects_non_hex_digits(string input)
    {
        Assert.Throws<FormatException>(() => HexHelpers.FromHex(input));
    }

    [Fact]
    public void TryFromHex_returns_false_instead_of_throwing()
    {
        Assert.False(HexHelpers.TryFromHex("ZZ", out var result));
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x02, 0x8A, 0x02 })]
    [InlineData(new byte[] { 0x64, 0x00, 0x40, 0x06, 0x0A, 0x00 })]
    public void RoundTrip_bytes_through_hex_and_back(byte[] original)
    {
        var hex = HexHelpers.ToHex(original);
        var roundTripped = HexHelpers.FromHex(hex);
        Assert.Equal(original, roundTripped);
    }
}
