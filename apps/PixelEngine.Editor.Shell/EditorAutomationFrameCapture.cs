using System.Buffers.Binary;

namespace PixelEngine.Editor.Shell;

/// <summary>OpenGL 左下原点 framebuffer 内的捕获矩形。</summary>
internal readonly record struct EditorFramebufferCaptureRect(int X, int Y, int Width, int Height)
{
    public bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0;
}

/// <summary>GPU safe phase 冻结的 RGBA8、左下行优先图像。</summary>
internal sealed record EditorAutomationRawCapture
{
    public required string Kind { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required long ContentRevision { get; init; }

    public required byte[] RgbaBottomUp { get; init; }
}

/// <summary>一次性 present 前 readback；取消与完成都会解除 render callback。</summary>
internal sealed class EditorAutomationFrameCapture
{
    private readonly TaskCompletionSource<EditorAutomationRawCapture> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IDisposable? _registration;
    private int _terminal;

    public void Attach(IDisposable registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (Interlocked.CompareExchange(ref _registration, registration, null) is not null)
        {
            registration.Dispose();
            throw new InvalidOperationException("Frame capture callback 已注册。 ");
        }

        if (Volatile.Read(ref _terminal) != 0)
        {
            DisposeRegistration();
        }
    }

    public void Complete(Func<EditorAutomationRawCapture> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _ = _completion.TrySetResult(capture());
        }
        catch (Exception exception)
        {
            _ = _completion.TrySetException(exception);
        }
        finally
        {
            DisposeRegistration();
        }
    }

    public async ValueTask<EditorAutomationRawCapture> WaitAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(Cancel);
        return await _completion.Task.ConfigureAwait(false);
    }

    private void Cancel()
    {
        if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
        {
            return;
        }

        _ = _completion.TrySetCanceled();
        DisposeRegistration();
    }

    private void DisposeRegistration()
    {
        Interlocked.Exchange(ref _registration, null)?.Dispose();
    }
}

/// <summary>在后台把独占 RGBA8 readback 原地转换并编码为无损 32-bit BMP。</summary>
internal static class EditorAutomationBmpEncoder
{
    private const int HeaderBytes = 54;

    public static async ValueTask WriteAsync(
        Stream stream,
        EditorAutomationRawCapture capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(capture);
        int pixelBytes = checked(capture.Width * capture.Height * 4);
        if (capture.RgbaBottomUp.Length != pixelBytes)
        {
            throw new InvalidDataException("Automation capture 像素长度与宽高不一致。");
        }

        Span<byte> pixels = capture.RgbaBottomUp;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            if ((i & 0xF_FFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        byte[] header = new byte[HeaderBytes];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2, 4), checked(HeaderBytes + pixelBytes));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10, 4), HeaderBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18, 4), capture.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22, 4), capture.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28, 2), 32);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(34, 4), pixelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(38, 4), 2_835);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(42, 4), 2_835);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(capture.RgbaBottomUp, cancellationToken).ConfigureAwait(false);
    }
}
