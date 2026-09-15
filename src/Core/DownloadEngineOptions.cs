namespace pbl4.Core;

public sealed class DownloadEngineOptions
{
    public int DefaultConcurrency { get; init; } = 4;

    public int MaxConcurrency { get; init; } = 8;

    public int BufferSize { get; init; } = 1024 * 1024;

    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}

