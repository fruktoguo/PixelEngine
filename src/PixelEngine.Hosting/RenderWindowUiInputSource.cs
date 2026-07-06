using PixelEngine.Rendering;
using PixelEngine.UI;
using Silk.NET.Input;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace PixelEngine.Hosting;

internal sealed class RenderWindowUiInputSource : IUiInputSource
{
    private const int TextBufferCapacity = 256;

    private readonly RenderWindow _window;
    private readonly char[] _textBuffer = new char[TextBufferCapacity];
    private int _textRead;
    private int _textCount;
    private float _lastWheelX;
    private float _lastWheelY;

    internal RenderWindowUiInputSource(RenderWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        for (int i = 0; i < _window.Input.Keyboards.Count; i++)
        {
            _window.Input.Keyboards[i].KeyChar += OnKeyChar;
        }
    }

    /// <summary>
    /// 从窗口鼠标设备读取当前指针状态。
    /// </summary>
    /// <param name="state">当前指针状态。</param>
    /// <returns>存在鼠标设备则返回 true。</returns>
    public bool TryGetPointer(out UiPointerState state)
    {
        if (_window.Input.Mice.Count == 0)
        {
            state = default;
            return false;
        }

        IMouse mouse = _window.Input.Mice[0];
        float wheelX = 0f;
        float wheelY = 0f;
        if (mouse.ScrollWheels.Count > 0)
        {
            wheelX = mouse.ScrollWheels[0].X;
            wheelY = mouse.ScrollWheels[0].Y;
        }

        state = new UiPointerState(
            mouse.Position.X * _window.FramebufferScaleX,
            mouse.Position.Y * _window.FramebufferScaleY,
            wheelX - _lastWheelX,
            wheelY - _lastWheelY,
            mouse.IsButtonPressed(SilkMouseButton.Left),
            mouse.IsButtonPressed(SilkMouseButton.Right),
            mouse.IsButtonPressed(SilkMouseButton.Middle));
        _lastWheelX = wheelX;
        _lastWheelY = wheelY;
        return true;
    }

    /// <summary>
    /// 从窗口键盘设备读取当前按下的 UI 按键。
    /// </summary>
    /// <param name="destination">按键写入缓冲。</param>
    /// <param name="modifiers">当前修饰键。</param>
    /// <returns>写入按键数量。</returns>
    public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
    {
        modifiers = UiKeyModifiers.None;
        if (_window.Input.Keyboards.Count == 0)
        {
            return 0;
        }

        IKeyboard keyboard = _window.Input.Keyboards[0];
        modifiers = CaptureModifiers(keyboard);
        int count = 0;
        AddIfDown(keyboard, SilkKey.Tab, destination, ref count);
        AddIfDown(keyboard, SilkKey.Left, destination, ref count);
        AddIfDown(keyboard, SilkKey.Right, destination, ref count);
        AddIfDown(keyboard, SilkKey.Up, destination, ref count);
        AddIfDown(keyboard, SilkKey.Down, destination, ref count);
        AddIfDown(keyboard, SilkKey.PageUp, destination, ref count);
        AddIfDown(keyboard, SilkKey.PageDown, destination, ref count);
        AddIfDown(keyboard, SilkKey.Home, destination, ref count);
        AddIfDown(keyboard, SilkKey.End, destination, ref count);
        AddIfDown(keyboard, SilkKey.Insert, destination, ref count);
        AddIfDown(keyboard, SilkKey.Delete, destination, ref count);
        AddIfDown(keyboard, SilkKey.Backspace, destination, ref count);
        AddIfDown(keyboard, SilkKey.Space, destination, ref count);
        AddIfDown(keyboard, SilkKey.Enter, destination, ref count);
        AddIfDown(keyboard, SilkKey.Escape, destination, ref count);
        AddIfDown(keyboard, SilkKey.A, destination, ref count);
        AddIfDown(keyboard, SilkKey.B, destination, ref count);
        AddIfDown(keyboard, SilkKey.C, destination, ref count);
        AddIfDown(keyboard, SilkKey.D, destination, ref count);
        AddIfDown(keyboard, SilkKey.E, destination, ref count);
        AddIfDown(keyboard, SilkKey.F, destination, ref count);
        AddIfDown(keyboard, SilkKey.G, destination, ref count);
        AddIfDown(keyboard, SilkKey.H, destination, ref count);
        AddIfDown(keyboard, SilkKey.I, destination, ref count);
        AddIfDown(keyboard, SilkKey.J, destination, ref count);
        AddIfDown(keyboard, SilkKey.K, destination, ref count);
        AddIfDown(keyboard, SilkKey.L, destination, ref count);
        AddIfDown(keyboard, SilkKey.M, destination, ref count);
        AddIfDown(keyboard, SilkKey.N, destination, ref count);
        AddIfDown(keyboard, SilkKey.O, destination, ref count);
        AddIfDown(keyboard, SilkKey.P, destination, ref count);
        AddIfDown(keyboard, SilkKey.Q, destination, ref count);
        AddIfDown(keyboard, SilkKey.R, destination, ref count);
        AddIfDown(keyboard, SilkKey.S, destination, ref count);
        AddIfDown(keyboard, SilkKey.T, destination, ref count);
        AddIfDown(keyboard, SilkKey.U, destination, ref count);
        AddIfDown(keyboard, SilkKey.V, destination, ref count);
        AddIfDown(keyboard, SilkKey.W, destination, ref count);
        AddIfDown(keyboard, SilkKey.X, destination, ref count);
        AddIfDown(keyboard, SilkKey.Y, destination, ref count);
        AddIfDown(keyboard, SilkKey.Z, destination, ref count);
        return count;
    }

