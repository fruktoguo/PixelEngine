using System.Runtime;

namespace PixelEngine.Hosting;

/// <summary>
/// 串行化进程级 GC 状态切换；<see cref="GCSettings.LatencyMode" /> 与 NoGCRegion 都是全局状态。
/// </summary>
internal static class EngineGcCoordinator
{
    private static readonly object Gate = new();

    /// <summary>
    /// 在无 NoGCRegion 临界帧时设置托管 GC 延迟模式。
    /// </summary>
    /// <param name="mode">目标 GC 延迟模式。</param>
    public static void ApplyLatencyMode(GCLatencyMode mode)
    {
        lock (Gate)
        {
            GCSettings.LatencyMode = mode;
        }
    }

    /// <summary>
    /// 尝试进入 NoGCRegion，并在成功后持有全局 GC 状态门直到 <see cref="EndNoGcRegion" />。
    /// </summary>
    /// <param name="budgetBytes">本帧 NoGCRegion 预算字节数。</param>
    /// <returns>成功进入 NoGCRegion 时为 true；预算不足或运行时拒绝时为 false。</returns>
    public static bool TryBeginNoGcRegion(long budgetBytes)
    {
        System.Threading.Monitor.Enter(Gate);
        try
        {
            if (!GC.TryStartNoGCRegion(budgetBytes, disallowFullBlockingGC: true))
            {
                System.Threading.Monitor.Exit(Gate);
                return false;
            }

            return true;
        }
        catch
        {
            System.Threading.Monitor.Exit(Gate);
            throw;
        }
    }

    /// <summary>
    /// 退出当前 Hosting 拥有的 NoGCRegion，并释放全局 GC 状态门。
    /// </summary>
    public static void EndNoGcRegion()
    {
        try
        {
            GC.EndNoGCRegion();
        }
        finally
        {
            System.Threading.Monitor.Exit(Gate);
        }
    }
}
