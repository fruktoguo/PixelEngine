using PixelEngine.Gui;
using PixelEngine.Rendering;
using Silk.NET.OpenGL;

namespace PixelEngine.UI;

/// <summary>
/// 把 <see cref="GuiApp" /> 适配为 ManagedFallbackBackend 可复用的绘制宿主。
/// </summary>
public sealed class GuiAppManagedFallbackHost : IManagedFallbackGuiHost, IDisposable
{
    private readonly GuiApp _gui;
    private readonly RenderWindow? _window;
    private ImageEntry[] _images = new ImageEntry[8];
    private int _imageCount;

    /// <summary>
    /// 创建不带图片上传能力的 GUI host 适配器。
    /// </summary>
    /// <param name="gui">共享 GUI 应用。</param>
    public GuiAppManagedFallbackHost(GuiApp gui)
        : this(gui, window: null)
    {
    }

    /// <summary>
    /// 创建可把 UI 图片上传到当前渲染窗口 GL 上下文的 GUI host 适配器。
    /// </summary>
    /// <param name="gui">共享 GUI 应用。</param>
    /// <param name="window">当前渲染窗口；为空时不允许绘制图片。</param>
    public GuiAppManagedFallbackHost(GuiApp gui, RenderWindow? window)
    {
        _gui = gui ?? throw new ArgumentNullException(nameof(gui));
        _window = window;
    }

    /// <summary>
    /// 底层 GuiApp 是否已经运行。
    /// </summary>
    public bool IsRunning => _gui.IsRunning;

    /// <summary>
    /// 初始化底层 GuiApp。
    /// </summary>
    public void Initialize()
    {
        _gui.Initialize();
    }

    /// <summary>
    /// 在 GuiApp 的托管绘制帧中执行回调。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt，单位秒。</param>
    /// <param name="width">帧缓冲宽度。</param>
    /// <param name="height">帧缓冲高度。</param>
    /// <param name="drawGui">托管 GUI 绘制回调。</param>
    public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
    {
        _gui.DrawManagedFrame(deltaSeconds, width, height, drawGui);
    }

    /// <summary>
    /// 从磁盘读取 PNG 图片并上传为当前 OpenGL 上下文可采样的 Texture2D；重复路径会复用缓存纹理。
    /// </summary>
    /// <param name="path">图片绝对路径。</param>
    /// <returns>可由 ManagedFallback 绘制的图片资产。</returns>
    public unsafe ManagedFallbackImage LoadImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        for (int i = 0; i < _imageCount; i++)
        {
            if (string.Equals(_images[i].Path, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return _images[i].Image;
            }
        }

        if (_window is null)
        {
            throw new InvalidOperationException("当前 Gui host 未绑定 RenderWindow，无法上传 UI 图片纹理。");
        }

        UiImageBitmap bitmap = UiPngImageLoader.Load(fullPath);
        GlTexture texture = new(_window.Gl, bitmap.Width, bitmap.Height, InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte);
        texture.Bind();
        _window.Gl.GetInteger(GLEnum.UnpackAlignment, out int previousAlignment);
        _window.Gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        try
        {
            fixed (uint* pixels = bitmap.Rgba)
            {
                _window.Gl.TexSubImage2D(
                    TextureTarget.Texture2D,
                    level: 0,
                    xoffset: 0,
                    yoffset: 0,
                    (uint)bitmap.Width,
                    (uint)bitmap.Height,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }
        }
        finally
        {
            _window.Gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment);
        }

        ManagedFallbackImage image = new(texture.Handle, texture.Width, texture.Height);
        EnsureImageCapacity(_imageCount + 1);
        _images[_imageCount++] = new ImageEntry(fullPath, texture, image);
        return image;
    }

    /// <summary>
    /// 释放由该适配器上传并缓存的 UI 图片纹理；不会释放共享的 <see cref="GuiApp" />。
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < _imageCount; i++)
        {
            _images[i].Texture.Dispose();
            _images[i] = default;
        }

        _imageCount = 0;
    }

    private void EnsureImageCapacity(int required)
    {
        if (_images.Length >= required)
        {
            return;
        }

        int capacity = _images.Length * 2;
        while (capacity < required)
        {
            capacity *= 2;
        }

        Array.Resize(ref _images, capacity);
    }

    private readonly record struct ImageEntry(string Path, GlTexture Texture, ManagedFallbackImage Image);
}
