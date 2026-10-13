using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;

namespace pbl4.Networking;

public sealed class HttpRangeClient : IHttpRangeClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpRangeClientOptions _options;
    private readonly bool _ownsClient;

    public HttpRangeClient(HttpRangeClientOptions? options = null)
    {
        _options = options ?? new HttpRangeClientOptions();

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            Proxy = _options.Proxy,
            UseProxy = _options.Proxy is not null,
            MaxConnectionsPerServer = _options.MaxConnectionsPerServer,
            SslOptions =
            {
                // Minh hoa HTTPS/TLS: chi cho phep TLS 1.2/1.3, tranh cac protocol cu.
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = _options.Timeout
        };
        _ownsClient = true;
    }

    public HttpRangeClient(HttpClient httpClient, HttpRangeClientOptions? options = null)
    {
        _httpClient = httpClient;
        _options = options ?? new HttpRangeClientOptions();
        _ownsClient = false;
    }

    public async Task<HttpRangeProbeResult> ProbeAsync(Uri url, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Head, url);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var supportsRanges = HasAcceptRangesBytes(response);
        var contentLength = response.Content.Headers.ContentLength;

        if (supportsRanges && contentLength is > 0)
        {
            return ToProbeResult(url, supportsRanges, contentLength, response);
        }

        return await ProbeWithSingleByteRangeAsync(url, response, cancellationToken);
    }

    private async Task<HttpRangeProbeResult> ProbeWithSingleByteRangeAsync(
        Uri url,
        HttpResponseMessage headResponse,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        var supportsRanges = HasAcceptRangesBytes(headResponse) || HasAcceptRangesBytes(response);

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            supportsRanges = true;
            if (response.Content.Headers.ContentRange is not null &&
                HttpRangeParser.TryParseContentRange(response.Content.Headers.ContentRange.ToString(), out var parsedRange))
            {
                contentLength = parsedRange.TotalLength;
            }
        }

        return new HttpRangeProbeResult(
            url,
            supportsRanges,
            contentLength ?? headResponse.Content.Headers.ContentLength,
            response.Headers.ETag?.Tag ?? headResponse.Headers.ETag?.Tag,
            response.Content.Headers.LastModified ?? headResponse.Content.Headers.LastModified,
            response.Content.Headers.ContentType?.MediaType ?? headResponse.Content.Headers.ContentType?.MediaType,
            response.Version);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri url)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Version = _options.HttpVersion,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        request.Headers.UserAgent.ParseAdd("pbl4-download-accelerator/1.0");
        return request;
    }

    private static bool HasAcceptRangesBytes(HttpResponseMessage response)
        => response.Headers.AcceptRanges.Any(
            value => value.Equals("bytes", StringComparison.OrdinalIgnoreCase));

    private static HttpRangeProbeResult ToProbeResult(Uri url, bool supportsRanges, long? contentLength, HttpResponseMessage response)
        => new(
            url,
            supportsRanges,
            contentLength,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified,
            response.Content.Headers.ContentType?.MediaType,
            response.Version);

    public async Task<HttpRangeResponse> DownloadRangeAsync(
        Uri url,
        long start,
        long end,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "end phai >= start.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = _options.HttpVersion,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        // Day la minh hoa HTTP Range Request o tang ung dung:
        // client yeu cau server chi tra ve byte tu start den end.
        request.Headers.Range = new RangeHeaderValue(start, end);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRangeRequestException(
                response.StatusCode,
                start,
                end,
                response.Headers.RetryAfter?.Delta);
        }

        ParsedContentRange? parsedRange = null;
        if (response.Content.Headers.ContentRange is not null)
        {
            var headerValue = response.Content.Headers.ContentRange.ToString();
            if (HttpRangeParser.TryParseContentRange(headerValue, out var parsed))
            {
                parsedRange = parsed;
            }
        }

        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[_options.BufferSize];
        long bytesReadTotal = 0;

        while (true)
        {
            var read = await networkStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            bytesReadTotal += read;
        }

        return new HttpRangeResponse(
            response.StatusCode,
            start,
            end,
            parsedRange?.Start,
            parsedRange?.End,
            parsedRange?.TotalLength,
            bytesReadTotal,
            IsPartialContent: true);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
