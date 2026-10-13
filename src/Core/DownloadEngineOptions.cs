namespace pbl4.Core;

public sealed class DownloadEngineOptions
{
    public int DefaultConcurrency { get; init; } = 4;

    public int MaxConcurrency { get; init; } = 8;

    public int BufferSize { get; init; } = 1024 * 1024;

    public int MaxRetryAttempts { get; init; } = 8;

    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan SegmentIdleTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan SegmentLowSpeedWindow { get; init; } = TimeSpan.FromSeconds(30);

    public long MinimumSegmentBytesPerSecond { get; init; } = 8 * 1024;
}
