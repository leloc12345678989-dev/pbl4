namespace pbl4.Core;

public sealed class DownloadSegment
{
    public DownloadSegment(int index, long start, long end, long downloadedBytes = 0, SegmentState state = SegmentState.Pending)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (start < 0 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Segment byte range khong hop le.");
        }

        Index = index;
        Start = start;
        End = end;
        DownloadedBytes = downloadedBytes;
        State = state;
    }

    public int Index { get; }

    public long Start { get; }

    public long End { get; }

    public long Length => End - Start + 1;

    public long DownloadedBytes { get; private set; }

    public SegmentState State { get; private set; }

    public long NextOffset => Start + DownloadedBytes;

    public long RemainingBytes => Math.Max(0, Length - DownloadedBytes);

    public void MarkRunning() => State = SegmentState.Running;

    public void MarkCompleted()
    {
        DownloadedBytes = Length;
        State = SegmentState.Completed;
    }

    public void MarkFailed() => State = SegmentState.Failed;

    public void AddProgress(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        DownloadedBytes = Math.Min(Length, DownloadedBytes + bytes);
        if (DownloadedBytes == Length)
        {
            State = SegmentState.Completed;
        }
    }
}

