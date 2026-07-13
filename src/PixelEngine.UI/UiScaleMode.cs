namespace PixelEngine.UI;

/// <summary>
/// Web Canvas 的缩放模式，对齐 Unity CanvasScaler 的三个 UI Scale Mode。
/// </summary>
public enum UiScaleMode : byte
{
    /// <summary>
    /// 使用固定缩放因子；Canvas 逻辑尺寸随 presentation 像素尺寸变化。
    /// </summary>
    ConstantPixelSize = 0,

    /// <summary>
    /// 相对参考分辨率计算缩放因子。
    /// </summary>
    ScaleWithScreenSize = 1,

    /// <summary>
    /// 使用显示器物理 DPI 与目标物理单位计算缩放因子。
    /// </summary>
    ConstantPhysicalSize = 2,
}
