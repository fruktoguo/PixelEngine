using PixelEngine.Rendering;
using Silk.NET.Input;
using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 将 Silk.NET 窗口输入事件转发到中性 GUI 输入桥。
/// </summary>
public sealed class GuiWindowInputConnector : IDisposable
{
    private const float MouseUnavailable = -float.MaxValue;

    private readonly RenderWindow _window;
    private readonly GuiInputBridge _input;
    private readonly IGuiViewportInputRoute? _viewportRoute;
    private readonly HashSet<Key> _forwardedKeys = new(64);
    private int _forwardedMouseButtons;
    private bool _disposed;

    /// <summary>
    /// 创建输入连接器，并订阅当前窗口已有的键盘与鼠标设备。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <param name="input">GUI 输入桥。</param>
    public GuiWindowInputConnector(RenderWindow window, GuiInputBridge input)
        : this(window, input, viewportRoute: null)
    {
    }

    /// <summary>
    /// 创建输入连接器，并允许嵌入式宿主把窗口输入裁剪、映射到 runtime GUI viewport。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <param name="input">GUI 输入桥。</param>
    /// <param name="viewportRoute">可选嵌入式 viewport 路由；为空时保持独立 Player 的整窗输入行为。</param>
    public GuiWindowInputConnector(
        RenderWindow window,
        GuiInputBridge input,
        IGuiViewportInputRoute? viewportRoute)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _viewportRoute = viewportRoute;
        Subscribe();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseRoutedInput();
        Unsubscribe();
        _disposed = true;
    }

    private void Subscribe()
    {
        for (int i = 0; i < _window.Input.Keyboards.Count; i++)
        {
            IKeyboard keyboard = _window.Input.Keyboards[i];
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        for (int i = 0; i < _window.Input.Mice.Count; i++)
        {
            IMouse mouse = _window.Input.Mice[i];
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnScroll;
        }
    }

    private void Unsubscribe()
    {
        for (int i = 0; i < _window.Input.Keyboards.Count; i++)
        {
            IKeyboard keyboard = _window.Input.Keyboards[i];
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
            keyboard.KeyChar -= OnKeyChar;
        }

        for (int i = 0; i < _window.Input.Mice.Count; i++)
        {
            IMouse mouse = _window.Input.Mice[i];
            mouse.MouseMove -= OnMouseMove;
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.Scroll -= OnScroll;
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        _ = keyboard;
        _ = scanCode;
        if (_viewportRoute is not null && !_viewportRoute.AllowsKeyboardInput)
        {
            ReleaseForwardedKeys();
            return;
        }

        _input.Key(key, down: true);
        if (_viewportRoute is not null)
        {
            _ = _forwardedKeys.Add(key);
        }
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        _ = keyboard;
        _ = scanCode;
        if (_viewportRoute is null || _forwardedKeys.Remove(key))
        {
            _input.Key(key, down: false);
        }
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        _ = keyboard;
        if (_viewportRoute is null || _viewportRoute.AllowsKeyboardInput)
        {
            _input.Text(character.ToString());
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        _ = mouse;
        _ = ForwardMousePosition(position);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        SynchronizeKeyboardRoute();
        if (!ForwardMousePosition(mouse.Position))
        {
            return;
        }

        _input.MouseButton(button, down: true);
        if (_viewportRoute is not null)
        {
            _forwardedMouseButtons |= MouseButtonMask(button);
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        _ = ForwardMousePosition(mouse.Position);
        int mask = MouseButtonMask(button);
        if (_viewportRoute is null || (_forwardedMouseButtons & mask) != 0)
        {
            _input.MouseButton(button, down: false);
            _forwardedMouseButtons &= ~mask;
        }
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (_viewportRoute is null)
        {
            _input.MouseWheel(wheel.X, wheel.Y);
            return;
        }

        SynchronizeKeyboardRoute();
        if (ForwardMousePosition(mouse.Position))
        {
            _input.MouseWheel(wheel.X, wheel.Y);
        }
    }

    // Silk 指针先转为窗口 framebuffer；嵌入模式再经 Game View 的 DPI/letterbox 契约映射到 runtime FBO。
    private bool ForwardMousePosition(Vector2 position)
    {
        float framebufferX = position.X * _window.FramebufferScaleX;
        float framebufferY = position.Y * _window.FramebufferScaleY;
        if (TryResolvePointer(
                _viewportRoute,
                framebufferX,
                framebufferY,
                out float viewportX,
                out float viewportY))
        {
            _input.MouseMoveFramebuffer(viewportX, viewportY);
            return true;
        }

        // 显式把 ImGui 指针移出 viewport，避免离开 Game View 后仍 hover/点击上一帧控件。
        _input.MouseMoveFramebuffer(MouseUnavailable, MouseUnavailable);
        return false;
    }

    internal static bool TryResolvePointer(
        IGuiViewportInputRoute? route,
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY)
    {
        if (route is null)
        {
            viewportX = framebufferX;
            viewportY = framebufferY;
            return float.IsFinite(viewportX) && float.IsFinite(viewportY);
        }

        if (route.TryMapPointer(framebufferX, framebufferY, out viewportX, out viewportY) &&
            float.IsFinite(viewportX) &&
            float.IsFinite(viewportY))
        {
            return true;
        }

        viewportX = 0f;
        viewportY = 0f;
        return false;
    }

    private void SynchronizeKeyboardRoute()
    {
        if (_viewportRoute is not null && !_viewportRoute.AllowsKeyboardInput)
        {
            ReleaseForwardedKeys();
        }
    }

    private void ReleaseRoutedInput()
    {
        if (_viewportRoute is null)
        {
            return;
        }

        ReleaseForwardedKeys();
        ReleaseMouseButton(MouseButton.Left);
        ReleaseMouseButton(MouseButton.Right);
        ReleaseMouseButton(MouseButton.Middle);
    }

    private void ReleaseForwardedKeys()
    {
        foreach (Key key in _forwardedKeys)
        {
            _input.Key(key, down: false);
        }

        _forwardedKeys.Clear();
    }

    private void ReleaseMouseButton(MouseButton button)
    {
        int mask = MouseButtonMask(button);
        if ((_forwardedMouseButtons & mask) == 0)
        {
            return;
        }

        _input.MouseButton(button, down: false);
        _forwardedMouseButtons &= ~mask;
    }

    private static int MouseButtonMask(MouseButton button)
    {
#pragma warning disable IDE0072
        return button switch
        {
            MouseButton.Left => 1 << 0,
            MouseButton.Right => 1 << 1,
            MouseButton.Middle => 1 << 2,
            _ => 0,
        };
#pragma warning restore IDE0072
    }
}
