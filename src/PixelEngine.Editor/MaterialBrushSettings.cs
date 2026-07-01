namespace PixelEngine.Editor;

/// <summary>
/// 世界材质/温度画刷参数。
/// </summary>
public sealed class MaterialBrushSettings
{
    /// <summary>
    /// 当前工具。
    /// </summary>
    public EditorBrushTool Tool { get; set; } = EditorBrushTool.Paint;

    /// <summary>
    /// 画刷形状。
    /// </summary>
    public EditorBrushShape Shape { get; set; } = EditorBrushShape.Circle;

    /// <summary>
    /// 目标材质 id。
    /// </summary>
    public ushort MaterialId { get; set; }

    /// <summary>
    /// 半径，0 表示单 cell。
    /// </summary>
    public int Radius { get; set; } = 2;

    /// <summary>
    /// 应用概率，范围 0..1。
    /// </summary>
    public float Probability { get; set; } = 1f;

    /// <summary>
    /// 温度笔刷模式。
    /// </summary>
    public TemperatureBrushMode TemperatureMode { get; set; } = TemperatureBrushMode.Additive;

    /// <summary>
    /// 温度增量或目标温度，单位摄氏度。
    /// </summary>
    public float TemperatureCelsius { get; set; } = 100f;

    /// <summary>
    /// 返回钳制后的半径。
    /// </summary>
    public int ClampedRadius => Math.Clamp(Radius, 0, 128);

    /// <summary>
    /// 返回钳制后的概率。
    /// </summary>
    public float ClampedProbability => Math.Clamp(Probability, 0f, 1f);
}