    /// <summary>
    /// 读取本帧文本输入。
    /// </summary>
    /// <param name="destination">文本写入缓冲。</param>
    /// <returns>写入字符数量。</returns>
    public int CaptureText(Span<char> destination)
    {
        int written = Math.Min(destination.Length, _textCount);
        for (int i = 0; i < written; i++)
        {
            destination[i] = _textBuffer[_textRead];
            _textRead = (_textRead + 1) % _textBuffer.Length;
        }

        _textCount -= written;
        return written;
    }

    /// <summary>
    /// 读取当前平台 IME composition 预编辑文本。Silk.NET 当前窗口输入只暴露 KeyChar committed text，未暴露真实 composition 事件，因此这里显式返回非活动状态，避免把 KeyChar 冒充预编辑文本。
    /// </summary>
    /// <param name="destination">预编辑文本写入缓冲。</param>
    /// <param name="composition">当前预编辑状态。</param>
    /// <returns>写入字符数量。</returns>
    public int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
    {
        _ = destination;
        composition = UiTextComposition.Inactive;
        return 0;
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        _ = keyboard;
        if (character == '\0')
        {
            return;
        }

        if (_textCount == _textBuffer.Length)
        {
            _textRead = (_textRead + 1) % _textBuffer.Length;
            _textCount--;
        }

        int write = (_textRead + _textCount) % _textBuffer.Length;
        _textBuffer[write] = character;
        _textCount++;
    }

    private static UiKeyModifiers CaptureModifiers(IKeyboard keyboard)
    {
        UiKeyModifiers modifiers = UiKeyModifiers.None;
        if (keyboard.IsKeyPressed(SilkKey.ShiftLeft) || keyboard.IsKeyPressed(SilkKey.ShiftRight))
        {
            modifiers |= UiKeyModifiers.Shift;
        }

        if (keyboard.IsKeyPressed(SilkKey.ControlLeft) || keyboard.IsKeyPressed(SilkKey.ControlRight))
        {
            modifiers |= UiKeyModifiers.Control;
        }

        if (keyboard.IsKeyPressed(SilkKey.AltLeft) || keyboard.IsKeyPressed(SilkKey.AltRight))
        {
            modifiers |= UiKeyModifiers.Alt;
        }

        return modifiers;
    }

    private static void AddIfDown(IKeyboard keyboard, SilkKey source, Span<UiKey> destination, ref int count)
    {
        if (count < destination.Length && keyboard.IsKeyPressed(source))
        {
            destination[count++] = new UiKey((int)source);
        }
    }
}
