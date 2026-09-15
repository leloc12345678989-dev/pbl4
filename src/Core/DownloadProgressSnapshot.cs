namespace pbl4.Core;

public sealed record DownloadProgressSnapshot(
    Guid JobId,
    DownloadState State,
    long DownloadedBytes,
    long TotalBytes,
    double ProgressPercent,
    double SpeedBytesPerSecond,
    int ActiveSegments,
    string Message,
    DateTimeOffset Timestamp);

