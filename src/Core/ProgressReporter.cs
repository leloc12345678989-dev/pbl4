using System.Collections.Concurrent;
using System.Threading.Channels;

namespace pbl4.Core;

public sealed class ProgressReporter
{
    private readonly ConcurrentDictionary<Guid, List<Channel<DownloadProgressSnapshot>>> _subscribers = new();

    public void Publish(DownloadProgressSnapshot snapshot)
    {
        if (!_subscribers.TryGetValue(snapshot.JobId, out var channels))
        {
            return;
        }

        lock (channels)
        {
            foreach (var channel in channels.ToArray())
            {
                if (!channel.Writer.TryWrite(snapshot))
                {
                    channels.Remove(channel);
                }
            }
        }
    }

    public ChannelReader<DownloadProgressSnapshot> Subscribe(Guid jobId)
    {
        var channel = Channel.CreateUnbounded<DownloadProgressSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var channels = _subscribers.GetOrAdd(jobId, _ => new List<Channel<DownloadProgressSnapshot>>());
        lock (channels)
        {
            channels.Add(channel);
        }

        return channel.Reader;
    }
}

