namespace pbl4.Core;

public sealed record DownloadProgressSnapshot(
    Guid JobId,
    DownloadState State,
    long DownloadedBytes,
    long TotalBytes,
    double ProgressPercent,
    double SpeedBytesPerSecond,
    int ActiveSegments,
    IReadOnlyList<SegmentProgressSnapshot> Segments,
    string Message,
    DateTimeOffset Timestamp);

public sealed record SegmentProgressSnapshot(
    int Index,
    long DownloadedBytes,
    long TotalBytes,
    double ProgressPercent,
    SegmentState State);
