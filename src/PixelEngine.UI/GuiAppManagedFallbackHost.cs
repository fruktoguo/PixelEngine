using PixelEngine.Gui;
using PixelEngine.Rendering;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace PixelEngine.UI;

/// <summary>
/// 把 <see cref="GuiApp" /> 适配为 ManagedFallbackBackend 可复用的绘制宿主。
/// </summary>
public sealed class GuiAppManagedFallbackHost(GuiApp gui, RenderWindow? window) : IManagedFallbackGuiHost, IDisposable
{
    private readonly GuiApp _gui = gui ?? throw new ArgumentNullException(nameof(gui));
    private readonly RenderWindow? _window = window;
    private ImageEntry[] _images = new ImageEntry[8];
    private int _imageCount;
    private bool _shiftDown;
    private bool _controlDown;
    private bool _altDown;

    /// <summary>
    /// 创建不带图片上传能力的 GUI host 适配器。
    /// </summary>
    /// <param name="gui">共享 GUI 应用。</param>
    public GuiAppManagedFallbackHost(GuiApp gui)
        : this(gui, window: null)
    {
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

        // 解码 PNG 并上传到当前共享 GL context；UnpackAlignment=1 兼容非 4 字节对齐行宽。
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
    /// 将 presentation 指针位置写入共享 Gui 输入队列。
    /// </summary>
    /// <param name="x">presentation X。</param>
    /// <param name="y">presentation Y。</param>
    public void FeedPointerMove(float x, float y)
    {
        _gui.Input.MouseMoveFramebuffer(x, y);
    }

    /// <summary>
    /// 将规范化指针按钮边沿写入共享 Gui 输入队列。
    /// </summary>
    /// <param name="button">指针按钮。</param>
    /// <param name="isDown">是否按下。</param>
    public void FeedPointerButton(UiPointerButton button, bool isDown)
    {
        MouseButton mapped = button switch
        {
            UiPointerButton.Left => MouseButton.Left,
            UiPointerButton.Right => MouseButton.Right,
            UiPointerButton.Middle => MouseButton.Middle,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "未知 UI 指针按钮。"),
        };
        _gui.Input.MouseButton(mapped, isDown);
    }

    /// <summary>
    /// 将 presentation 滚轮增量写入共享 Gui 输入队列。
    /// </summary>
    /// <param name="deltaX">水平增量。</param>
    /// <param name="deltaY">垂直增量。</param>
    public void FeedScroll(float deltaX, float deltaY)
    {
        _gui.Input.MouseWheel(deltaX, deltaY);
    }

    /// <summary>
    /// 将规范化按键边沿及修饰键状态写入共享 Gui 输入队列。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <param name="isDown">是否按下。</param>
    /// <param name="modifiers">修饰键。</param>
    public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
    {
        SynchronizeModifier(Key.ShiftLeft, (modifiers & UiKeyModifiers.Shift) != 0, ref _shiftDown);
        SynchronizeModifier(Key.ControlLeft, (modifiers & UiKeyModifiers.Control) != 0, ref _controlDown);
        SynchronizeModifier(Key.AltLeft, (modifiers & UiKeyModifiers.Alt) != 0, ref _altDown);
        _gui.Input.Key((Key)key.Value, isDown);
    }

    /// <summary>
    /// 将已提交文本写入共享 Gui 输入队列。
    /// </summary>
    /// <param name="text">已提交文本。</param>
    public void FeedText(ReadOnlySpan<char> text)
    {
        if (!text.IsEmpty)
        {
            _gui.Input.Text(new string(text));
        }
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

    private void SynchronizeModifier(Key key, bool isDown, ref bool previous)
    {
        if (isDown == previous)
        {
            return;
        }

        _gui.Input.Key(key, isDown);
        previous = isDown;
    }

    private readonly record struct ImageEntry(string Path, GlTexture Texture, ManagedFallbackImage Image);
}
