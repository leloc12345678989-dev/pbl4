namespace pbl4.Networking;

public static class RawHttpResponseParser
{
    public static RawHttpResponse Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            throw new FormatException("Raw HTTP response rong.");
        }

        var headerEnd = rawResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var separatorLength = 4;
        if (headerEnd < 0)
        {
            headerEnd = rawResponse.IndexOf("\n\n", StringComparison.Ordinal);
            separatorLength = 2;
        }

        var headerText = headerEnd >= 0 ? rawResponse[..headerEnd] : rawResponse;
        var body = headerEnd >= 0 ? rawResponse[(headerEnd + separatorLength)..] : string.Empty;
        var lines = headerText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        if (lines.Length == 0)
        {
            throw new FormatException("HTTP response khong co status line.");
        }

        var statusParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out var statusCode))
        {
            throw new FormatException($"Status line khong hop le: '{lines[0]}'.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                throw new FormatException($"Header line khong hop le: '{line}'.");
            }

            var name = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();
            headers[name] = value;
        }

        return new RawHttpResponse(
            statusParts[0],
            statusCode,
            statusParts.Length == 3 ? statusParts[2] : string.Empty,
            headers,
            body.Length > 512 ? body[..512] : body);
    }
}

