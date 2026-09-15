using pbl4.OSIntegration;
using Xunit;

namespace pbl4.OSIntegration.Tests;

public sealed class SafeFileWriterTests
{
    [Fact]
    public async Task WriteAsync_WithDifferentOffsets_WritesExpectedBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pbl4-writer-{Guid.NewGuid():N}.part");

        await using (var writer = new SafeFileWriter(path))
        {
            await writer.InitializeAsync(6);
            await writer.WriteAsync(3, new byte[] { 4, 5, 6 });
            await writer.WriteAsync(0, new byte[] { 1, 2, 3 });
            await writer.FlushAsync();
        }

        var bytes = await File.ReadAllBytesAsync(path);
        File.Delete(path);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, bytes);
    }
}

