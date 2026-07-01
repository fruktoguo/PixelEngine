namespace PixelEngine.Rendering;

/// <summary>
/// OverlayRenderer 支持的屏幕空间绘制原语类型。
/// </summary>
public enum OverlayPrimitiveType
{
    /// <summary>
    /// 以单一 BGRA8 颜色填充的矩形。
    /// </summary>
    SolidRectangle,

    /// <summary>
    /// 只绘制边框的矩形，边框位于矩形内部。
    /// </summary>
    OutlineRectangle,

    /// <summary>
    /// 从 OpenGL 2D 纹理采样的精灵矩形。
    /// </summary>
    Sprite,

    /// <summary>
    /// 带厚度的屏幕空间线段。
    /// </summary>
    Line,
}
