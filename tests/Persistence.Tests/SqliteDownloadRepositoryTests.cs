using pbl4.Core;
using pbl4.Persistence;
using Xunit;

namespace pbl4.Persistence.Tests;

public sealed class SqliteDownloadRepositoryTests
{
    [Fact]
    public async Task SaveAndFindAsync_RoundTripsJobAndSegments()
    {
        var path = CreateTempDbPath();
        var repository = new SqliteDownloadRepository(path);
        await repository.InitializeAsync();
        var job = CreateJob();
        job.Segments[0].AddProgress(25);

        await repository.SaveAsync(job);
        var loaded = await repository.FindAsync(job.Id);

        File.Delete(path);
        Assert.NotNull(loaded);
        Assert.Equal(job.Id, loaded.Id);
        Assert.Equal(job.CreatedAt, loaded.CreatedAt);
        Assert.Equal(25, loaded.Segments[0].DownloadedBytes);
        Assert.Equal(2, loaded.Segments.Count);
    }

    [Fact]
    public async Task UpdateSegmentAsync_PersistsResumeProgress()
    {
        var path = CreateTempDbPath();
        var repository = new SqliteDownloadRepository(path);
        await repository.InitializeAsync();
        var job = CreateJob();
        await repository.SaveAsync(job);

        job.Segments[1].AddProgress(50);
        await repository.UpdateSegmentAsync(job.Id, job.Segments[1]);
        var loaded = await repository.FindAsync(job.Id);

        File.Delete(path);
        Assert.NotNull(loaded);
        Assert.Equal(50, loaded.Segments[1].DownloadedBytes);
        Assert.Equal(50, loaded.Segments[1].RemainingBytes);
    }

    [Fact]
    public async Task UpdateJobStateAsync_PersistsPauseState()
    {
        var path = CreateTempDbPath();
        var repository = new SqliteDownloadRepository(path);
        await repository.InitializeAsync();
        var job = CreateJob();
        await repository.SaveAsync(job);

        await repository.UpdateJobStateAsync(job.Id, DownloadState.Paused);
        var loaded = await repository.FindAsync(job.Id);

        File.Delete(path);
        Assert.NotNull(loaded);
        Assert.Equal(DownloadState.Paused, loaded.State);
    }

    private static DownloadJob CreateJob()
        => new(
            Guid.NewGuid(),
            new Uri("https://example.com/video.iso"),
            Path.Combine(Path.GetTempPath(), "video.iso"),
            200,
            new[]
            {
                new DownloadSegment(0, 0, 99),
                new DownloadSegment(1, 100, 199)
            },
            DownloadState.Pending,
            requestedConcurrency: 2);

    private static string CreateTempDbPath()
        => Path.Combine(Path.GetTempPath(), $"pbl4-{Guid.NewGuid():N}.db");
}
