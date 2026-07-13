namespace PixelEngine.UI;

/// <summary>
/// Web Canvas 的 Unity-compatible CanvasScaler 设置。设备度量只参与解析，不会反写这些入盘值。
/// </summary>
public readonly record struct UiCanvasScalerSettings
{
    /// <summary>
    /// 创建 Unity-compatible 默认 CanvasScaler 设置。
    /// </summary>
    public UiCanvasScalerSettings()
        : this(
            UiScaleMode.ConstantPixelSize,
            1f,
            800f,
            600f,
            UiScreenMatchMode.MatchWidthOrHeight,
            0f,
            UiPhysicalUnit.Points,
            96f,
            96f,
            100f)
    {
    }

    /// <summary>
    /// 创建完整 CanvasScaler 设置。
    /// </summary>
    /// <param name="scaleMode">UI 缩放模式。</param>
    /// <param name="scaleFactor">Constant Pixel Size 的固定缩放。</param>
    /// <param name="referenceWidth">参考分辨率宽度。</param>
    /// <param name="referenceHeight">参考分辨率高度。</param>
    /// <param name="screenMatchMode">参考分辨率宽高合并方式。</param>
    /// <param name="matchWidthOrHeight">0 完全匹配宽，1 完全匹配高。</param>
    /// <param name="physicalUnit">Constant Physical Size 的物理单位。</param>
    /// <param name="fallbackScreenDpi">平台不能提供 raw physical DPI 时的明确回退值。</param>
    /// <param name="defaultSpriteDpi">图片资产的默认 DPI。</param>
    /// <param name="referencePixelsPerUnit">每个 UI unit 对应的参考像素数。</param>
    public UiCanvasScalerSettings(
        UiScaleMode scaleMode = UiScaleMode.ConstantPixelSize,
        float scaleFactor = 1f,
        float referenceWidth = 800f,
        float referenceHeight = 600f,
        UiScreenMatchMode screenMatchMode = UiScreenMatchMode.MatchWidthOrHeight,
        float matchWidthOrHeight = 0f,
        UiPhysicalUnit physicalUnit = UiPhysicalUnit.Points,
        float fallbackScreenDpi = 96f,
        float defaultSpriteDpi = 96f,
        float referencePixelsPerUnit = 100f)
    {
        ScaleMode = scaleMode;
        ScaleFactor = scaleFactor;
        ReferenceWidth = referenceWidth;
        ReferenceHeight = referenceHeight;
        ScreenMatchMode = screenMatchMode;
        MatchWidthOrHeight = matchWidthOrHeight;
        PhysicalUnit = physicalUnit;
        FallbackScreenDpi = fallbackScreenDpi;
        DefaultSpriteDpi = defaultSpriteDpi;
        ReferencePixelsPerUnit = referencePixelsPerUnit;
    }

    /// <summary>
    /// Unity-compatible 默认值：Constant Pixel Size、800×600、Points、96 DPI、100 PPU。
    /// </summary>
    public static UiCanvasScalerSettings Default { get; } = new UiCanvasScalerSettings();

    /// <summary>UI 缩放模式。</summary>
    public UiScaleMode ScaleMode { get; init; }

    /// <summary>Constant Pixel Size 的固定缩放。</summary>
    public float ScaleFactor { get; init; }

    /// <summary>参考分辨率宽度。</summary>
    public float ReferenceWidth { get; init; }

    /// <summary>参考分辨率高度。</summary>
    public float ReferenceHeight { get; init; }

    /// <summary>Scale With Screen Size 的宽高合并方式。</summary>
    public UiScreenMatchMode ScreenMatchMode { get; init; }

    /// <summary>Match Width Or Height 插值；0 匹配宽，1 匹配高。</summary>
    public float MatchWidthOrHeight { get; init; }

    /// <summary>Constant Physical Size 的物理单位。</summary>
    public UiPhysicalUnit PhysicalUnit { get; init; }

    /// <summary>raw physical DPI 不可用时的回退 DPI。</summary>
    public float FallbackScreenDpi { get; init; }

    /// <summary>图片资产默认 DPI。</summary>
    public float DefaultSpriteDpi { get; init; }

    /// <summary>参考 pixels-per-unit。</summary>
    public float ReferencePixelsPerUnit { get; init; }
}
