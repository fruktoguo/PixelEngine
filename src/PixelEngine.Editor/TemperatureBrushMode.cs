namespace PixelEngine.Editor;

/// <summary>
/// 温度笔刷写入模式。
/// </summary>
public enum TemperatureBrushMode : byte
{
    /// <summary>
    /// 在当前温度上叠加。
    /// </summary>
    Additive,

    /// <summary>
    /// 调整到目标温度。
    /// </summary>
    Target,
}
