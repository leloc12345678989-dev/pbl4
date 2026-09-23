using System.Collections.Concurrent;
using System.Threading.Channels;
using pbl4.Networking;
using pbl4.OSIntegration;

namespace pbl4.Core;

public sealed class DownloadEngine
{
    private readonly IHttpRangeClient _httpClient;
    private readonly SegmentPlanner _segmentPlanner;
    private readonly IDownloadRepository _repository;
    private readonly ProgressReporter _progressReporter;
    private readonly DownloadEngineOptions _options;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningJobs = new();

    public DownloadEngine(
        IHttpRangeClient httpClient,
        SegmentPlanner segmentPlanner,
        IDownloadRepository repository,
        ProgressReporter progressReporter,
        DownloadEngineOptions? options = null)
    {
        _httpClient = httpClient;
        _segmentPlanner = segmentPlanner;
        _repository = repository;
        _progressReporter = progressReporter;
        _options = options ?? new DownloadEngineOptions();
    }

    public async Task<DownloadJob> CreateAsync(CreateDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var probe = await _httpClient.ProbeAsync(request.Url, cancellationToken);
        var totalBytes = probe.ContentLength ?? throw new InvalidOperationException("Server khong cung cap Content-Length.");
        var segmentCount = probe.SupportsRanges
            ? Math.Clamp(request.SegmentCount, 1, _options.MaxConcurrency)
            : 1;

        var plannedSegments = _segmentPlanner.Plan(totalBytes, segmentCount);
        var segments = plannedSegments
            .Select(segment => new DownloadSegment(segment.Index, segment.Start, segment.End))
            .ToList();

        var job = new DownloadJob(
            Guid.NewGuid(),
            request.Url,
            request.OutputPath,
            totalBytes,
            segments,
            DownloadState.Pending,
            segmentCount);

        await _repository.SaveAsync(job, cancellationToken);
        Publish(job, 0, "Da tao download job.");
        return job;
    }

    public async Task StartAsync(Guid jobId, long? bandwidthLimitBytesPerSecond = null, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobOrThrowAsync(jobId, cancellationToken);
        if (job.State != DownloadState.Running)
        {
            job.TransitionTo(DownloadState.Running);
            await _repository.UpdateJobStateAsync(job.Id, job.State, cancellationToken);
        }

        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_runningJobs.TryAdd(job.Id, jobCts))
        {
            return;
        }

        try
        {
            await RunJobAsync(job, bandwidthLimitBytesPerSecond, jobCts.Token);
        }
        catch (OperationCanceledException) when (job.State == DownloadState.Paused)
        {
            Publish(job, 0, "Job da tam dung an toan.");
        }
        catch (OperationCanceledException)
        {
            if (job.State != DownloadState.Canceled)
            {
                job.TransitionTo(DownloadState.Paused);
                await _repository.UpdateJobStateAsync(job.Id, job.State, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            if (!job.IsTerminal)
            {
                job.TransitionTo(DownloadState.Failed);
                await _repository.UpdateJobStateAsync(job.Id, job.State, CancellationToken.None);
                Publish(job, 0, $"Job failed: {ex.Message}");
            }
        }
        finally
        {
            _runningJobs.TryRemove(job.Id, out _);
            jobCts.Dispose();
        }
    }

    public async Task PauseAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobOrThrowAsync(jobId, cancellationToken);
        if (job.State != DownloadState.Running)
        {
            return;
        }

        job.TransitionTo(DownloadState.Paused);
        await _repository.UpdateJobStateAsync(job.Id, DownloadState.Paused, cancellationToken);

        if (_runningJobs.TryGetValue(jobId, out var cts))
        {
            await cts.CancelAsync();
        }

        Publish(job, 0, "Da gui tin hieu pause.");
    }

    public async Task ResumeAsync(Guid jobId, long? bandwidthLimitBytesPerSecond = null, CancellationToken cancellationToken = default)
    {
        await StartAsync(jobId, bandwidthLimitBytesPerSecond, cancellationToken);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobOrThrowAsync(jobId, cancellationToken);
        if (job.IsTerminal)
        {
            return;
        }

        job.TransitionTo(DownloadState.Canceled);
        await _repository.UpdateJobStateAsync(job.Id, DownloadState.Canceled, cancellationToken);

        if (_runningJobs.TryGetValue(jobId, out var cts))
        {
            await cts.CancelAsync();
        }

        Publish(job, 0, "Job da bi huy.");
    }

    public Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public Task<DownloadJob?> FindAsync(Guid jobId, CancellationToken cancellationToken = default)
        => _repository.FindAsync(jobId, cancellationToken);

    public ChannelReader<DownloadProgressSnapshot> Subscribe(Guid jobId)
        => _progressReporter.Subscribe(jobId);

