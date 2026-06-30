namespace PixelEngine.Simulation;

/// <summary>
/// 材质 custom-update 执行 seam。
/// </summary>
public interface IMaterialCustomUpdateExecutor
{
    /// <summary>
    /// 空实现，表示没有材质自定义更新。
    /// </summary>
    static IMaterialCustomUpdateExecutor Null { get; } = new NullMaterialCustomUpdateExecutor();

    /// <summary>
    /// 若指定材质注册了 custom-update，则执行并返回 true。
    /// </summary>
    bool TryUpdate(
        ref NeighborWindow window,
        IChunkSource chunks,
        int wx,
        int wy,
        ushort material,
        byte parityBit);

    private sealed class NullMaterialCustomUpdateExecutor : IMaterialCustomUpdateExecutor
    {
        public bool TryUpdate(
            ref NeighborWindow window,
            IChunkSource chunks,
            int wx,
            int wy,
            ushort material,
            byte parityBit)
        {
            return false;
        }
    }
}
