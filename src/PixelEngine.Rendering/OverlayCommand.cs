namespace PixelEngine.Rendering;

/// <summary>
/// OverlayRenderer 的单条绘制命令。坐标以 viewport 左上角为原点，单位为屏幕像素。
/// </summary>
/// <param name="PrimitiveType">绘制原语类型。</param>
/// <param name="ViewportX">目标矩形左上角 X 坐标，单位为 viewport pixel。</param>
/// <param name="ViewportY">目标矩形左上角 Y 坐标，单位为 viewport pixel。</param>
/// <param name="Width">目标矩形宽度，单位为 viewport pixel。</param>
/// <param name="Height">目标矩形高度，单位为 viewport pixel。</param>
/// <param name="ColorBgra">矩形颜色或精灵 tint，BGRA8 非预乘 alpha。</param>
/// <param name="OutlineThickness">轮廓矩形的内部描边厚度，单位为 viewport pixel；其它原语忽略该值。</param>
/// <param name="Sprite">纹理精灵来源；仅当 <paramref name="PrimitiveType"/> 为 <see cref="OverlayPrimitiveType.Sprite"/> 时使用。</param>
public readonly record struct OverlayCommand(
    OverlayPrimitiveType PrimitiveType,
    float ViewportX,
    float ViewportY,
    float Width,
    float Height,
    uint ColorBgra,
    float OutlineThickness,
    OverlaySprite Sprite)
{
    /// <summary>
    /// 创建实色矩形命令。
    /// </summary>
    /// <param name="viewportX">目标矩形左上角 X 坐标，单位为 viewport pixel。</param>
    /// <param name="viewportY">目标矩形左上角 Y 坐标，单位为 viewport pixel。</param>
    /// <param name="width">目标矩形宽度，单位为 viewport pixel。</param>
    /// <param name="height">目标矩形高度，单位为 viewport pixel。</param>
    /// <param name="colorBgra">填充颜色，BGRA8 非预乘 alpha。</param>
    /// <returns>实色矩形命令。</returns>
    public static OverlayCommand SolidRectangle(float viewportX, float viewportY, float width, float height, uint colorBgra)
    {
        return new OverlayCommand(OverlayPrimitiveType.SolidRectangle, viewportX, viewportY, width, height, colorBgra, 0f, default);
    }

    /// <summary>
    /// 创建内部描边矩形命令。
    /// </summary>
    /// <param name="viewportX">目标矩形左上角 X 坐标，单位为 viewport pixel。</param>
    /// <param name="viewportY">目标矩形左上角 Y 坐标，单位为 viewport pixel。</param>
    /// <param name="width">目标矩形宽度，单位为 viewport pixel。</param>
    /// <param name="height">目标矩形高度，单位为 viewport pixel。</param>
    /// <param name="thickness">内部描边厚度，单位为 viewport pixel。</param>
    /// <param name="colorBgra">描边颜色，BGRA8 非预乘 alpha。</param>
    /// <returns>轮廓矩形命令。</returns>
    public static OverlayCommand OutlineRectangle(float viewportX, float viewportY, float width, float height, float thickness, uint colorBgra)
    {
        return new OverlayCommand(OverlayPrimitiveType.OutlineRectangle, viewportX, viewportY, width, height, colorBgra, thickness, default);
    }

    /// <summary>
    /// 创建纹理精灵命令。
    /// </summary>
    /// <param name="viewportX">目标矩形左上角 X 坐标，单位为 viewport pixel。</param>
    /// <param name="viewportY">目标矩形左上角 Y 坐标，单位为 viewport pixel。</param>
    /// <param name="width">目标矩形宽度，单位为 viewport pixel。</param>
    /// <param name="height">目标矩形高度，单位为 viewport pixel。</param>
    /// <param name="sprite">精灵纹理来源。</param>
    /// <param name="tintBgra">精灵 tint，BGRA8 非预乘 alpha；默认白色不改色。</param>
    /// <returns>纹理精灵命令。</returns>
    public static OverlayCommand SpriteRectangle(float viewportX, float viewportY, float width, float height, OverlaySprite sprite, uint tintBgra = 0xFFFFFFFFu)
    {
        return new OverlayCommand(OverlayPrimitiveType.Sprite, viewportX, viewportY, width, height, tintBgra, 0f, sprite);
    }

    /// <summary>
    /// 校验命令参数，失败时抛出明确异常。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">坐标、尺寸、描边厚度或精灵参数非法时抛出。</exception>
    public void Validate()
    {
        if (!float.IsFinite(ViewportX) || !float.IsFinite(ViewportY))
        {
            throw new ArgumentOutOfRangeException(nameof(ViewportX), "Overlay 坐标必须为有限数值。");
        }

        if (!float.IsFinite(Width) || !float.IsFinite(Height) || Width <= 0f || Height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Overlay 目标矩形尺寸必须为正有限数值。");
        }

        switch (PrimitiveType)
        {
            case OverlayPrimitiveType.SolidRectangle:
                return;
            case OverlayPrimitiveType.OutlineRectangle:
                if (!float.IsFinite(OutlineThickness) || OutlineThickness <= 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(OutlineThickness), "Overlay 轮廓厚度必须为正有限数值。");
                }

                return;
            case OverlayPrimitiveType.Sprite:
                Sprite.Validate();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(PrimitiveType), "未知 Overlay 原语类型。");
        }
    }
}
