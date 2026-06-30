namespace PixelEngine.Hosting;

/// <summary>
/// 延迟命令执行委托。
/// </summary>
/// <param name="context">当前 tick 与目标相位上下文。</param>
/// <param name="state">调用方提供的命令状态对象。</param>
public delegate void EngineCommandAction(EngineTickContext context, object? state);
