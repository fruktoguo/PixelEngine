namespace PixelEngine.Rendering;

/// <summary>
/// 渲染管线最终画面的只读纹理快照；不转移 OpenGL 纹理所有权。
/// </summary>
public readonly struct RenderViewportTexture
{
    /// <summary>
    /// 创建有效的最终画面纹理快照。
    /// </summary>
    /// <param name="handle">OpenGL 2D 纹理句柄。</param>
    /// <param name="width">纹理宽度。</param>
    /// <param name="height">纹理高度。</param>
    public RenderViewportTexture(uint handle, int width, int height)
    {
        if (handle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "纹理句柄必须非零。");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "纹理宽度必须为正数。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "纹理高度必须为正数。");
        }

        Handle = handle;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// OpenGL 2D 纹理句柄。
    /// </summary>
    public uint Handle { get; }

    /// <summary>
    /// 纹理宽度。
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 纹理高度。
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// 快照是否指向有效纹理。
    /// </summary>
    public bool IsValid => Handle != 0 && Width > 0 && Height > 0;
}
