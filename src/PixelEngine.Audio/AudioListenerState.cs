using System.Numerics;

namespace PixelEngine.Audio;

/// <summary>
/// OpenAL listener 的 2D 世界映射状态。
/// </summary>
public readonly record struct AudioListenerState(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    float Gain)
{
    /// <summary>
    /// 根据当前相机视图与音频设置计算 listener 状态。
    /// </summary>
    /// <param name="view">相机视口快照。</param>
    /// <param name="settings">音频设置。</param>
    /// <returns>listener 状态。</returns>
    public static AudioListenerState FromView(in AudioListenerView view, AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = settings.Validate();
        if (!float.IsFinite(view.CellsPerPixel) || view.CellsPerPixel <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(view), "CellsPerPixel 必须为正有限数。");
        }

        if (view.ViewportWidth <= 0 || view.ViewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(view), "Viewport 尺寸必须为正数。");
        }

        AudioSpace space = new(settings.PixelsPerMeter);
        Vector3 center = space.ToMeters(view.CenterWorldX, view.CenterWorldY);
        center.Z = settings.ListenerDepth;
        return new AudioListenerState(
            center,
            new Vector3(0f, 0f, -1f),
            Vector3.UnitY,
            settings.MasterVolume);
    }
}
