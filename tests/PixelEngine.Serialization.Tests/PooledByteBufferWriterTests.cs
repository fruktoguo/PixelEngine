using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// ArrayPool-backed byte writer 测试。
/// </summary>
public sealed class PooledByteBufferWriterTests
{
    /// <summary>
    /// 验证 writer 可增长、清空并在 dispose 后拒绝使用。
    /// </summary>
    [Fact]
    public void PooledByteBufferWriterGrowsClearsAndRejectsAfterDispose()
    {
        PooledByteBufferWriter writer = new(initialCapacity: 4);

        Span<byte> span = writer.GetSpan(8);
        for (int i = 0; i < 8; i++)
        {
            span[i] = (byte)i;
        }

        writer.Advance(8);

        Assert.Equal(8, writer.WrittenCount);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], writer.ToArray());

        writer.Clear();
        Assert.Equal(0, writer.WrittenCount);

        writer.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => writer.GetSpan(1));
    }
}
