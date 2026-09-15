namespace pbl4.Core;

public sealed class DownloadJob
{
    private readonly object _stateLock = new();
    private readonly List<DownloadSegment> _segments;

    public DownloadJob(
        Guid id,
        Uri url,
        string outputPath,
        long totalBytes,
        IReadOnlyList<DownloadSegment> segments,
        DownloadState state = DownloadState.Pending,
        int requestedConcurrency = 4)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Url = url;
        OutputPath = outputPath;
        TotalBytes = totalBytes;
        _segments = segments.ToList();
        State = state;
        RequestedConcurrency = requestedConcurrency;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }

    public Uri Url { get; }

    public string OutputPath { get; }

    public string PartPath => OutputPath + ".part";

    public long TotalBytes { get; }

    public int RequestedConcurrency { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DownloadState State { get; private set; }

    public IReadOnlyList<DownloadSegment> Segments => _segments;

    public long DownloadedBytes => _segments.Sum(segment => segment.DownloadedBytes);

    public double ProgressPercent => TotalBytes <= 0 ? 0 : DownloadedBytes * 100.0 / TotalBytes;

    public bool IsTerminal => State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled;

    public void TransitionTo(DownloadState next)
    {
        var lockTaken = false;
        try
        {
            // Critical section: state machine cua job co the bi API/UI goi pause/resume/cancel dong thoi.
            // Giu lock ngan va khong thuc hien network I/O trong lock de tranh deadlock.
            Monitor.TryEnter(_stateLock, TimeSpan.FromSeconds(3), ref lockTaken);
            if (!lockTaken)
            {
                throw new TimeoutException("Khong lay duoc JobStateLock.");
            }

            if (!CanTransition(State, next))
            {
                throw new InvalidOperationException($"Khong the chuyen job tu {State} sang {next}.");
            }

            State = next;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_stateLock);
            }
        }
    }

    private static bool CanTransition(DownloadState current, DownloadState next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            DownloadState.Pending => next is DownloadState.Running or DownloadState.Canceled,
            DownloadState.Running => next is DownloadState.Paused or DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled,
            DownloadState.Paused => next is DownloadState.Running or DownloadState.Canceled,
            DownloadState.Failed => next is DownloadState.Running or DownloadState.Canceled,
            DownloadState.Completed => false,
            DownloadState.Canceled => false,
            _ => false
        };
    }
}

