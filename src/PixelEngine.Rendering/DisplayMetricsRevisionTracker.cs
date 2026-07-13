namespace PixelEngine.Rendering;

/// <summary>
/// 将平台即时采样收敛为只在帧边界递增的显示度量 revision。
/// </summary>
internal sealed class DisplayMetricsRevisionTracker
{
    private bool _initialized;

    internal DisplayMetricsSnapshot Current { get; private set; }

    internal DisplayMetricsSnapshot Commit(
        nint monitorId,
        float framebufferScaleX,
        float framebufferScaleY,
        float? actualPhysicalDpi)
    {
        DisplayMetricsSnapshot candidate = new(
            monitorId,
            framebufferScaleX,
            framebufferScaleY,
            actualPhysicalDpi,
            _initialized ? Current.Revision : 1);
        candidate.Validate();
        if (!_initialized)
        {
            Current = candidate;
            _initialized = true;
            return Current;
        }

        if (Current.MonitorId != monitorId ||
            !Current.FramebufferScaleX.Equals(framebufferScaleX) ||
            !Current.FramebufferScaleY.Equals(framebufferScaleY) ||
            !Nullable.Equals(Current.ActualPhysicalDpi, actualPhysicalDpi))
        {
            Current = candidate with { Revision = checked(Current.Revision + 1) };
        }

        return Current;
    }
}
