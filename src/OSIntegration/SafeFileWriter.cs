using Microsoft.Win32.SafeHandles;

namespace pbl4.OSIntegration;

public sealed class SafeFileWriter : ISafeFileWriter
{
    private readonly string _path;
    private readonly SemaphoreSlim _fileWriteGate = new(1, 1);
    private FileStream? _stream;
    private SafeFileHandle? _handle;
    private long _length;
    private bool _initialized;

    public SafeFileWriter(string path)
    {
        _path = path;
    }

    public async Task InitializeAsync(long length, CancellationToken cancellationToken = default)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Kich thuoc file phai lon hon 0.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)) ?? ".");
        _length = length;
        _stream = new FileStream(
            _path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        _stream.SetLength(length);
        await _stream.FlushAsync(cancellationToken);
        _handle = _stream.SafeFileHandle;
        _initialized = true;
    }

    public async Task WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        if (offset < 0 || offset + buffer.Length > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Vung ghi nam ngoai kich thuoc file.");
        }

        // Critical section bat dong bo: nhieu segment co the cung ghi vao mot file .part.
        // SemaphoreSlim.WaitAsync khong block ThreadPool thread trong luc cho den luot ghi.
        // Timeout giup phat hien tranh chap bat thuong thay vi doi vo han.
        if (!await _fileWriteGate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
        {
            throw new TimeoutException("Khong lay duoc FileWriteGate trong thoi gian cho phep.");
        }

        try
        {
            // FileStream duoc mo voi FileOptions.Asynchronous tren Windows.
            // RandomAccess.WriteAsync ghi theo offset va duoc .NET map xuong overlapped I/O,
            // giup kernel xu ly I/O bat dong bo thay vi block UI thread.
            await RandomAccess.WriteAsync(_handle!, buffer, offset, cancellationToken);
        }
        finally
        {
            _fileWriteGate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await _stream!.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _fileWriteGate.Dispose();
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _stream is null || _handle is null)
        {
            throw new InvalidOperationException("SafeFileWriter chua duoc InitializeAsync.");
        }
    }
}
