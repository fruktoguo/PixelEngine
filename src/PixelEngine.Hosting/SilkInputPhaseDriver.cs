using PixelEngine.Rendering;
using PixelEngine.Scripting;
using Silk.NET.Input;
using ScriptKey = PixelEngine.Scripting.Key;
using ScriptMouseButton = PixelEngine.Scripting.MouseButton;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace PixelEngine.Hosting;

/// <summary>
/// 从 Silk.NET 窗口输入上下文采样键鼠状态，并在相位 0 写入脚本输入快照。
/// </summary>
public sealed class SilkInputPhaseDriver(
    RenderWindow window,
    ScriptInputApi input,
    Func<EngineTickContext, ScriptInputRoute>? routeProvider = null,
    int logicalViewportWidth = 0,
    int logicalViewportHeight = 0) : IEnginePhaseDriver
{
    private readonly RenderWindow _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly ScriptInputApi _input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly Func<EngineTickContext, ScriptInputRoute> _routeProvider = routeProvider ?? (_ => ScriptInputRoute.Allowed);
    private readonly int _logicalViewportWidth = logicalViewportWidth;
    private readonly int _logicalViewportHeight = logicalViewportHeight;
    private readonly ScriptKey[] _keyBuffer = new ScriptKey[20];
    private readonly ScriptMouseButton[] _mouseBuffer = new ScriptMouseButton[3];
    private float _lastWheelY;

    /// <summary>
    /// 注册输入采样相位。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.InputAndTime, CaptureInput);
    }

    // 相位 0：泵送窗口事件，按输入仲裁路由采样键鼠并写入脚本输入快照。
    private void CaptureInput(EngineTickContext context)
    {
        _window.DoEvents();
        ScriptInputRoute route = _routeProvider(context);
        int keyCount = route.AllowKeyboard ? CaptureKeys(_keyBuffer) : 0;
        int buttonCount = route.AllowMouse ? CaptureMouseButtons(_mouseBuffer) : 0;
        (float x, float y, float wheelY) = CaptureMouseState(route.AllowMouse);
        ScriptInputSnapshotBuilder.Update(
            _input,
            _keyBuffer.AsSpan(0, keyCount),
            _mouseBuffer.AsSpan(0, buttonCount),
            x,
            y,
            wheelY,
            route.AllowKeyboard,
            route.AllowMouse);
    }

    private int CaptureKeys(Span<ScriptKey> destination)
    {
        if (_window.Input.Keyboards.Count == 0)
        {
            return 0;
        }

        IKeyboard keyboard = _window.Input.Keyboards[0];
        int count = 0;
        AddIfDown(keyboard, SilkKey.A, ScriptKey.A, destination, ref count);
        AddIfDown(keyboard, SilkKey.D, ScriptKey.D, destination, ref count);
        AddIfDown(keyboard, SilkKey.W, ScriptKey.W, destination, ref count);
        AddIfDown(keyboard, SilkKey.S, ScriptKey.S, destination, ref count);
        AddIfDown(keyboard, SilkKey.R, ScriptKey.R, destination, ref count);
        AddIfDown(keyboard, SilkKey.Left, ScriptKey.Left, destination, ref count);
        AddIfDown(keyboard, SilkKey.Right, ScriptKey.Right, destination, ref count);
        AddIfDown(keyboard, SilkKey.Up, ScriptKey.Up, destination, ref count);
        AddIfDown(keyboard, SilkKey.Down, ScriptKey.Down, destination, ref count);
        AddIfDown(keyboard, SilkKey.Space, ScriptKey.Space, destination, ref count);
        AddIfDown(keyboard, SilkKey.Escape, ScriptKey.Escape, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number0, ScriptKey.Digit0, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number1, ScriptKey.Digit1, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number2, ScriptKey.Digit2, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number3, ScriptKey.Digit3, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number4, ScriptKey.Digit4, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number5, ScriptKey.Digit5, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number6, ScriptKey.Digit6, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number7, ScriptKey.Digit7, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number8, ScriptKey.Digit8, destination, ref count);
        AddIfDown(keyboard, SilkKey.Number9, ScriptKey.Digit9, destination, ref count);
        return count;
    }

    private int CaptureMouseButtons(Span<ScriptMouseButton> destination)
    {
        if (_window.Input.Mice.Count == 0)
        {
            return 0;
        }

        IMouse mouse = _window.Input.Mice[0];
        int count = 0;
        AddIfDown(mouse, SilkMouseButton.Left, ScriptMouseButton.Left, destination, ref count);
        AddIfDown(mouse, SilkMouseButton.Right, ScriptMouseButton.Right, destination, ref count);
        AddIfDown(mouse, SilkMouseButton.Middle, ScriptMouseButton.Middle, destination, ref count);
        return count;
    }

    private (float X, float Y, float WheelY) CaptureMouseState(bool allowMouse)
    {
        if (_window.Input.Mice.Count == 0)
        {
            return (0f, 0f, 0f);
        }

        IMouse mouse = _window.Input.Mice[0];
        float currentWheelY = mouse.ScrollWheels.Count == 0 ? 0f : mouse.ScrollWheels[0].Y;
        float wheelDelta = allowMouse ? currentWheelY - _lastWheelY : 0f;
        _lastWheelY = currentWheelY;
        float framebufferX = mouse.Position.X * _window.FramebufferScaleX;
        float framebufferY = mouse.Position.Y * _window.FramebufferScaleY;
        // 逻辑视口与帧缓冲不一致时，将鼠标坐标映射回 sim 内部分辨率空间。
        if (_logicalViewportWidth > 0 && _logicalViewportHeight > 0)
        {
            PresentationViewport viewport = PresentationViewport.Fit(
                _logicalViewportWidth,
                _logicalViewportHeight,
                _window.Width,
                _window.Height);
            (float sourceX, float sourceY) = viewport.MapFramebufferToSource(framebufferX, framebufferY);
            return (sourceX, sourceY, wheelDelta);
        }

        return (
            framebufferX,
            framebufferY,
            wheelDelta);
    }

    private static void AddIfDown(IKeyboard keyboard, SilkKey source, ScriptKey target, Span<ScriptKey> destination, ref int count)
    {
        if (keyboard.IsKeyPressed(source))
        {
            destination[count++] = target;
        }
    }

    private static void AddIfDown(IMouse mouse, SilkMouseButton source, ScriptMouseButton target, Span<ScriptMouseButton> destination, ref int count)
    {
        if (mouse.IsButtonPressed(source))
        {
            destination[count++] = target;
        }
    }
}

/// <summary>
/// 脚本输入通道门控结果。
/// </summary>
/// <param name="AllowKeyboard">脚本是否可消费键盘。</param>
/// <param name="AllowMouse">脚本是否可消费鼠标。</param>
public readonly record struct ScriptInputRoute(bool AllowKeyboard, bool AllowMouse)
{
    /// <summary>
    /// 键盘与鼠标都允许进入脚本。
    /// </summary>
    public static ScriptInputRoute Allowed { get; } = new(AllowKeyboard: true, AllowMouse: true);
}
