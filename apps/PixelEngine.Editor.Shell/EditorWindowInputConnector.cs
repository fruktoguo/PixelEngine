using PixelEngine.Rendering;
using Silk.NET.Input;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 将 Silk 窗口输入连接到 Editor 与 Game View 输入路由。
/// </summary>
internal sealed class EditorWindowInputConnector : IDisposable
{
    private readonly RenderWindow _window;
    private readonly ImGuiInputBridge _input;
    private bool _disposed;

    public EditorWindowInputConnector(RenderWindow window, ImGuiInputBridge input)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Subscribe();
    }

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

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        ForwardMousePosition(mouse.Position);
        _input.MouseButton(button, down: true);
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
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
        _input.MouseMoveFramebuffer(
            position.X * _window.FramebufferScaleX,
            position.Y * _window.FramebufferScaleY);
    }
}
