using PixelEngine.Rendering;
using Silk.NET.Input;
using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 将 Silk.NET 窗口输入事件转发到中性 GUI 输入桥。
/// </summary>
public sealed class GuiWindowInputConnector : IDisposable
{
    private readonly RenderWindow _window;
    private readonly GuiInputBridge _input;
    private bool _disposed;

    /// <summary>
    /// 创建输入连接器，并订阅当前窗口已有的键盘与鼠标设备。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <param name="input">GUI 输入桥。</param>
    public GuiWindowInputConnector(RenderWindow window, GuiInputBridge input)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Subscribe();
    }

    /// <inheritdoc />
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
        ForwardMousePosition(position);
    }

    private void OnMouseDown(IMouse mouse, Silk.NET.Input.MouseButton button)
    {
        ForwardMousePosition(mouse.Position);
        _input.MouseButton(button, down: true);
    }

    private void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button)
    {
        ForwardMousePosition(mouse.Position);
        _input.MouseButton(button, down: false);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        _ = mouse;
        _input.MouseWheel(wheel.X, wheel.Y);
    }

    private void ForwardMousePosition(Vector2 position)
    {
        _input.MouseMove(position.X, position.Y);
    }
}
