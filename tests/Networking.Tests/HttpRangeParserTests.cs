using pbl4.Networking;
using Xunit;

namespace pbl4.Networking.Tests;

public sealed class HttpRangeParserTests
{
    [Fact]
    public void ParseContentRange_WithValidRange_ReturnsOffsets()
    {
        var range = HttpRangeParser.ParseContentRange("bytes 0-1023/4096");

        Assert.Equal(0, range.Start);
        Assert.Equal(1023, range.End);
        Assert.Equal(4096, range.TotalLength);
        Assert.Equal(1024, range.Length);
    }

    [Fact]
    public void TryParseContentRange_WithUnknownTotal_ReturnsNullTotal()
    {
        var ok = HttpRangeParser.TryParseContentRange("bytes 100-199/*", out var range);

        Assert.True(ok);
        Assert.Equal(100, range.Start);
        Assert.Equal(199, range.End);
        Assert.Null(range.TotalLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("items 0-10/100")]
    [InlineData("bytes 10-0/100")]
    [InlineData("bytes 0-100/100")]
    [InlineData("bytes a-b/100")]
    public void TryParseContentRange_WithInvalidInput_ReturnsFalse(string input)
    {
        var ok = HttpRangeParser.TryParseContentRange(input, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ParseRangeHeaderValue_WithValidHeader_ReturnsOffsets()
    {
        var (start, end) = HttpRangeParser.ParseRangeHeaderValue("bytes=2048-4095");

        Assert.Equal(2048, start);
        Assert.Equal(4095, end);
    }

    [Fact]
    public void TryParseRangeHeaderValue_WithMultipleRanges_ReturnsFalse()
    {
        var ok = HttpRangeParser.TryParseRangeHeaderValue("bytes=0-10,20-30", out _, out _);

        Assert.False(ok);
    }
}
