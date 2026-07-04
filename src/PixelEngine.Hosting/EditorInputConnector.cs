using PixelEngine.Gui;
using PixelEngine.Rendering;
using Silk.NET.Input;
using System.Numerics;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Silk.NET 窗口输入事件转发到 ImGui 输入桥。
/// </summary>
internal sealed class EditorInputConnector : IDisposable
{
    private readonly RenderWindow _window;
    private readonly GuiInputBridge _input;
    private bool _disposed;

    /// <summary>
    /// 创建输入连接器，并订阅当前窗口已有的键盘与鼠标设备。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <param name="input">ImGui 输入桥。</param>
    public EditorInputConnector(RenderWindow window, GuiInputBridge input)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Subscribe();
    }

    /// <summary>
    /// 取消事件订阅，停止向 ImGui 输入桥转发窗口输入。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

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
        _input.Key(key, down: true);
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        _ = keyboard;
        _ = scanCode;
        _input.Key(key, down: false);
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        _ = keyboard;
        _input.Text(character.ToString());
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        _ = mouse;
        _input.MouseMove(position.X, position.Y);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        _ = mouse;
        _input.MouseButton(button, down: true);
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        _ = mouse;
        _input.MouseButton(button, down: false);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        _ = mouse;
        _input.MouseWheel(wheel.X, wheel.Y);
    }
}
