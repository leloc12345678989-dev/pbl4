namespace pbl4.Networking;

public sealed record TcpProbeResult(
    string Host,
    int Port,
    bool Connected,
    TimeSpan RoundTripTime,
    string? Error);

