namespace PixelEngine.Simulation;

/// <summary>
/// cell lifetime 归零时的处理 seam，由 Materials/Lifecycle 子系统实现。
/// </summary>
public interface ILifetimeSink
{
    /// <summary>
    /// 空实现，表示 lifetime 归零不改变材质。
    /// </summary>
    static ILifetimeSink Null { get; } = new NullLifetimeSink();

    /// <summary>
    /// 通知一个 cell 的 lifetime 已递减到 0。实现方可写材质、flag、dirty 或粒子请求。
    /// </summary>
    void OnExpired(ref NeighborWindow window, int wx, int wy, ushort material, byte parityBit);

    private sealed class NullLifetimeSink : ILifetimeSink
    {
        public void OnExpired(ref NeighborWindow window, int wx, int wy, ushort material, byte parityBit)
        {
        }
    }
}
