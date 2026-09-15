using System.Diagnostics;
using System.Net.Sockets;

namespace pbl4.Networking;

public sealed class TcpProbeClient
{
    public async Task<TcpProbeResult> MeasureConnectRttAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // RTT o day la thoi gian bat tay TCP connect tu client den server.
            await client.ConnectAsync(host, port, timeoutCts.Token);
            stopwatch.Stop();
            return new TcpProbeResult(host, port, true, stopwatch.Elapsed, null);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            stopwatch.Stop();
            return new TcpProbeResult(host, port, false, stopwatch.Elapsed, ex.Message);
        }
    }
}

