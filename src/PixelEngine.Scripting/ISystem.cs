namespace PixelEngine.Scripting;

/// <summary>
/// 表示脚本层的数据导向批处理系统；由 Hosting 在相位 1 按注册顺序调用。
/// </summary>
public interface ISystem
{
    /// <summary>
    /// 在执行 sim tick 的帧调用一次；sim 降频时跳过，适合与权威模拟对齐的玩法逻辑。
    /// </summary>
    /// <param name="context">脚本访问引擎能力的统一上下文。</param>
    void OnSimTick(IScriptContext context);

    /// <summary>
    /// 每个渲染帧调用一次；即使 sim 降频也保持逐帧调用。
    /// </summary>
    /// <param name="context">脚本访问引擎能力的统一上下文。</param>
    /// <param name="dt">本帧经过时间，单位秒。</param>
    void OnFrame(IScriptContext context, float dt);
}
