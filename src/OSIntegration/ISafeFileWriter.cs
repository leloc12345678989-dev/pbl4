namespace pbl4.OSIntegration;

public interface ISafeFileWriter : IAsyncDisposable
{
    Task InitializeAsync(long length, CancellationToken cancellationToken = default);

    Task WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);
}

