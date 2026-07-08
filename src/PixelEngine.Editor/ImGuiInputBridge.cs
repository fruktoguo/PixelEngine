using Hexa.NET.ImGui;
using Silk.NET.Input;

namespace PixelEngine.Editor;

#pragma warning disable IDE0010, IDE0046, IDE0072, IDE0290

/// <summary>
/// Silk.NET 输入到 ImGuiIO 的桥接器，并发布输入捕获仲裁状态。
/// </summary>
public sealed class ImGuiInputBridge
{
    private readonly IEditorImGuiBackend _backend;
    private bool _leftCtrlDown;
    private bool _rightCtrlDown;
    private bool _leftShiftDown;
    private bool _rightShiftDown;
    private bool _leftAltDown;
    private bool _rightAltDown;

    /// <summary>
    /// 创建输入桥。
    /// </summary>
    /// <param name="backend">ImGui 后端。</param>
    public ImGuiInputBridge(IEditorImGuiBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// 当前输入捕获状态。
    /// </summary>
    public EditorInputSnapshot Capture => _backend.Capture;

    /// <summary>
    /// 注入鼠标位置。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    public void MouseMove(float x, float y)
    {
        _backend.AddMousePosition(x, y);
    }

    /// <summary>
    /// 注入鼠标按键。
    /// </summary>
    /// <param name="button">Silk.NET 鼠标键。</param>
    /// <param name="down">是否按下。</param>
    public void MouseButton(MouseButton button, bool down)
    {
        int imguiButton = -1;
        if (button == Silk.NET.Input.MouseButton.Left)
        {
            imguiButton = 0;
        }
        else if (button == Silk.NET.Input.MouseButton.Right)
        {
            imguiButton = 1;
        }
        else if (button == Silk.NET.Input.MouseButton.Middle)
        {
            imguiButton = 2;
        }

        if (imguiButton >= 0)
        {
            _backend.AddMouseButton(imguiButton, down);
        }
    }

    /// <summary>
    /// 注入鼠标滚轮。
    /// </summary>
    /// <param name="x">水平滚轮。</param>
    /// <param name="y">垂直滚轮。</param>
    public void MouseWheel(float x, float y)
    {
        _backend.AddMouseWheel(x, y);
    }

    /// <summary>
    /// 注入键盘按键。
    /// </summary>
    /// <param name="key">Silk.NET 键码。</param>
    /// <param name="down">是否按下。</param>
    public void Key(Key key, bool down)
    {
        ImGuiKey imguiKey = MapKey(key);
        if (imguiKey != ImGuiKey.None)
        {
            _backend.AddKey(imguiKey, down);
            UpdateModifierState(key, down);
        }
    }

    /// <summary>
    /// 注入文本输入。
    /// </summary>
    /// <param name="text">输入文本。</param>
    public void Text(string text)
    {
        _backend.AddText(text);
    }

    private static ImGuiKey MapKey(Key key)
    {
        if (key == Silk.NET.Input.Key.Tab)
        {
            return ImGuiKey.Tab;
        }

        return key switch
        {
            Silk.NET.Input.Key.Left => ImGuiKey.LeftArrow,
            Silk.NET.Input.Key.Right => ImGuiKey.RightArrow,
            Silk.NET.Input.Key.Up => ImGuiKey.UpArrow,
            Silk.NET.Input.Key.Down => ImGuiKey.DownArrow,
            Silk.NET.Input.Key.PageUp => ImGuiKey.PageUp,
            Silk.NET.Input.Key.PageDown => ImGuiKey.PageDown,
            Silk.NET.Input.Key.Home => ImGuiKey.Home,
            Silk.NET.Input.Key.End => ImGuiKey.End,
            Silk.NET.Input.Key.Insert => ImGuiKey.Insert,
            Silk.NET.Input.Key.Delete => ImGuiKey.Delete,
            Silk.NET.Input.Key.Backspace => ImGuiKey.Backspace,
            Silk.NET.Input.Key.Space => ImGuiKey.Space,
            Silk.NET.Input.Key.Enter => ImGuiKey.Enter,
            Silk.NET.Input.Key.Escape => ImGuiKey.Escape,
            Silk.NET.Input.Key.ControlLeft => ImGuiKey.LeftCtrl,
            Silk.NET.Input.Key.ShiftLeft => ImGuiKey.LeftShift,
            Silk.NET.Input.Key.AltLeft => ImGuiKey.LeftAlt,
            Silk.NET.Input.Key.ControlRight => ImGuiKey.RightCtrl,
            Silk.NET.Input.Key.ShiftRight => ImGuiKey.RightShift,
            Silk.NET.Input.Key.AltRight => ImGuiKey.RightAlt,
            Silk.NET.Input.Key.A => ImGuiKey.A,
            Silk.NET.Input.Key.B => ImGuiKey.B,
            Silk.NET.Input.Key.C => ImGuiKey.C,
            Silk.NET.Input.Key.D => ImGuiKey.D,
            Silk.NET.Input.Key.E => ImGuiKey.E,
            Silk.NET.Input.Key.F => ImGuiKey.F,
            Silk.NET.Input.Key.G => ImGuiKey.G,
            Silk.NET.Input.Key.H => ImGuiKey.H,
            Silk.NET.Input.Key.I => ImGuiKey.I,
            Silk.NET.Input.Key.J => ImGuiKey.J,
            Silk.NET.Input.Key.K => ImGuiKey.K,
            Silk.NET.Input.Key.L => ImGuiKey.L,
            Silk.NET.Input.Key.M => ImGuiKey.M,
            Silk.NET.Input.Key.N => ImGuiKey.N,
            Silk.NET.Input.Key.O => ImGuiKey.O,
            Silk.NET.Input.Key.P => ImGuiKey.P,
            Silk.NET.Input.Key.Q => ImGuiKey.Q,
            Silk.NET.Input.Key.R => ImGuiKey.R,
            Silk.NET.Input.Key.S => ImGuiKey.S,
            Silk.NET.Input.Key.T => ImGuiKey.T,
            Silk.NET.Input.Key.U => ImGuiKey.U,
            Silk.NET.Input.Key.V => ImGuiKey.V,
            Silk.NET.Input.Key.W => ImGuiKey.W,
            Silk.NET.Input.Key.X => ImGuiKey.X,
            Silk.NET.Input.Key.Y => ImGuiKey.Y,
            Silk.NET.Input.Key.Z => ImGuiKey.Z,
            _ => ImGuiKey.None,
        };
    }

    private void UpdateModifierState(Key key, bool down)
    {
        switch (key)
        {
            case Silk.NET.Input.Key.ControlLeft:
                _leftCtrlDown = down;
                _backend.AddKey(ImGuiKey.ModCtrl, _leftCtrlDown || _rightCtrlDown);
                break;
            case Silk.NET.Input.Key.ControlRight:
                _rightCtrlDown = down;
                _backend.AddKey(ImGuiKey.ModCtrl, _leftCtrlDown || _rightCtrlDown);
                break;
            case Silk.NET.Input.Key.ShiftLeft:
                _leftShiftDown = down;
                _backend.AddKey(ImGuiKey.ModShift, _leftShiftDown || _rightShiftDown);
                break;
            case Silk.NET.Input.Key.ShiftRight:
                _rightShiftDown = down;
                _backend.AddKey(ImGuiKey.ModShift, _leftShiftDown || _rightShiftDown);
                break;
            case Silk.NET.Input.Key.AltLeft:
                _leftAltDown = down;
                _backend.AddKey(ImGuiKey.ModAlt, _leftAltDown || _rightAltDown);
                break;
            case Silk.NET.Input.Key.AltRight:
                _rightAltDown = down;
                _backend.AddKey(ImGuiKey.ModAlt, _leftAltDown || _rightAltDown);
                break;
            default:
                break;
        }
    }
}

#pragma warning restore IDE0010, IDE0046, IDE0072, IDE0290
