using PixelEngine.Core.Diagnostics;
using Xunit;

namespace PixelEngine.Core.Tests;

/// <summary>
/// 通用自定义 metric channel 的发布、快照与清理测试。
/// </summary>
public sealed class CustomMetricChannelTests
{
    /// <summary>
    /// 验证 label/value 会作为同一快照读取。
    /// </summary>
    [Fact]
    public void PublishReadAndClearExposeGenericMetric()
    {
        CustomMetricChannel channel = new();

        channel.Read(out string initialName, out long initialValue);
        Assert.Equal(string.Empty, initialName);
        Assert.Equal(0, initialValue);

        channel.Publish("test_metric", 12345);
        channel.Read(out string name, out long value);
        Assert.Equal("test_metric", name);
        Assert.Equal(12345, value);

        channel.Clear();
        channel.Read(out string clearedName, out long clearedValue);
        Assert.Equal(string.Empty, clearedName);
        Assert.Equal(0, clearedValue);
    }

    /// <summary>
    /// 验证空 label 不会污染诊断通道。
    /// </summary>
    [Fact]
    public void PublishRejectsEmptyName()
    {
        CustomMetricChannel channel = new();

        _ = Assert.Throws<ArgumentException>(() => channel.Publish(" ", 1));
    }
}
