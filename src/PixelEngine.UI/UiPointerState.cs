namespace PixelEngine.UI;

/// <summary>
/// UI 指针输入源在当前帧采样到的状态。
/// </summary>
/// <param name="X">UI 坐标 x。</param>
/// <param name="Y">UI 坐标 y。</param>
/// <param name="WheelDeltaX">本帧水平滚轮增量。</param>
/// <param name="WheelDeltaY">本帧垂直滚轮增量。</param>
/// <param name="LeftDown">左键是否按下。</param>
/// <param name="RightDown">右键是否按下。</param>
/// <param name="MiddleDown">中键是否按下。</param>
public readonly record struct UiPointerState(
    float X,
    float Y,
    float WheelDeltaX,
    float WheelDeltaY,
    bool LeftDown,
    bool RightDown,
    bool MiddleDown);
