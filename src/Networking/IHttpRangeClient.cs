namespace pbl4.Networking;

public interface IHttpRangeClient
{
    Task<HttpRangeProbeResult> ProbeAsync(Uri url, CancellationToken cancellationToken = default);

    Task<HttpRangeResponse> DownloadRangeAsync(
        Uri url,
        long start,
        long end,
        Stream destination,
        CancellationToken cancellationToken = default);
}

