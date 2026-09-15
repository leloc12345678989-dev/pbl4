namespace pbl4.Networking;

public sealed record UdpProbeResult(
    string Host,
    int Port,
    int Sent,
    int Received,
    double PacketLossPercent,
    TimeSpan? AverageRtt);

