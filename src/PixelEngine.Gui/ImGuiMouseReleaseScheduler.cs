using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 为同一 render interval 内完成的快速拖拽延后 release，使 ImGui 至少观察一帧起点按下和一帧目标按下。
/// </summary>
internal sealed class ImGuiMouseReleaseScheduler
{
    internal const int MaximumMouseButtons = 5;
    private const float MovementThresholdSquared = 1f;

    private readonly ulong[] _downFrames = new ulong[MaximumMouseButtons];
    private readonly ulong[] _releaseFrames = new ulong[MaximumMouseButtons];
    private readonly Vector2[] _downPositions = new Vector2[MaximumMouseButtons];
    private ulong _frameIndex;
    private Vector2 _lastPosition;
    private int _logicalDownMask;
    private int _movedInDownFrameMask;
    private int _pendingReleaseMask;
    private bool _hasPosition;

    /// <summary>
    /// 记录已经转发给 ImGui 的指针位置。
    /// </summary>
    /// <param name="position">ImGui 坐标系中的指针位置。</param>
    public void RecordPosition(Vector2 position)
    {
        _lastPosition = position;
        _hasPosition = true;

        int downMask = _logicalDownMask;
        for (int button = 0; button < MaximumMouseButtons && downMask != 0; button++)
        {
            int bit = 1 << button;
            if ((downMask & bit) == 0)
            {
                continue;
            }

            downMask &= ~bit;
            if (_downFrames[button] == _frameIndex &&
                Vector2.DistanceSquared(_downPositions[button], position) >= MovementThresholdSquared)
            {
                _movedInDownFrameMask |= bit;
            }
        }
    }

    /// <summary>
    /// 判断当前按钮事件是否立即转发；同帧完整拖拽的 release 会被调度到目标按下帧之后。
    /// </summary>
    /// <param name="button">ImGui 鼠标按钮索引。</param>
    /// <param name="down">是否按下。</param>
    /// <param name="releaseBeforeEvent">是否必须先提交前一次延迟 release，再提交当前按钮事件。</param>
    /// <returns>true 表示调用方应立即转发，false 表示 release 已由本调度器接管。</returns>
    public bool ShouldEmitButtonEvent(int button, bool down, out bool releaseBeforeEvent)
    {
        releaseBeforeEvent = false;
        if ((uint)button >= MaximumMouseButtons)
        {
            return true;
        }

        int bit = 1 << button;
        if (down)
        {
            releaseBeforeEvent = (_pendingReleaseMask & bit) != 0;
            _logicalDownMask |= bit;
            _movedInDownFrameMask &= ~bit;
            _pendingReleaseMask &= ~bit;
            _downFrames[button] = _frameIndex;
            _downPositions[button] = _hasPosition ? _lastPosition : default;
            return true;
        }

        bool deferRelease =
            (_logicalDownMask & bit) != 0 &&
            (_movedInDownFrameMask & bit) != 0 &&
            _downFrames[button] == _frameIndex;
        _logicalDownMask &= ~bit;
        _movedInDownFrameMask &= ~bit;
        if (!deferRelease)
        {
            return true;
        }

        _pendingReleaseMask |= bit;
        _releaseFrames[button] = _frameIndex + 2;
        return false;
    }

    /// <summary>
    /// 收集本帧开始前到期的 release，并推进 render frame 序号。
    /// </summary>
    /// <param name="destination">至少可容纳五个按钮索引的缓冲区。</param>
    /// <returns>到期 release 数量。</returns>
    public int BeginFrame(Span<int> destination)
    {
        if (destination.Length < MaximumMouseButtons)
        {
            throw new ArgumentException("鼠标 release 缓冲区容量不足。", nameof(destination));
        }

        int count = 0;
        int pendingMask = _pendingReleaseMask;
        for (int button = 0; button < MaximumMouseButtons && pendingMask != 0; button++)
        {
            int bit = 1 << button;
            if ((pendingMask & bit) == 0)
            {
                continue;
            }

            pendingMask &= ~bit;
            if (_releaseFrames[button] <= _frameIndex)
            {
                destination[count++] = button;
                _pendingReleaseMask &= ~bit;
            }
        }

        _frameIndex++;
        return count;
    }

    /// <summary>
    /// 失焦时收集所有逻辑按下或待 release 的按钮，并清空跨焦点状态。
    /// </summary>
    /// <param name="destination">至少可容纳五个按钮索引的缓冲区。</param>
    /// <returns>必须立即 release 的按钮数量。</returns>
    public int FlushForFocusLoss(Span<int> destination)
    {
        if (destination.Length < MaximumMouseButtons)
        {
            throw new ArgumentException("鼠标 release 缓冲区容量不足。", nameof(destination));
        }

        int count = 0;
        int releaseMask = _logicalDownMask | _pendingReleaseMask;
        for (int button = 0; button < MaximumMouseButtons; button++)
        {
            int bit = 1 << button;
            if ((releaseMask & bit) != 0)
            {
                destination[count++] = button;
            }
        }

        _logicalDownMask = 0;
        _movedInDownFrameMask = 0;
        _pendingReleaseMask = 0;
        _hasPosition = false;
        return count;
    }
}
