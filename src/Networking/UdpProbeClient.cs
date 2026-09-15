using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace pbl4.Networking;

public sealed class UdpProbeClient
{
    public async Task<UdpProbeResult> ProbeEchoAsync(
        string host,
        int port,
        int packets,
        TimeSpan timeoutPerPacket,
        CancellationToken cancellationToken = default)
    {
        if (packets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packets), "So packet phai lon hon 0.");
        }

        using var udp = new UdpClient();
        udp.Connect(host, port);

        var received = 0;
        var rtts = new List<TimeSpan>();

        for (var i = 0; i < packets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = Encoding.UTF8.GetBytes($"pbl4-udp-probe:{i}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            var stopwatch = Stopwatch.StartNew();

            // UDP khong dam bao giao hang. Do do neu qua timeout ma khong co echo,
            // ta xem packet nay bi mat de minh hoa packet loss.
            await udp.SendAsync(payload, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutPerPacket);

            try
            {
                var result = await udp.ReceiveAsync(timeoutCts.Token);
                stopwatch.Stop();

                if (result.Buffer.Length > 0)
                {
                    received++;
                    rtts.Add(stopwatch.Elapsed);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
            }
        }

        var loss = (packets - received) * 100.0 / packets;
        var averageTicks = rtts.Count == 0 ? (long?)null : (long)rtts.Average(rtt => rtt.Ticks);

        return new UdpProbeResult(
            host,
            port,
            packets,
            received,
            loss,
            averageTicks is null ? null : TimeSpan.FromTicks(averageTicks.Value));
    }
}

