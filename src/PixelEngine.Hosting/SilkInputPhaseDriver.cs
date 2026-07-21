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
/// <param name="window">输入所属渲染窗口。</param>
/// <param name="input">脚本输入 API。</param>
/// <param name="routeProvider">可选输入仲裁路由。</param>
/// <param name="logicalViewportWidth">逻辑 viewport 宽度；0 表示直接使用 framebuffer 坐标。</param>
/// <param name="logicalViewportHeight">逻辑 viewport 高度；0 表示直接使用 framebuffer 坐标。</param>
/// <param name="gameplayViewportMapper">可选嵌入式 gameplay viewport 映射器。</param>
public sealed class SilkInputPhaseDriver(
    RenderWindow window,
    ScriptInputApi input,
    Func<EngineTickContext, ScriptInputRoute>? routeProvider,
    int logicalViewportWidth,
    int logicalViewportHeight,
    IGameplayViewportInputMapper? gameplayViewportMapper) : IEnginePhaseDriver
{
    private readonly RenderWindow _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly ScriptInputApi _input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly Func<EngineTickContext, ScriptInputRoute> _routeProvider = routeProvider ?? (_ => ScriptInputRoute.Allowed);
    private readonly int _logicalViewportWidth = logicalViewportWidth;
    private readonly int _logicalViewportHeight = logicalViewportHeight;
    private readonly IGameplayViewportInputMapper? _gameplayViewportMapper = gameplayViewportMapper;
    private readonly ScriptKey[] _keyBuffer = new ScriptKey[22];
    private readonly ScriptMouseButton[] _mouseBuffer = new ScriptMouseButton[3];
    private float _lastWheelY;

    /// <summary>
    /// 使用独立窗口坐标映射创建输入驱动；保留既有五参数 CLR 构造器的二进制兼容性。
    /// </summary>
    /// <param name="window">输入所属渲染窗口。</param>
    /// <param name="input">脚本输入 API。</param>
    /// <param name="routeProvider">可选输入仲裁路由。</param>
    /// <param name="logicalViewportWidth">逻辑 viewport 宽度；0 表示直接使用 framebuffer 坐标。</param>
    /// <param name="logicalViewportHeight">逻辑 viewport 高度；0 表示直接使用 framebuffer 坐标。</param>
    public SilkInputPhaseDriver(
        RenderWindow window,
        ScriptInputApi input,
        Func<EngineTickContext, ScriptInputRoute>? routeProvider = null,
        int logicalViewportWidth = 0,
        int logicalViewportHeight = 0)
        : this(window, input, routeProvider, logicalViewportWidth, logicalViewportHeight, gameplayViewportMapper: null)
    {
    }

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
        (float x, float y, float wheelY, bool pointerMapped) = CaptureMouseState(route.AllowMouse);
        bool allowMouse = route.AllowMouse && pointerMapped;
        int buttonCount = allowMouse ? CaptureMouseButtons(_mouseBuffer) : 0;
        ScriptInputSnapshotBuilder.Update(
            _input,
            _keyBuffer.AsSpan(0, keyCount),
            _mouseBuffer.AsSpan(0, buttonCount),
            x,
            y,
            wheelY,
            route.AllowKeyboard,
            allowMouse);
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
        AddIfDown(keyboard, SilkKey.B, ScriptKey.B, destination, ref count);
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

    private (float X, float Y, float WheelY, bool PointerMapped) CaptureMouseState(bool allowMouse)
    {
        if (_window.Input.Mice.Count == 0)
        {
            return (0f, 0f, 0f, _gameplayViewportMapper is null);
        }

        IMouse mouse = _window.Input.Mice[0];
        float currentWheelY = mouse.ScrollWheels.Count == 0 ? 0f : mouse.ScrollWheels[0].Y;
        float wheelDelta = allowMouse ? currentWheelY - _lastWheelY : 0f;
        _lastWheelY = currentWheelY;
        if (!allowMouse)
        {
            return (0f, 0f, 0f, false);
        }

        float framebufferX = mouse.Position.X * _window.FramebufferScaleX;
        float framebufferY = mouse.Position.Y * _window.FramebufferScaleY;
        // 嵌入式宿主（Editor Game View）拥有 runtime viewport 的真实图像矩形；玩法与 Game UI
        // 必须复用当前窗口事件坐标的同一映射，不能依赖上一帧 panel hover 快照。
        if (_gameplayViewportMapper is not null)
        {
            return TryMapGameplayViewportPointer(
                    _gameplayViewportMapper,
                    framebufferX,
                    framebufferY,
                    out float viewportX,
                    out float viewportY)
                    ? (viewportX, viewportY, wheelDelta, true)
                    : (0f, 0f, 0f, false);
        }

        // 逻辑视口与帧缓冲不一致时，将鼠标坐标映射回 sim 内部分辨率空间。
        if (_logicalViewportWidth > 0 && _logicalViewportHeight > 0)
        {
            PresentationViewport viewport = PresentationViewport.Fit(
                _logicalViewportWidth,
                _logicalViewportHeight,
                _window.Width,
                _window.Height);
            if (!ContainsFramebufferPoint(in viewport, framebufferX, framebufferY))
            {
                return (0f, 0f, 0f, false);
            }

            (float sourceX, float sourceY) = viewport.MapFramebufferToSource(framebufferX, framebufferY);
            return (sourceX, sourceY, wheelDelta, true);
        }

        return (
            framebufferX,
            framebufferY,
            wheelDelta,
            true);
    }

    internal static bool ContainsFramebufferPoint(
        in PresentationViewport viewport,
        float framebufferX,
        float framebufferY)
    {
        if (!float.IsFinite(framebufferX) || !float.IsFinite(framebufferY))
        {
            return false;
        }

        float top = viewport.TargetHeight - viewport.Y - viewport.Height;
        return framebufferX >= viewport.X &&
            framebufferY >= top &&
            framebufferX < viewport.X + viewport.Width &&
            framebufferY < top + viewport.Height;
    }

    internal static bool TryMapGameplayViewportPointer(
        IGameplayViewportInputMapper mapper,
        out float viewportX,
        out float viewportY)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return ValidateMappedPointer(
            mapper.TryMapPointerToViewport(out viewportX, out viewportY),
            ref viewportX,
            ref viewportY);
    }

    internal static bool TryMapGameplayViewportPointer(
        IGameplayViewportInputMapper mapper,
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return ValidateMappedPointer(
            mapper.TryMapFramebufferPointerToViewport(
                framebufferX,
                framebufferY,
                out viewportX,
                out viewportY),
            ref viewportX,
            ref viewportY);
    }

    private static bool ValidateMappedPointer(bool mapped, ref float viewportX, ref float viewportY)
    {
        if (mapped && float.IsFinite(viewportX) && float.IsFinite(viewportY))
        {
            return true;
        }

        viewportX = 0f;
        viewportY = 0f;
        return false;
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