    private async Task RunJobAsync(DownloadJob job, long? bandwidthLimitBytesPerSecond, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(job.OutputPath)) ?? ".");
        var speedMeter = new SpeedMeter();
        var isCompleted = false;

        await using (var writer = new SafeFileWriter(job.PartPath))
        {
            await writer.InitializeAsync(job.TotalBytes, cancellationToken);

            await using var threadPool = new ThreadPoolManager(Math.Min(job.RequestedConcurrency, _options.MaxConcurrency));
            var limiter = bandwidthLimitBytesPerSecond is > 0
                ? new TokenBucketRateLimiter(bandwidthLimitBytesPerSecond.Value)
                : null;

            var activeSegments = 0;
            var tasks = job.Segments
                .Where(segment => segment.State != SegmentState.Completed && segment.RemainingBytes > 0)
                .Select(segment => threadPool.RunAsync(async token =>
                {
                    Interlocked.Increment(ref activeSegments);
                    try
                    {
                        await DownloadSegmentWithRetryAsync(job, segment, writer, limiter, speedMeter, () => activeSegments, token);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeSegments);
                    }
                }, cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);
            await writer.FlushAsync(cancellationToken);
            isCompleted = job.Segments.All(segment => segment.State == SegmentState.Completed);
        }

        if (isCompleted)
        {
            // Windows khong cho rename/delete file khi handle van mo.
            // Vi vay SafeFileWriter phai dispose xong moi move .part thanh file dich.
            File.Move(job.PartPath, job.OutputPath, overwrite: true);
            job.TransitionTo(DownloadState.Completed);
            await _repository.UpdateJobStateAsync(job.Id, DownloadState.Completed, cancellationToken);
            Publish(job, speedMeter.BytesPerSecond, "Download hoan tat.");
        }
    }

    private async Task DownloadSegmentWithRetryAsync(
        DownloadJob job,
        DownloadSegment segment,
        ISafeFileWriter writer,
        TokenBucketRateLimiter? limiter,
        SpeedMeter speedMeter,
        Func<int> activeSegmentCount,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                segment.MarkRunning();
                await DownloadSegmentOnceAsync(job, segment, writer, limiter, speedMeter, activeSegmentCount, cancellationToken);
                await _repository.UpdateSegmentAsync(job.Id, segment, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < _options.MaxRetryAttempts)
            {
                attempt++;
                var delay = TimeSpan.FromMilliseconds(_options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                Publish(job, speedMeter.BytesPerSecond, $"Segment {segment.Index} loi, retry lan {attempt}: {ex.Message}");
                await Task.Delay(delay, cancellationToken);
            }
            catch
            {
                segment.MarkFailed();
                await _repository.UpdateSegmentAsync(job.Id, segment, CancellationToken.None);
                throw;
            }
        }
    }

    private async Task DownloadSegmentOnceAsync(
        DownloadJob job,
        DownloadSegment segment,
        ISafeFileWriter writer,
        TokenBucketRateLimiter? limiter,
        SpeedMeter speedMeter,
        Func<int> activeSegmentCount,
        CancellationToken cancellationToken)
    {
        var start = segment.NextOffset;
        if (start > segment.End)
        {
            segment.MarkCompleted();
            return;
        }

        var lastProgressPublish = DateTimeOffset.MinValue;
        await using var writeStream = new SegmentFileWriteStream(
            writer,
            start,
            limiter,
            bytesWritten =>
            {
                segment.AddProgress(bytesWritten);
                speedMeter.Add(bytesWritten);
                var now = DateTimeOffset.UtcNow;
                if (now - lastProgressPublish >= TimeSpan.FromMilliseconds(250))
                {
                    lastProgressPublish = now;
                    Publish(job, speedMeter.BytesPerSecond, $"Dang tai segment {segment.Index}.", activeSegmentCount());
                }
            },
            cancellationToken);

        await _httpClient.DownloadRangeAsync(job.Url, start, segment.End, writeStream, cancellationToken);

        if (segment.RemainingBytes == 0)
        {
            segment.MarkCompleted();
        }

        await _repository.UpdateSegmentAsync(job.Id, segment, cancellationToken);
        Publish(job, speedMeter.BytesPerSecond, $"Segment {segment.Index} hoan tat.", activeSegmentCount());
    }

    private async Task<DownloadJob> LoadJobOrThrowAsync(Guid jobId, CancellationToken cancellationToken)
        => await _repository.FindAsync(jobId, cancellationToken)
           ?? throw new KeyNotFoundException($"Khong tim thay download job {jobId}.");

    private void Publish(DownloadJob job, double speedBytesPerSecond, string message, int activeSegments = 0)
    {
        _progressReporter.Publish(new DownloadProgressSnapshot(
            job.Id,
            job.State,
            job.DownloadedBytes,
            job.TotalBytes,
            job.ProgressPercent,
            speedBytesPerSecond,
            activeSegments,
            message,
            DateTimeOffset.UtcNow));
    }

    private sealed class SpeedMeter
    {
        private readonly object _lock = new();
        private readonly Queue<(DateTimeOffset Timestamp, long Bytes)> _samples = new();
        private long _bytesInWindow;

        public double BytesPerSecond
        {
            get
            {
                lock (_lock)
                {
                    Trim();
                    return _bytesInWindow / 5.0;
                }
            }
        }

        public void Add(long bytes)
        {
            lock (_lock)
            {
                Trim();
                _samples.Enqueue((DateTimeOffset.UtcNow, bytes));
                _bytesInWindow += bytes;
            }
        }

        private void Trim()
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-5);
            while (_samples.TryPeek(out var sample) && sample.Timestamp < cutoff)
            {
                _samples.Dequeue();
                _bytesInWindow -= sample.Bytes;
            }
        }
    }
}
