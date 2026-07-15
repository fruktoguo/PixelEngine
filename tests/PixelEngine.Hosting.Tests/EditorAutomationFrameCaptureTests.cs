using System.Buffers.Binary;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>Automation screenshot 的一次性生命周期与后台 BMP 编码回归。</summary>
public sealed class EditorAutomationFrameCaptureTests
{
    /// <summary>验证 RGBA readback 原地转换为合法的 32-bit bottom-up BMP。</summary>
    [Fact]
    public async Task BmpEncoderWritesHeaderAndBgraPixels()
    {
        EditorAutomationRawCapture capture = new()
        {
            Kind = "test",
            Width = 2,
            Height = 1,
            ContentRevision = 7,
            RgbaBottomUp =
            [
                255, 0, 0, 255,
                0, 0, 255, 255,
            ],
        };
        await using MemoryStream stream = new();

        await EditorAutomationBmpEncoder.WriteAsync(stream, capture, CancellationToken.None);

        byte[] bmp = stream.ToArray();
        Assert.Equal(62, bmp.Length);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(62, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2, 4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22, 4)));
        Assert.Equal((ushort)32, BinaryPrimitives.ReadUInt16LittleEndian(bmp.AsSpan(28, 2)));
        Assert.Equal(
            new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 },
            bmp.AsSpan(54).ToArray());
    }

    /// <summary>验证取消等待会物理解除尚未执行的 render callback。</summary>
    [Fact]
    public async Task CancellationDisposesPendingRenderRegistration()
    {
        EditorAutomationFrameCapture capture = new();
        CountingDisposable registration = new();
        capture.Attach(registration);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => capture.WaitAsync(cancellation.Token).AsTask());

        Assert.Equal(1, registration.DisposeCount);
    }

    /// <summary>验证 readback 只完成一次并在继续后台编码前解除 callback。</summary>
    [Fact]
    public async Task CompletionIsSingleShotAndDisposesRenderRegistration()
    {
        EditorAutomationFrameCapture capture = new();
        CountingDisposable registration = new();
        capture.Attach(registration);
        EditorAutomationRawCapture expected = new()
        {
            Kind = "game-presentation",
            Width = 1,
            Height = 1,
            ContentRevision = 9,
            RgbaBottomUp = [1, 2, 3, 4],
        };

        capture.Complete(() => expected);
        capture.Complete(static () => throw new InvalidOperationException("不得执行第二次 capture。"));
        EditorAutomationRawCapture actual = await capture.WaitAsync(CancellationToken.None);

        Assert.Same(expected, actual);
        Assert.Equal(1, registration.DisposeCount);
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
