namespace pbl4.Core;

public sealed class TokenBucketRateLimiter
{
    private readonly object _lock = new();
    private readonly long _capacity;
    private readonly long _refillBytesPerSecond;
    private double _tokens;
    private DateTimeOffset _lastRefill;

    public TokenBucketRateLimiter(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        }

        _capacity = bytesPerSecond;
        _refillBytesPerSecond = bytesPerSecond;
        _tokens = bytesPerSecond;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    public async Task WaitAsync(int bytes, CancellationToken cancellationToken = default)
    {
        if (bytes <= 0)
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double missingTokens;

            lock (_lock)
            {
                Refill();
                if (_tokens >= bytes)
                {
                    _tokens -= bytes;
                    return;
                }

                missingTokens = bytes - _tokens;
            }

            var delayMs = Math.Max(1, missingTokens / _refillBytesPerSecond * 1000.0);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
        }
    }

    private void Refill()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = (now - _lastRefill).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return;
        }

        _tokens = Math.Min(_capacity, _tokens + elapsedSeconds * _refillBytesPerSecond);
        _lastRefill = now;
    }
}

