namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 已解析并上传的图片资产。
/// </summary>
/// <param name="TextureHandle">当前 GL 上下文中的 Texture2D 句柄；测试宿主可使用非零虚拟句柄。</param>
/// <param name="Width">图片像素宽度。</param>
/// <param name="Height">图片像素高度。</param>
public readonly record struct ManagedFallbackImage(uint TextureHandle, int Width, int Height)
{
    /// <summary>
    /// 校验图片资产是否可绘制。
    /// </summary>
    public void Validate()
    {
        if (TextureHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TextureHandle), "图片纹理句柄必须非 0。");
        }

        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "图片尺寸必须为正数。");
        }
    }
}
