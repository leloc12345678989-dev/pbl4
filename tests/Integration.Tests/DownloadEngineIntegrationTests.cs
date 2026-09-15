using pbl4.Core;
using pbl4.Integration.Tests.Fakes;
using pbl4.OSIntegration;
using Xunit;

namespace pbl4.Integration.Tests;

public sealed class DownloadEngineIntegrationTests
{
    [Fact]
    public async Task CreateAsync_WhenServerSupportsRanges_CreatesMultipleSegments()
    {
        var engine = CreateEngine(new byte[16], out _, out var repository);

        var job = await engine.CreateAsync(new CreateDownloadRequest(
            new Uri("https://example.test/file.bin"),
            CreateTempOutputPath(),
            SegmentCount: 4));

        var saved = await repository.FindAsync(job.Id);

        Assert.NotNull(saved);
        Assert.Equal(4, saved.Segments.Count);
        Assert.All(saved.Segments, segment => Assert.Equal(4, segment.Length));
    }

    [Fact]
    public async Task CreateAsync_WhenServerDoesNotSupportRanges_FallsBackToOneSegment()
    {
        var engine = CreateEngine(new byte[16], out _, out _, supportsRanges: false);

        var job = await engine.CreateAsync(new CreateDownloadRequest(
            new Uri("https://example.test/file.bin"),
            CreateTempOutputPath(),
            SegmentCount: 8));

        Assert.Single(job.Segments);
        Assert.Equal(16, job.Segments[0].Length);
    }

    [Fact]
    public async Task StartAsync_DownloadsAllSegmentsAndMovesPartFileToFinalPath()
    {
        var content = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var engine = CreateEngine(content, out var http, out _);
        var outputPath = CreateTempOutputPath();
        var job = await engine.CreateAsync(new CreateDownloadRequest(
            new Uri("https://example.test/file.bin"),
            outputPath,
            SegmentCount: 4));

        await engine.StartAsync(job.Id);

        var downloaded = await File.ReadAllBytesAsync(outputPath);
        TryDelete(outputPath);

        Assert.Equal(content, downloaded);
        Assert.Equal(4, http.RequestedRanges.Count);
    }

    [Fact]
    public async Task StartAsync_WithExistingSegmentProgress_RequestsOnlyRemainingBytes()
    {
        var content = Enumerable.Range(0, 12).Select(i => (byte)(i + 1)).ToArray();
        var http = new FakeHttpRangeClient(content);
        var repository = new InMemoryDownloadRepository();
        var outputPath = CreateTempOutputPath();
        var job = new DownloadJob(
            Guid.NewGuid(),
            new Uri("https://example.test/file.bin"),
            outputPath,
            12,
            new[]
            {
                new DownloadSegment(0, 0, 5, downloadedBytes: 6, SegmentState.Completed),
                new DownloadSegment(1, 6, 11, downloadedBytes: 2, SegmentState.Pending)
            },
            DownloadState.Pending,
            requestedConcurrency: 2);
        await repository.SaveAsync(job);
        var engine = new DownloadEngine(http, new SegmentPlanner(), repository, new ProgressReporter());

        await engine.StartAsync(job.Id);
        TryDelete(outputPath);

        Assert.Contains(http.RequestedRanges, range => range.Start == 8 && range.End == 11);
        Assert.DoesNotContain(http.RequestedRanges, range => range.Start == 0);
    }

    private static DownloadEngine CreateEngine(
        byte[] content,
        out FakeHttpRangeClient http,
        out InMemoryDownloadRepository repository,
        bool supportsRanges = true)
    {
        http = new FakeHttpRangeClient(content, supportsRanges);
        repository = new InMemoryDownloadRepository();
        return new DownloadEngine(
            http,
            new SegmentPlanner(),
            repository,
            new ProgressReporter(),
            new DownloadEngineOptions { MaxConcurrency = 8, BufferSize = 4 });
    }

    private static string CreateTempOutputPath()
        => Path.Combine(Path.GetTempPath(), $"pbl4-it-{Guid.NewGuid():N}.bin");

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var part = path + ".part";
        if (File.Exists(part))
        {
            File.Delete(part);
        }
    }
}

