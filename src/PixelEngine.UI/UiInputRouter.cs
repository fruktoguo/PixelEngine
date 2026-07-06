namespace PixelEngine.UI;

/// <summary>
/// 把平台输入泵入游戏 UI 后端，并提供 UI capture 结果给游戏输入仲裁。
/// </summary>
public sealed class UiInputRouter
{
    private readonly GameUiHost _host;
    private readonly IUiInputSource _source;
    private readonly UiKey[] _downKeys;
    private readonly UiKey[] _previousKeys;
    private readonly char[] _textBuffer;
    private int _downKeyCount;
    private int _previousKeyCount;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;
    private bool _hasKeyboardFocus;

    /// <summary>
    /// 创建 UI 输入路由器。
    /// </summary>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <param name="source">平台输入源。</param>
    /// <param name="keyCapacity">单帧按键缓冲容量。</param>
    /// <param name="textCapacity">单帧文本缓冲容量。</param>
    public UiInputRouter(GameUiHost host, IUiInputSource source, int keyCapacity = 64, int textCapacity = 64)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keyCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(textCapacity);
        _downKeys = new UiKey[keyCapacity];
        _previousKeys = new UiKey[keyCapacity];
        _textBuffer = new char[textCapacity];
    }

    /// <summary>
    /// 最近一次刷新得到的 UI 输入捕获快照。
    /// </summary>
    public UiInputCapture Capture { get; private set; } = UiInputCapture.None;

    /// <summary>
    /// 刷新 UI capture；不向后端注入输入，用于游戏输入采样前的同帧仲裁。
    /// </summary>
    /// <returns>当前 UI 输入捕获快照。</returns>
    public UiInputCapture RefreshCapture()
    {
        if (!_source.TryGetPointer(out UiPointerState pointer))
        {
            Capture = UiInputCapture.None;
            return Capture;
        }

        UiHitResult hit = _host.HitTest(pointer.X, pointer.Y);
        UpdateKeyboardFocus(hit, pointer);
        Capture = new UiInputCapture(hit.HitsUi, hit.Opaque, hit.WantsMouse, hit.WantsKeyboard || _hasKeyboardFocus);
        return Capture;
    }

    /// <summary>
    /// 把本帧输入状态注入 UI 后端。
    /// </summary>
    /// <returns>当前 UI 输入捕获快照。</returns>
    public UiInputCapture Pump()
    {
        return Pump(allowPointer: true, allowKeyboard: true);
    }

    /// <summary>
    /// 在上游输入门允许的前提下把本帧输入状态注入 UI 后端。
    /// </summary>
    /// <param name="allowPointer">上游是否允许 UI 消费指针输入。</param>
    /// <param name="allowKeyboard">上游是否允许 UI 消费键盘/文本输入。</param>
    /// <returns>当前 UI 输入捕获快照。</returns>
    public UiInputCapture Pump(bool allowPointer, bool allowKeyboard)
    {
        if (allowPointer && _source.TryGetPointer(out UiPointerState pointer))
        {
            _host.FeedPointerMove(pointer.X, pointer.Y);
            if (pointer.WheelDeltaX != 0f || pointer.WheelDeltaY != 0f)
            {
                _host.FeedScroll(pointer.WheelDeltaX, pointer.WheelDeltaY);
            }

            FeedButton(UiPointerButton.Left, pointer.LeftDown, ref _leftDown);
            FeedButton(UiPointerButton.Right, pointer.RightDown, ref _rightDown);
            FeedButton(UiPointerButton.Middle, pointer.MiddleDown, ref _middleDown);
        }
        else
        {
            FeedButton(UiPointerButton.Left, isDown: false, ref _leftDown);
            FeedButton(UiPointerButton.Right, isDown: false, ref _rightDown);
            FeedButton(UiPointerButton.Middle, isDown: false, ref _middleDown);
        }

        if (allowPointer)
        {
            _ = RefreshCapture();
        }
        else
        {
            _hasKeyboardFocus = false;
            Capture = UiInputCapture.None;
        }

        if (allowKeyboard && Capture.WantCaptureKeyboard)
        {
            _downKeyCount = Math.Clamp(_source.CaptureDownKeys(_downKeys, out UiKeyModifiers modifiers), 0, _downKeys.Length);
            FeedKeyEdges(_downKeys.AsSpan(0, _downKeyCount), _previousKeys.AsSpan(0, _previousKeyCount), modifiers);
            _downKeys.AsSpan(0, _downKeyCount).CopyTo(_previousKeys);
            _previousKeyCount = _downKeyCount;

            if (_textBuffer.Length > 0)
            {
                int textCount = CaptureCommittedText(_textBuffer);
                if (textCount > 0)
                {
                    _host.FeedText(_textBuffer.AsSpan(0, textCount));
                }
            }
        }
        else
        {
            ReleasePreviousKeys();
            if (_textBuffer.Length > 0)
            {
                _ = _source.CaptureText(_textBuffer);
            }
        }

        return Capture;
    }

    private int CaptureCommittedText(Span<char> destination)
    {
        int textCount = Math.Clamp(_source.CaptureText(destination), 0, destination.Length);
        int write = 0;
        for (int i = 0; i < textCount; i++)
        {
            char character = destination[i];
            if (character == '\0' || char.IsControl(character))
            {
                continue;
            }

            destination[write++] = character;
        }

        destination.Slice(write, textCount - write).Clear();
        return write;
    }

    private void ReleasePreviousKeys()
    {
        for (int i = 0; i < _previousKeyCount; i++)
        {
            _host.FeedKey(_previousKeys[i], isDown: false, UiKeyModifiers.None);
        }

        _previousKeyCount = 0;
    }

    private void FeedButton(UiPointerButton button, bool isDown, ref bool previous)
    {
        if (isDown != previous)
        {
            _host.FeedPointerButton(button, isDown);
            previous = isDown;
        }
    }

    private void FeedKeyEdges(ReadOnlySpan<UiKey> current, ReadOnlySpan<UiKey> previous, UiKeyModifiers modifiers)
    {
        for (int i = 0; i < current.Length; i++)
        {
            if (!Contains(previous, current[i]))
            {
                _host.FeedKey(current[i], isDown: true, modifiers);
            }
        }

        for (int i = 0; i < previous.Length; i++)
        {
            if (!Contains(current, previous[i]))
            {
                _host.FeedKey(previous[i], isDown: false, modifiers);
            }
        }
    }

    private void UpdateKeyboardFocus(UiHitResult hit, in UiPointerState pointer)
    {
        if (hit.WantsKeyboard)
        {
            _hasKeyboardFocus = true;
            return;
        }

        if (pointer.LeftDown || pointer.RightDown || pointer.MiddleDown)
        {
            _hasKeyboardFocus = false;
        }
    }

    private static bool Contains(ReadOnlySpan<UiKey> keys, UiKey key)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key)
            {
                return true;
            }
        }

        return false;
    }
}
