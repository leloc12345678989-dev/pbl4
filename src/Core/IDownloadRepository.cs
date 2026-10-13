namespace pbl4.Core;

public interface IDownloadRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(DownloadJob job, CancellationToken cancellationToken = default);

    Task<DownloadJob?> FindAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken = default);

    Task UpdateJobStateAsync(Guid jobId, DownloadState state, CancellationToken cancellationToken = default);

    Task UpdateSegmentAsync(Guid jobId, DownloadSegment segment, CancellationToken cancellationToken = default);
}
