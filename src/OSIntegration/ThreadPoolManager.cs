namespace pbl4.OSIntegration;

public sealed class ThreadPoolManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public ThreadPoolManager(int maxConcurrency)
    {
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Concurrency phai lon hon 0.");
        }

        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Work chay tren .NET ThreadPool thong qua Task scheduler mac dinh.
            // SemaphoreSlim gioi han so segment dong thoi de tranh tao qua nhieu luong/I/O.
            await Task.Run(() => work(cancellationToken), cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        _semaphore.Dispose();
    }
}
