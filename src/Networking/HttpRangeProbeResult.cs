namespace pbl4.Networking;

public sealed record HttpRangeProbeResult(
    Uri Url,
    bool SupportsRanges,
    long? ContentLength,
    string? ETag,
    DateTimeOffset? LastModified,
    string? ContentType,
    Version? HttpVersion);

