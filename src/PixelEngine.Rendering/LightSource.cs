namespace PixelEngine.Rendering;

/// <summary>
/// 相位 10 光照管线消费的点光源快照。
/// </summary>
/// <param name="X">光源屏幕/视口 X 坐标，单位为像素。</param>
/// <param name="Y">光源屏幕/视口 Y 坐标，单位为像素。</param>
/// <param name="Radius">光源半径，单位为像素。</param>
/// <param name="ColorBgra">光源颜色，BGRA8。</param>
/// <param name="Intensity">光照强度，1 表示原始颜色强度。</param>
public readonly record struct LightSource(
    float X,
    float Y,
    float Radius,
    uint ColorBgra,
    float Intensity)
{
    /// <summary>
    /// 校验光源参数，失败时抛出明确异常。
    /// </summary>
    public void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y))
        {
            throw new ArgumentOutOfRangeException(nameof(X), "光源坐标必须为有限数值。");
        }

        if (!float.IsFinite(Radius) || Radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Radius), "光源半径必须为正有限数值。");
        }

        if (!float.IsFinite(Intensity) || Intensity < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Intensity), "光源强度必须为非负有限数值。");
        }
    }
}
