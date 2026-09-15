namespace pbl4.Core;

public sealed record CreateDownloadRequest(
    Uri Url,
    string OutputPath,
    int SegmentCount = 4,
    long? BandwidthLimitBytesPerSecond = null);

