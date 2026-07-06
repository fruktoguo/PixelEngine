namespace PixelEngine.UI;

/// <summary>
/// UI 脏矩形收集与合并器；只做纯几何计算，不触发 GL 上传。
/// </summary>
public sealed class UiDirtyRectCollector
{
    private readonly UiDirtyRect[] _rects;

    /// <summary>
    /// 创建固定容量脏矩形收集器。
    /// </summary>
    /// <param name="capacity">最大保留矩形数量。</param>
    public UiDirtyRectCollector(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _rects = GC.AllocateUninitializedArray<UiDirtyRect>(capacity);
    }

    /// <summary>
    /// 当前已合并的脏矩形数量。
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// 清空收集器。
    /// </summary>
    public void Clear()
    {
        _rects.AsSpan(0, Count).Clear();
        Count = 0;
    }

    /// <summary>
    /// 添加一个脏矩形；空矩形会被忽略。
    /// </summary>
    /// <param name="rect">待添加矩形。</param>
    /// <returns>添加后当前有效矩形数量。</returns>
    public int Add(in UiDirtyRect rect)
    {
        rect.Validate();
        if (rect.IsEmpty)
        {
            return Count;
        }

        UiDirtyRect merged = rect;
        int index = 0;
        while (index < Count)
        {
            if (!_rects[index].TouchesOrOverlaps(in merged))
            {
                index++;
                continue;
            }

            merged = _rects[index].Union(in merged);
            RemoveAtSwapBack(index);
            index = 0;
        }

        if (Count == _rects.Length)
        {
            CoalesceIntoFirst(in merged);
        }
        else
        {
            _rects[Count++] = merged;
        }

        return Count;
    }

    /// <summary>
    /// 将当前脏矩形复制到目标 Span。
    /// </summary>
    /// <param name="destination">输出缓冲。</param>
    /// <returns>复制的矩形数量。</returns>
    public int CopyTo(Span<UiDirtyRect> destination)
    {
        int count = Math.Min(Count, destination.Length);
        _rects.AsSpan(0, count).CopyTo(destination);
        return count;
    }

    private void CoalesceIntoFirst(in UiDirtyRect rect)
    {
        _rects[0] = _rects[0].Union(in rect);
        for (int i = 1; i < Count; i++)
        {
            if (_rects[i].TouchesOrOverlaps(in _rects[0]))
            {
                _rects[0] = _rects[0].Union(in _rects[i]);
                RemoveAtSwapBack(i);
                i = 0;
            }
        }
    }

    private void RemoveAtSwapBack(int index)
    {
        int last = --Count;
        if (index != last)
        {
            _rects[index] = _rects[last];
        }

        _rects[last] = default;
    }
}
