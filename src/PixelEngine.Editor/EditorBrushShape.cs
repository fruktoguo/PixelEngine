namespace PixelEngine.Editor;

/// <summary>
/// 编辑器世界画刷形状。
/// </summary>
public enum EditorBrushShape : byte
{
    /// <summary>
    /// 单点。
    /// </summary>
    Point,

    /// <summary>
    /// 圆形或椭圆形；横纵半径相等时为圆形。
    /// </summary>
    Circle,

    /// <summary>
    /// 方形或矩形；横纵半径相等时为方形。
    /// </summary>
    Square,
}
