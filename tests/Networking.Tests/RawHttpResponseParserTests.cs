using pbl4.Networking;
using Xunit;

namespace pbl4.Networking.Tests;

public sealed class RawHttpResponseParserTests
{
    [Fact]
    public void Parse_WithPartialContentResponse_ReturnsHeaders()
    {
        const string raw =
            "HTTP/1.1 206 Partial Content\r\n" +
            "Content-Range: bytes 0-1023/4096\r\n" +
            "Content-Length: 1024\r\n" +
            "\r\n" +
            "body";

        var response = RawHttpResponseParser.Parse(raw);

        Assert.Equal("HTTP/1.1", response.HttpVersion);
        Assert.Equal(206, response.StatusCode);
        Assert.Equal("Partial Content", response.ReasonPhrase);
        Assert.Equal("bytes 0-1023/4096", response.Headers["Content-Range"]);
        Assert.Equal("body", response.BodyPreview);
    }

    [Fact]
    public void Parse_WithInvalidHeader_ThrowsFormatException()
    {
        const string raw =
            "HTTP/1.1 200 OK\r\n" +
            "BrokenHeader\r\n" +
            "\r\n";

        Assert.Throws<FormatException>(() => RawHttpResponseParser.Parse(raw));
    }
}
