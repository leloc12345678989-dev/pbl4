using System.Net;

namespace pbl4.Networking;

public sealed class HttpRangeRequestException : Exception
{
    public HttpRangeRequestException(
        HttpStatusCode statusCode,
        long start,
        long end,
        TimeSpan? retryAfter)
        : base(CreateMessage(statusCode, start, end))
    {
        StatusCode = statusCode;
        RequestedStart = start;
        RequestedEnd = end;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public long RequestedStart { get; }

    public long RequestedEnd { get; }

    public TimeSpan? RetryAfter { get; }

    private static string CreateMessage(HttpStatusCode statusCode, long start, long end)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return $"Server dang gioi han so request Range bytes={start}-{end} (429 Too Many Requests).";
        }

        return $"Server khong tra 206 Partial Content cho Range bytes={start}-{end}. Status: {(int)statusCode}.";
    }
}
