namespace PixelEngine.Hosting;

/// <summary>
/// 表示 Hosting 当前驱动世界与脚本生命周期的执行模式。
/// </summary>
public enum EngineExecutionMode
{
    /// <summary>
    /// 编辑模式：暂停 sim/physics，只推进输入、渲染与后台流式相位。
    /// </summary>
    Edit,

    /// <summary>
    /// 运行模式：按固定步长推进玩法、sim、physics 与渲染。
    /// </summary>
    Play,

    /// <summary>
    /// 单步模式：从编辑态临时执行恰好一个 sim tick。
    /// </summary>
    Step,
}
