using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 离屏 UI 位图合成器。供 Ultralight 等 CPU BGRA8 surface 后端复用同一 UI overlay 上传与三角形提交路径。
/// </summary>
public sealed class UiOffscreenSurfacePresenter : IDisposable
{
    private readonly UiVertex[] _vertices = new UiVertex[4];
    private readonly ushort[] _indices = [0, 1, 2, 0, 2, 3];
    private UiOverlayTexture? _texture;
    private bool _disposed;

    /// <summary>
    /// 当前 GL 纹理句柄；尚未首次合成时为 0。
    /// </summary>
    public uint TextureHandle => _texture?.Handle ?? 0;

    /// <summary>
    /// 当前离屏 surface 宽度；尚未首次合成时为 0。
    /// </summary>
    public int Width => _texture?.Width ?? 0;

    /// <summary>
    /// 当前离屏 surface 高度；尚未首次合成时为 0。
    /// </summary>
    public int Height => _texture?.Height ?? 0;

    /// <summary>
    /// 上传离屏 BGRA8 surface 的脏矩形并把它作为一个 alpha blended textured quad 合成到 UI present 层。
    /// </summary>
    /// <param name="context">渲染管线提供的 UI present 上下文。</param>
    /// <param name="pixelsBgra">按行连续的 BGRA8 surface 像素，左上角为第 0 行。</param>
    /// <param name="sourceWidth">surface 宽度。</param>
    /// <param name="sourceHeight">surface 高度。</param>
    /// <param name="dirtyRects">待上传脏矩形；纹理已存在且无脏矩形时只重绘现有纹理。</param>
    /// <param name="viewport">该 surface 在 framebuffer 中的目标区域。</param>
    public void Present(
        in UiPresentContext context,
        ReadOnlySpan<uint> pixelsBgra,
        int sourceWidth,
        int sourceHeight,
        ReadOnlySpan<PixelUploadRect> dirtyRects,
        in UiViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context.Gl);
        ValidateSource(pixelsBgra, sourceWidth, sourceHeight);
        viewport.Validate();

        bool needsFullUpload = EnsureTexture(context, sourceWidth, sourceHeight);
        if (needsFullUpload)
        {
            context.UploadOverlayTexture(_texture!, pixelsBgra);
        }
        else if (!dirtyRects.IsEmpty)
        {
            context.UploadOverlayTexture(_texture!, pixelsBgra, sourceWidth, sourceHeight, dirtyRects);
        }
        else if (_texture is null)
        {
            return;
        }

        SubmitQuad(context, in viewport);
    }

    /// <summary>
    /// 释放离屏 UI 纹理资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _texture?.Dispose();
        _disposed = true;
    }

    private bool EnsureTexture(in UiPresentContext context, int sourceWidth, int sourceHeight)
    {
        if (_texture is null)
        {
            _texture = new UiOverlayTexture(context.Gl, sourceWidth, sourceHeight);
            return true;
        }

        if (_texture.Width == sourceWidth && _texture.Height == sourceHeight)
        {
            return false;
        }

        _texture.Resize(sourceWidth, sourceHeight);
        return true;
    }

    private void SubmitQuad(in UiPresentContext context, in UiViewport viewport)
    {
        float left = viewport.X;
        float top = viewport.Y;
        float right = viewport.X + viewport.Width;
        float bottom = viewport.Y + viewport.Height;
        const uint white = 0xFFFFFFFFu;

        _vertices[0] = new UiVertex(left, top, 0f, 0f, white);
        _vertices[1] = new UiVertex(right, top, 1f, 0f, white);
        _vertices[2] = new UiVertex(right, bottom, 1f, 1f, white);
        _vertices[3] = new UiVertex(left, bottom, 0f, 1f, white);
        context.SubmitTriangles(_vertices, _indices, UiDrawState.Textured(_texture!.Handle));
    }

    private static void ValidateSource(ReadOnlySpan<uint> pixelsBgra, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), sourceWidth, "离屏 UI surface 宽度必须为正数。");
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight), sourceHeight, "离屏 UI surface 高度必须为正数。");
        }

        int requiredLength = checked(sourceWidth * sourceHeight);
        if (pixelsBgra.Length < requiredLength)
        {
            throw new ArgumentException("离屏 UI surface 源像素长度小于宽高乘积。", nameof(pixelsBgra));
        }
    }
}
