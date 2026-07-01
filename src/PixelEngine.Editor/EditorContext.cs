using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Editor;

/// <summary>
/// 传入 Editor 面板的帧级上下文，仅包含只读共享状态与选择态。
/// </summary>
/// <param name="Counters">Core 诊断计数器快照来源。</param>
/// <param name="Selection">跨面板共享选择态。</param>
/// <param name="FrameIndex">当前渲染帧索引。</param>
/// <param name="Performance">性能 HUD 可消费的只读诊断快照。</param>
public readonly record struct EditorContext(
    EngineCounters Counters,
    EditorSelection Selection,
    long FrameIndex,
    EditorPerformanceSnapshot Performance)
{
    /// <summary>
    /// 创建只含计数器的 Editor 上下文；未接入 profiler 时性能 HUD 仍可显示计数项。
    /// </summary>
    public EditorContext(EngineCounters counters, EditorSelection selection, long frameIndex)
        : this(counters, selection, frameIndex, EditorPerformanceSnapshot.FromCounters(counters))
    {
    }
}
