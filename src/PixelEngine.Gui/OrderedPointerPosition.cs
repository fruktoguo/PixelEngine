using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 按 Silk 输入事件的交付顺序保存指针位置，避免按钮回调读取到已经推进至后续 move 的设备当前位置。
/// </summary>
internal struct OrderedPointerPosition
{
    private Vector2 _position;

    /// <summary>
    /// 是否已经接收过可作为按钮事件坐标的指针位置。
    /// </summary>
    public bool HasPosition { get; private set; }

    /// <summary>
    /// 记录最近一次已交付的移动事件坐标。
    /// </summary>
    /// <param name="position">Silk 移动事件携带的窗口逻辑坐标。</param>
    /// <returns>原坐标，便于直接转发。</returns>
    public Vector2 RecordMove(Vector2 position)
    {
        _position = position;
        HasPosition = true;
        return position;
    }

    /// <summary>
    /// 解析按钮或滚轮事件位置。已有移动事件时坚持事件序坐标；尚无位置时才读取调用方提供的设备当前位置。
    /// </summary>
    /// <param name="fallbackPosition">从未收到移动事件时的设备当前位置。</param>
    /// <returns>应随当前按钮或滚轮事件转发的位置。</returns>
    public Vector2 ResolveButtonPosition(Vector2 fallbackPosition)
    {
        if (!HasPosition)
        {
            _ = RecordMove(fallbackPosition);
        }

        return _position;
    }

    /// <summary>
    /// 清除跨焦点生命周期的旧位置，使下一次没有先导 move 的点击回退到设备当前位置。
    /// </summary>
    public void Reset()
    {
        HasPosition = false;
    }
}
