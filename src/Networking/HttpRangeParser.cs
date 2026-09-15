using System.Globalization;

namespace pbl4.Networking;

public static class HttpRangeParser
{
    public static ParsedContentRange ParseContentRange(string value)
    {
        if (!TryParseContentRange(value, out var range))
        {
            throw new FormatException($"Content-Range khong hop le: '{value}'.");
        }

        return range;
    }

    public static bool TryParseContentRange(string? value, out ParsedContentRange range)
    {
        range = default!;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = trimmed["bytes ".Length..].Trim();
        var slashIndex = spec.IndexOf('/');
        if (slashIndex <= 0 || slashIndex == spec.Length - 1)
        {
            return false;
        }

        var rangePart = spec[..slashIndex];
        var totalPart = spec[(slashIndex + 1)..];
        var dashIndex = rangePart.IndexOf('-');
        if (dashIndex <= 0 || dashIndex == rangePart.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(rangePart[..dashIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var start))
        {
            return false;
        }

        if (!long.TryParse(rangePart[(dashIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var end))
        {
            return false;
        }

        long? totalLength = null;
        if (totalPart != "*")
        {
            if (!long.TryParse(totalPart, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTotal))
            {
                return false;
            }

            totalLength = parsedTotal;
        }

        if (start < 0 || end < start)
        {
            return false;
        }

        if (totalLength is not null && end >= totalLength.Value)
        {
            return false;
        }

        range = new ParsedContentRange(start, end, totalLength);
        return true;
    }

    public static (long Start, long End) ParseRangeHeaderValue(string value)
    {
        if (!TryParseRangeHeaderValue(value, out var start, out var end))
        {
            throw new FormatException($"Range header khong hop le: '{value}'.");
        }

        return (start, end);
    }

    public static bool TryParseRangeHeaderValue(string? value, out long start, out long end)
    {
        start = 0;
        end = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = trimmed["bytes=".Length..].Trim();
        if (spec.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        var dashIndex = spec.IndexOf('-');
        if (dashIndex <= 0 || dashIndex == spec.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(spec[..dashIndex], NumberStyles.None, CultureInfo.InvariantCulture, out start))
        {
            return false;
        }

        if (!long.TryParse(spec[(dashIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out end))
        {
            return false;
        }

        return start >= 0 && end >= start;
    }
}

