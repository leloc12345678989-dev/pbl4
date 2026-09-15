using pbl4.Core;
using Xunit;

namespace pbl4.Core.Tests;

public sealed class TokenBucketRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_WhenTokensAvailable_CompletesQuickly()
    {
        var limiter = new TokenBucketRateLimiter(1024);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await limiter.WaitAsync(128, cts.Token);

        Assert.False(cts.IsCancellationRequested);
    }
}

