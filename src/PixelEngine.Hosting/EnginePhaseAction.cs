namespace PixelEngine.Hosting;

/// <summary>
/// Engine phase hook 委托。
/// </summary>
/// <param name="context">当前 tick 与相位上下文。</param>
public delegate void EnginePhaseAction(EngineTickContext context);
