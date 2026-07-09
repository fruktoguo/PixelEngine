namespace PixelEngine.Scripting;

/// <summary>
/// 脚本输入快照 API；窗口后端每帧写入快照，脚本在相位 1 读取稳定状态。
/// </summary>
public sealed class ScriptInputApi : IInputApi
{
    private const int KeyCount = (int)Key.Digit9 + 1;
    private const int MouseButtonCount = (int)MouseButton.Middle + 1;

    private readonly bool[] _downKeys = new bool[KeyCount];
    private readonly bool[] _previousKeys = new bool[KeyCount];
    private readonly bool[] _pressedKeys = new bool[KeyCount];
    private readonly bool[] _releasedKeys = new bool[KeyCount];
    private readonly bool[] _downButtons = new bool[MouseButtonCount];
    private readonly bool[] _previousButtons = new bool[MouseButtonCount];
    private readonly bool[] _pressedButtons = new bool[MouseButtonCount];
    private readonly bool[] _releasedButtons = new bool[MouseButtonCount];

    /// <summary>
    /// 用窗口层采集到的输入状态更新本帧脚本输入快照。
    /// </summary>
    /// <param name="downKeys">当前按下的键集合。</param>
    /// <param name="downButtons">当前按下的鼠标键集合。</param>
    /// <param name="mouseX">鼠标屏幕 X 坐标。</param>
    /// <param name="mouseY">鼠标屏幕 Y 坐标。</param>
    /// <param name="wheelY">本帧鼠标滚轮纵向增量。</param>
    public void Update(
        ReadOnlySpan<Key> downKeys,
        ReadOnlySpan<MouseButton> downButtons,
        float mouseX,
        float mouseY,
        float wheelY)
    {
        ValidateFinite(mouseX, nameof(mouseX));
        ValidateFinite(mouseY, nameof(mouseY));
        ValidateFinite(wheelY, nameof(wheelY));

        _downKeys.CopyTo(_previousKeys, 0);
        _downButtons.CopyTo(_previousButtons, 0);
        Array.Clear(_downKeys);
        Array.Clear(_downButtons);

        for (int i = 0; i < downKeys.Length; i++)
        {
            _downKeys[Index(downKeys[i])] = true;
        }

        for (int i = 0; i < downButtons.Length; i++)
        {
            _downButtons[Index(downButtons[i])] = true;
        }

        ComputeEdges(_previousKeys, _downKeys, _pressedKeys, _releasedKeys);
        ComputeEdges(_previousButtons, _downButtons, _pressedButtons, _releasedButtons);
        MousePixel = (mouseX, mouseY);
        MouseWheelY = wheelY;
    }

    /// <inheritdoc />
    public bool IsDown(Key key)
    {
        return _downKeys[Index(key)];
    }

    /// <inheritdoc />
    public bool WasPressed(Key key)
    {
        return _pressedKeys[Index(key)];
    }

    /// <inheritdoc />
    public bool WasReleased(Key key)
    {
        return _releasedKeys[Index(key)];
    }

    /// <inheritdoc />
    public float Axis(Axis axis)
    {
        return axis switch
        {
            Scripting.Axis.Horizontal => Positive(Key.D, Key.Right) - Positive(Key.A, Key.Left),
            Scripting.Axis.Vertical => Positive(Key.S, Key.Down) - Positive(Key.W, Key.Up),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "未知输入轴。"),
        };
    }

    /// <inheritdoc />
    public (float X, float Y) MousePixel { get; private set; }

    /// <inheritdoc />
    public float MouseWheelY { get; private set; }

    /// <inheritdoc />
    public bool IsMouseDown(MouseButton button)
    {
        return _downButtons[Index(button)];
    }

    /// <inheritdoc />
    public bool WasMousePressed(MouseButton button)
    {
        return _pressedButtons[Index(button)];
    }

    /// <inheritdoc />
    public bool WasMouseReleased(MouseButton button)
    {
        return _releasedButtons[Index(button)];
    }

    private float Positive(Key primary, Key secondary)
    {
        return IsDown(primary) || IsDown(secondary) ? 1f : 0f;
    }

    private static void ComputeEdges(bool[] previous, bool[] current, bool[] pressed, bool[] released)
    {
        for (int i = 0; i < current.Length; i++)
        {
            pressed[i] = current[i] && !previous[i];
            released[i] = !current[i] && previous[i];
        }
    }

    private static int Index(Key key)
    {
        int index = (int)key;
        return (uint)index < KeyCount
            ? index
            : throw new ArgumentOutOfRangeException(nameof(key), key, "未知脚本按键。");
    }

    private static int Index(MouseButton button)
    {
        int index = (int)button;
        return (uint)index < MouseButtonCount
            ? index
            : throw new ArgumentOutOfRangeException(nameof(button), button, "未知鼠标按键。");
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "输入数值必须为有限值。");
        }
    }
}
