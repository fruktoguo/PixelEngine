using Hexa.NET.ImGui;
using PixelEngine.Interop;
using PixelEngine.Rendering;
using Silk.NET.Input;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PixelEngine.Gui;

/// <summary>
/// Dear ImGui 的平台侧桥接：同步系统鼠标光标、键盘导航光标回写与 Windows IME 候选窗位置。
/// Renderer 只负责 draw data；这些职责必须由 platform backend 显式实现。
/// </summary>
public sealed unsafe class ImGuiPlatformBridge : IDisposable
{
    private readonly RenderWindow _window;
    private readonly PlatformSetImeDataFn _setImeData;
    private readonly WindowsImeContextController _imeContext = new(WindowsImeContextRegistry.Shared);
    private ImGuiMouseCursor _lastMouseCursor = ImGuiMouseCursor.Count;
    private bool _lastCursorHidden;
    private bool _attached;
    private bool _disposed;

    /// <summary>
    /// 创建复用指定引擎窗口与输入上下文的平台桥。
    /// </summary>
    /// <param name="window">拥有当前 ImGui context 的渲染窗口。</param>
    public ImGuiPlatformBridge(RenderWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _setImeData = SetImeData;
    }

    /// <summary>
    /// 把平台能力注册到当前 ImGui context。
    /// </summary>
    public void Attach()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_attached)
        {
            throw new InvalidOperationException("ImGui platform bridge 已经注册。");
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (_window.Input.Mice.Count > 0)
        {
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors | ImGuiBackendFlags.HasSetMousePos;
        }

        if (OperatingSystem.IsWindows() && _window.TryGetWin32WindowHandle(out IntPtr hwnd))
        {
            ImGuiPlatformIOPtr platform = ImGui.GetPlatformIO();
            platform.PlatformSetImeDataFn = (void*)Marshal.GetFunctionPointerForDelegate(_setImeData);
            _imeContext.Attach(hwnd);
        }

        _attached = true;
    }

    /// <summary>
    /// 在 <see cref="ImGui.NewFrame" /> 前处理上一帧请求的系统光标形状与键盘导航鼠标定位。
    /// </summary>
    public void NewFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_attached)
        {
            throw new InvalidOperationException("ImGui platform bridge 尚未注册。");
        }

        if (_window.Input.Mice.Count == 0)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        IMouse mouse = _window.Input.Mice[0];
        if (io.WantSetMousePos && IsFinite(io.MousePos))
        {
            mouse.Position = new Vector2(
                io.MousePos.X / _window.FramebufferScaleX,
                io.MousePos.Y / _window.FramebufferScaleY);
        }

        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
        {
            return;
        }

        ImGuiMouseCursor requested = ImGui.GetMouseCursor();
        bool hidden = io.MouseDrawCursor || requested == ImGuiMouseCursor.None;
        if (requested == _lastMouseCursor && hidden == _lastCursorHidden)
        {
            return;
        }

        ApplyCursor(mouse.Cursor, requested, hidden);
        _lastMouseCursor = requested;
        _lastCursorHidden = hidden;
    }

    /// <summary>
    /// 同步宿主窗口焦点；失焦时立即取消并暂挂当前 IME context，重新聚焦后仅在文本项仍请求输入时恢复。
    /// </summary>
    /// <param name="focused">窗口当前是否拥有键盘焦点。</param>
    public void SetWindowFocused(bool focused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _imeContext.SetFocused(focused);
    }

    /// <summary>
    /// 从当前 ImGui context 移除平台回调，并恢复标准系统光标。
    /// </summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        io.BackendFlags &= ~(ImGuiBackendFlags.HasMouseCursors | ImGuiBackendFlags.HasSetMousePos);
        if (OperatingSystem.IsWindows())
        {
            _imeContext.Detach();
            ImGuiPlatformIOPtr platform = ImGui.GetPlatformIO();
            platform.PlatformSetImeDataFn = null;
        }

        if (_window.Input.Mice.Count > 0)
        {
            ApplyCursor(_window.Input.Mice[0].Cursor, ImGuiMouseCursor.Arrow, hidden: false);
        }

        _lastMouseCursor = ImGuiMouseCursor.Count;
        _lastCursorHidden = false;
        _attached = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Detach();
        _disposed = true;
    }

    internal static StandardCursor MapCursor(ImGuiMouseCursor cursor)
    {
        return cursor switch
        {
            ImGuiMouseCursor.None or ImGuiMouseCursor.Arrow or ImGuiMouseCursor.Count => StandardCursor.Arrow,
            ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
            ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
            ImGuiMouseCursor.ResizeNs => StandardCursor.VResize,
            ImGuiMouseCursor.ResizeEw => StandardCursor.HResize,
            ImGuiMouseCursor.ResizeNesw => StandardCursor.NeswResize,
            ImGuiMouseCursor.ResizeNwse => StandardCursor.NwseResize,
            ImGuiMouseCursor.Hand => StandardCursor.Hand,
            ImGuiMouseCursor.Wait => StandardCursor.Wait,
            ImGuiMouseCursor.Progress => StandardCursor.WaitArrow,
            ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
            _ => StandardCursor.Arrow,
        };
    }

    internal static bool TryCreateImeForms(
        Vector2 inputPosition,
        float inputLineHeight,
        out Win32CompositionForm composition,
        out Win32CandidateForm candidate)
    {
        composition = default;
        candidate = default;
        if (!IsFinite(inputPosition))
        {
            return false;
        }

        int x = ToCoordinate(inputPosition.X);
        int y = ToCoordinate(inputPosition.Y);
        int lineHeight = Math.Max(1, ToCoordinate(
            float.IsFinite(inputLineHeight) && inputLineHeight > 0f
                ? inputLineHeight
                : 1f));
        int bottom = AddSaturated(y, lineHeight);
        composition = new Win32CompositionForm
        {
            Style = Win32ImeNative.CompositionFormStyleForcePosition,
            CurrentPos = new Win32Point { X = x, Y = y },
        };
        candidate = new Win32CandidateForm
        {
            Index = 0,
            Style = Win32ImeNative.CandidateFormStyleCandidatePos | Win32ImeNative.CandidateFormStyleExclude,
            CurrentPos = new Win32Point { X = x, Y = bottom },
            Area = new Win32Rect
            {
                Left = x,
                Top = y,
                Right = AddSaturated(x, 1),
                Bottom = bottom,
            },
        };
        return true;
    }

    private static void ApplyCursor(ICursor cursor, ImGuiMouseCursor requested, bool hidden)
    {
        CursorMode mode = hidden ? CursorMode.Hidden : CursorMode.Normal;
        if (cursor.IsSupported(mode))
        {
            cursor.CursorMode = mode;
        }

        if (hidden)
        {
            return;
        }

        StandardCursor standard = MapCursor(requested);
        if (!cursor.IsSupported(standard))
        {
            standard = StandardCursor.Arrow;
        }

        cursor.Type = CursorType.Standard;
        cursor.StandardCursor = standard;
    }

    private void SetImeData(
        ImGuiContext* context,
        ImGuiViewport* viewport,
        ImGuiPlatformImeData* data)
    {
        _ = context;
        _ = viewport;
        if (data is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        bool visible = data->WantVisible != 0;
        bool wantsTextInput = data->WantTextInput != 0;
        Win32CompositionForm composition = default;
        Win32CandidateForm candidate = default;
        bool hasForms = visible && TryCreateImeForms(
            data->InputPos,
            data->InputLineHeight,
            out composition,
            out candidate);
        _imeContext.UpdateRequest(
            wantsTextInput,
            visible,
            hasForms,
            in composition,
            in candidate);
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private static int ToCoordinate(float value)
    {
        return value >= int.MaxValue
            ? int.MaxValue
            : value <= int.MinValue
                ? int.MinValue
                : (int)MathF.Round(value);
    }

    private static int AddSaturated(int value, int delta)
    {
        long result = (long)value + delta;
        return (int)Math.Clamp(result, int.MinValue, int.MaxValue);
    }

}
