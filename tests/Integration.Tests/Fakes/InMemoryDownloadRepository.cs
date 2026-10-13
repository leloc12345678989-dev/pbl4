using System.Collections.Concurrent;
using pbl4.Core;

namespace pbl4.Integration.Tests.Fakes;

internal sealed class InMemoryDownloadRepository : IDownloadRepository
{
    private readonly ConcurrentDictionary<Guid, DownloadJob> _jobs = new();

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SaveAsync(DownloadJob job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = Clone(job);
        return Task.CompletedTask;
    }

    public Task<DownloadJob?> FindAsync(Guid jobId, CancellationToken cancellationToken = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? Clone(job) : null);

    public Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DownloadJob>>(_jobs.Values.Select(Clone).ToList());

    public Task UpdateJobStateAsync(Guid jobId, DownloadState state, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job) && job.State != state)
        {
            job.TransitionTo(state);
        }

        return Task.CompletedTask;
    }

    public Task UpdateSegmentAsync(Guid jobId, DownloadSegment segment, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return Task.CompletedTask;
        }

        var existing = job.Segments.First(item => item.Index == segment.Index);
        if (existing.State != SegmentState.Running && segment.State == SegmentState.Running)
        {
            existing.MarkRunning();
        }

        var delta = segment.DownloadedBytes - existing.DownloadedBytes;
        if (delta > 0)
        {
            existing.AddProgress(delta);
        }

        if (segment.State == SegmentState.Completed)
        {
            existing.MarkCompleted();
        }

        if (segment.State == SegmentState.Failed)
        {
            existing.MarkFailed();
        }

        return Task.CompletedTask;
    }

    private static DownloadJob Clone(DownloadJob job)
        => new(
            job.Id,
            job.Url,
            job.OutputPath,
            job.TotalBytes,
            job.Segments.Select(segment => new DownloadSegment(
                segment.Index,
                segment.Start,
                segment.End,
                segment.DownloadedBytes,
                segment.State)).ToList(),
            job.State,
            job.RequestedConcurrency,
            job.CreatedAt,
            job.UpdatedAt);
}
