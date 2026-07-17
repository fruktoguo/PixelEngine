using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 保存两次引擎输入采样之间到达的物理指针按钮边沿。
/// </summary>
/// <remarks>
/// Silk.NET 在窗口事件泵中同步调用写端，UI 输入相位在同一线程读取；固定环形缓冲避免稳态分配。
/// 队列满时只合并最后一个槽位到最新权威状态，确保异常输入洪泛不会留下卡住的按钮。
/// </remarks>
internal sealed class UiPointerTransitionQueue
{
    private readonly Entry[] _entries;
    private int _readIndex;
    private int _observedButtonMask;

    internal UiPointerTransitionQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _entries = new Entry[capacity];
    }

    internal int PendingCount { get; private set; }

    internal long CoalescedTransitionCount { get; private set; }

    internal long LeftPressEdges { get; private set; }

    internal long LeftReleaseEdges { get; private set; }

    internal float LastPressX { get; private set; }

    internal float LastPressY { get; private set; }

    internal float LastReleaseX { get; private set; }

    internal float LastReleaseY { get; private set; }

    internal void Synchronize(int buttonMask)
    {
        _observedButtonMask = buttonMask & AllButtonsMask;
    }

    internal void Record(UiPointerButton button, bool isDown, float x, float y)
    {
        int mask = ButtonMask(button);
        bool wasDown = (_observedButtonMask & mask) != 0;
        if (wasDown == isDown)
        {
            return;
        }

        _observedButtonMask = isDown
            ? _observedButtonMask | mask
            : _observedButtonMask & ~mask;
        if (button == UiPointerButton.Left)
        {
            if (isDown)
            {
                LeftPressEdges++;
                LastPressX = x;
                LastPressY = y;
            }
            else
            {
                LeftReleaseEdges++;
                LastReleaseX = x;
                LastReleaseY = y;
            }
        }

        Entry entry = new(x, y, _observedButtonMask);
        if (PendingCount == _entries.Length)
        {
            int lastIndex = (_readIndex + PendingCount - 1) % _entries.Length;
            _entries[lastIndex] = entry;
            CoalescedTransitionCount++;
            return;
        }

        int writeIndex = (_readIndex + PendingCount) % _entries.Length;
        _entries[writeIndex] = entry;
        PendingCount++;
    }

    internal void ReleaseAll(float x, float y)
    {
        RecordIfDown(UiPointerButton.Left, x, y);
        RecordIfDown(UiPointerButton.Right, x, y);
        RecordIfDown(UiPointerButton.Middle, x, y);
    }

    internal bool TryDequeue(out UiPointerState state)
    {
        if (PendingCount == 0)
        {
            state = default;
            return false;
        }

        Entry entry = _entries[_readIndex];
        _readIndex = (_readIndex + 1) % _entries.Length;
        PendingCount--;
        state = new UiPointerState(
            entry.X,
            entry.Y,
            0f,
            0f,
            (entry.ButtonMask & LeftButtonMask) != 0,
            (entry.ButtonMask & RightButtonMask) != 0,
            (entry.ButtonMask & MiddleButtonMask) != 0);
        return true;
    }

    internal static int CreateButtonMask(bool leftDown, bool rightDown, bool middleDown)
    {
        int mask = leftDown ? LeftButtonMask : 0;
        mask |= rightDown ? RightButtonMask : 0;
        mask |= middleDown ? MiddleButtonMask : 0;
        return mask;
    }

    private const int LeftButtonMask = 1 << 0;
    private const int RightButtonMask = 1 << 1;
    private const int MiddleButtonMask = 1 << 2;
    private const int AllButtonsMask = LeftButtonMask | RightButtonMask | MiddleButtonMask;

    private void RecordIfDown(UiPointerButton button, float x, float y)
    {
        if ((_observedButtonMask & ButtonMask(button)) != 0)
        {
            Record(button, isDown: false, x, y);
        }
    }

    private static int ButtonMask(UiPointerButton button)
    {
        return button switch
        {
            UiPointerButton.Left => LeftButtonMask,
            UiPointerButton.Right => RightButtonMask,
            UiPointerButton.Middle => MiddleButtonMask,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "未知 UI 指针按钮。"),
        };
    }

    private readonly record struct Entry(float X, float Y, int ButtonMask);
}
