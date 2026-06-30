namespace PixelEngine.Core;

/// <summary>
/// 指定引擎运行时的确定性策略。
/// </summary>
public enum DeterminismMode
{
    /// <summary>
    /// 高性能模式，允许使用有状态随机源与平台最优执行路径。
    /// </summary>
    HighPerformance,

    /// <summary>
    /// 确定性模式，优先使用可重演的输入、定点或固定 round 规则与 counter-based 随机源。
    /// </summary>
    Deterministic,
}
