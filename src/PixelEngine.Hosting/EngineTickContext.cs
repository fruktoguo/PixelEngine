using PixelEngine.Core.Time;

namespace PixelEngine.Hosting;

/// <summary>
/// 单个 Engine tick 传递给 phase hook 的上下文。
/// </summary>
/// <param name="Engine">当前 Engine。</param>
/// <param name="Context">当前 EngineContext。</param>
/// <param name="Timing">当前帧时序决策。</param>
/// <param name="Phase">正在执行的相位。</param>
public readonly record struct EngineTickContext(
    Engine Engine,
    EngineContext Context,
    FrameTiming Timing,
    EnginePhase Phase);
