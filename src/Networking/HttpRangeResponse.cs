using System.Net;

namespace pbl4.Networking;

public sealed record HttpRangeResponse(
    HttpStatusCode StatusCode,
    long RequestedStart,
    long RequestedEnd,
    long? ActualStart,
    long? ActualEnd,
    long? TotalLength,
    long BytesRead,
    bool IsPartialContent);

