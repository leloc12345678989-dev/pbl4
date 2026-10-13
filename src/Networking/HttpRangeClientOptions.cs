using System.Net;

namespace pbl4.Networking;

public sealed class HttpRangeClientOptions
{
    public IWebProxy? Proxy { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public Version HttpVersion { get; init; } = System.Net.HttpVersion.Version11;

    public int BufferSize { get; init; } = 1024 * 1024;

    public int MaxConnectionsPerServer { get; init; } = 8;
}
