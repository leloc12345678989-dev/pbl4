using pbl4.Core;
using Xunit;

namespace pbl4.Core.Tests;

public sealed class DownloadJobTests
{
    [Fact]
    public void TransitionTo_FromPendingToRunning_IsAllowed()
    {
        var job = CreateJob();

        job.TransitionTo(DownloadState.Running);

        Assert.Equal(DownloadState.Running, job.State);
    }

    [Fact]
    public void TransitionTo_FromCompletedToRunning_Throws()
    {
        var job = CreateJob();
        job.TransitionTo(DownloadState.Running);
        job.TransitionTo(DownloadState.Completed);

        Assert.Throws<InvalidOperationException>(() => job.TransitionTo(DownloadState.Running));
    }

    [Fact]
    public void ProgressPercent_UsesSegmentDownloadedBytes()
    {
        var job = CreateJob();
        job.Segments[0].AddProgress(25);

        Assert.Equal(25, job.DownloadedBytes);
        Assert.Equal(25, job.ProgressPercent);
    }

    private static DownloadJob CreateJob()
        => new(
            Guid.NewGuid(),
            new Uri("https://example.com/file.bin"),
            "file.bin",
            100,
            new[] { new DownloadSegment(0, 0, 99) });
}

