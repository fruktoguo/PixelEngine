namespace PixelEngine.Rendering;

/// <summary>
/// OverlayRenderer 使用的纹理精灵描述。目标位置由 OverlayCommand 提供，本结构只描述纹理来源。
/// </summary>
/// <param name="TextureHandle">OpenGL 2D 纹理句柄，必须非 0。</param>
/// <param name="TextureWidth">纹理宽度，单位为像素。</param>
/// <param name="TextureHeight">纹理高度，单位为像素。</param>
/// <param name="U0">精灵左侧归一化 U 坐标。</param>
/// <param name="V0">精灵顶部归一化 V 坐标。</param>
/// <param name="U1">精灵右侧归一化 U 坐标。</param>
/// <param name="V1">精灵底部归一化 V 坐标。</param>
public readonly record struct OverlaySprite(
    uint TextureHandle,
    int TextureWidth,
    int TextureHeight,
    float U0,
    float V0,
    float U1,
    float V1)
{
    /// <summary>
    /// 创建覆盖整张纹理的精灵描述。
    /// </summary>
    /// <param name="textureHandle">OpenGL 2D 纹理句柄，必须非 0。</param>
    /// <param name="textureWidth">纹理宽度，单位为像素。</param>
    /// <param name="textureHeight">纹理高度，单位为像素。</param>
    public OverlaySprite(uint textureHandle, int textureWidth, int textureHeight)
        : this(textureHandle, textureWidth, textureHeight, 0f, 0f, 1f, 1f)
    {
    }

    /// <summary>
    /// 校验精灵纹理与 UV 参数。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">纹理句柄、尺寸或 UV 参数非法时抛出。</exception>
    public void Validate()
    {
        if (TextureHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TextureHandle), "Overlay sprite 纹理句柄必须非 0。");
        }

        if (TextureWidth <= 0 || TextureHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TextureWidth), "Overlay sprite 纹理尺寸必须为正数。");
        }

        if (!float.IsFinite(U0) || !float.IsFinite(V0) || !float.IsFinite(U1) || !float.IsFinite(V1))
        {
            throw new ArgumentOutOfRangeException(nameof(U0), "Overlay sprite UV 必须为有限数值。");
        }

        if (U0 < 0f || V0 < 0f || U1 > 1f || V1 > 1f || U0 >= U1 || V0 >= V1)
        {
            throw new ArgumentOutOfRangeException(nameof(U0), "Overlay sprite UV 必须位于 [0,1] 且形成非空区域。");
        }
    }
}
