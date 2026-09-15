namespace pbl4.OSIntegration;

public sealed class SegmentPlanner
{
    public IReadOnlyList<PlannedSegment> Plan(long totalBytes, int requestedSegments)
    {
        if (totalBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes), "Kich thuoc file phai lon hon 0.");
        }

        if (requestedSegments <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSegments), "So segment phai lon hon 0.");
        }

        var segmentCount = (int)Math.Min(requestedSegments, totalBytes);
        var baseSize = totalBytes / segmentCount;
        var remainder = totalBytes % segmentCount;
        var segments = new List<PlannedSegment>(segmentCount);
        long cursor = 0;

        for (var index = 0; index < segmentCount; index++)
        {
            var length = baseSize + (index < remainder ? 1 : 0);
            var start = cursor;
            var end = cursor + length - 1;
            segments.Add(new PlannedSegment(index, start, end));
            cursor = end + 1;
        }

        return segments;
    }
}

