namespace pbl4.OSIntegration;

public sealed record PlannedSegment(int Index, long Start, long End)
{
    public long Length => End - Start + 1;
}

