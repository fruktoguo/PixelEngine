namespace PixelEngine.Simulation;

/// <summary>
/// 结构破坏动作完成后向粒子、采集或玩法层发布的零分配事件接收器。
/// </summary>
public interface ICellDestructionSink
{
    /// <summary>
    /// 空实现，表示仅修改权威 cell 网格，不产生碎屑或采集副作用。
    /// </summary>
    static ICellDestructionSink Null { get; } = new NullCellDestructionSink();

    /// <summary>
    /// 通知一个普通 cell 已经由结构破坏动作转为 rubble 或 Empty。
    /// </summary>
    /// <param name="item">结构破坏事件。</param>
    void OnCellDestroyed(in CellDestructionEvent item);

    private sealed class NullCellDestructionSink : ICellDestructionSink
    {
        public void OnCellDestroyed(in CellDestructionEvent item)
        {
        }
    }
}
