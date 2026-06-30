namespace PixelEngine.Simulation;

/// <summary>
/// CA movement 后尝试执行材质反应的 seam，由 Materials/Reactions 子系统实现。
/// </summary>
public interface IReactionExecutor
{
    /// <summary>
    /// 空实现，表示没有任何反应。
    /// </summary>
    static IReactionExecutor Null { get; } = new NullReactionExecutor();

    /// <summary>
    /// 对两个 von Neumann 邻居 cell 尝试反应。实现方负责写产物、parity、dirty 与 KeepAlive。
    /// </summary>
    bool TryReact(ref NeighborWindow window, int wx1, int wy1, ushort materialA, int wx2, int wy2, ushort materialB, byte parityBit);

    private sealed class NullReactionExecutor : IReactionExecutor
    {
        public bool TryReact(ref NeighborWindow window, int wx1, int wy1, ushort materialA, int wx2, int wy2, ushort materialB, byte parityBit)
        {
            return false;
        }
    }
}
