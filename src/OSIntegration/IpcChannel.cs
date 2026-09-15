using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace pbl4.OSIntegration;

public sealed class IpcChannel
{
    private readonly string _pipeName;

    public IpcChannel(string pipeName = "pbl4-download-engine")
    {
        _pipeName = pipeName;
    }

    public async Task StartServerAsync(
        Func<IpcMessage, CancellationToken, Task<IpcMessage?>> handler,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);

            // Named Pipe minh hoa IPC tren Windows: UI process va Engine process trao doi message noi bo may.
            await server.WaitForConnectionAsync(cancellationToken);
            var request = await ReadMessageAsync(server, cancellationToken);
            var response = await handler(request, cancellationToken);

            if (response is not null)
            {
                await WriteMessageAsync(server, response, cancellationToken);
            }
        }
    }

    public async Task<IpcMessage?> SendAsync(IpcMessage message, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        await client.ConnectAsync(timeoutCts.Token);
        await WriteMessageAsync(client, message, timeoutCts.Token);

        if (!client.CanRead)
        {
            return null;
        }

        return await ReadMessageAsync(client, timeoutCts.Token);
    }

    private static async Task<IpcMessage> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var json = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("IPC message rong.");
        }

        return JsonSerializer.Deserialize<IpcMessage>(json)
               ?? throw new InvalidOperationException("IPC message khong parse duoc.");
    }

    private static async Task WriteMessageAsync(Stream stream, IpcMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

