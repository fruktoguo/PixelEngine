using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// GUI 动态文本缓冲区测试。
/// </summary>
public sealed class GuiTextBufferTests
{
    /// <summary>
    /// 验证容量增长后仍保留完整内容。
    /// </summary>
    [Fact]
    public void GrowthPreservesWrittenContent()
    {
        GuiTextBuffer buffer = new(4);

        _ = buffer.Append("生命 ").Append(87.5f, "0.0").Append('/').Append(100);

        Assert.Equal("生命 87.5/100", buffer.ToString());
        Assert.True(buffer.Capacity >= buffer.Length);
    }

    /// <summary>
    /// 验证预热并稳定容量后，清空、追加与 span 读取不产生托管堆分配。
    /// </summary>
    [Fact]
    public void StableFormattingDoesNotAllocate()
    {
        GuiTextBuffer buffer = new(128);
        FormatLine(buffer, 0, 0f);
        _ = buffer.WrittenSpan.Length;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            FormatLine(buffer, i, i / 10f);
            _ = buffer.WrittenSpan.Length;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal("Frame 999   99.9 ms", buffer.ToString());
    }

    private static void FormatLine(GuiTextBuffer buffer, int frame, float milliseconds)
    {
        _ = buffer.Clear()
            .Append("Frame ")
            .Append(frame)
            .Append("   ")
            .Append(milliseconds, "0.0")
            .Append(" ms");
    }
}
