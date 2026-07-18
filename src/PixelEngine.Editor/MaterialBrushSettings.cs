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
    /// 兼容旧调用方的统一半径；读取时返回横纵半径的较大值，写入时同时更新两轴。
    /// </summary>
    public int Radius
    {
        get => Math.Max(RadiusX, RadiusY);
        set
        {
            RadiusX = value;
            RadiusY = value;
        }
    }

    /// <summary>
    /// 横向半径，0 表示仅覆盖中心列。
    /// </summary>
    public int RadiusX { get; set; } = 2;

    /// <summary>
    /// 纵向半径，0 表示仅覆盖中心行。
    /// </summary>
    public int RadiusY { get; set; } = 2;

    /// <summary>
    /// UI 调整一轴时是否同步另一轴；关闭后圆形变为椭圆、方形变为矩形。
    /// </summary>
    public bool LockAspectRatio { get; set; } = true;

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
    /// 返回钳制后的横向半径。
    /// </summary>
    public int ClampedRadiusX => Math.Clamp(RadiusX, 0, 128);

    /// <summary>
    /// 返回钳制后的纵向半径。
    /// </summary>
    public int ClampedRadiusY => Math.Clamp(RadiusY, 0, 128);

    /// <summary>
    /// 返回钳制后的概率。
    /// </summary>
    public float ClampedProbability => Math.Clamp(Probability, 0f, 1f);
}
