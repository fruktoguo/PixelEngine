namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 保存诊断 HUD 与预算监测需要的引擎计数器。
/// </summary>
public sealed class EngineCounters
{
    private long _activeChunks;
    private long _activeCells;
    private long _freeParticles;
    private long _rigidBodies;
    private long _residentChunks;
    private long _residentMemoryBytes;

    /// <summary>
    /// 获取或设置活跃 chunk 数。
    /// </summary>
    public long ActiveChunks { get => Volatile.Read(ref _activeChunks); set => Volatile.Write(ref _activeChunks, value); }

    /// <summary>
    /// 获取或设置活跃 cell 数。
    /// </summary>
    public long ActiveCells { get => Volatile.Read(ref _activeCells); set => Volatile.Write(ref _activeCells, value); }

    /// <summary>
    /// 获取或设置自由粒子数量。
    /// </summary>
    public long FreeParticles { get => Volatile.Read(ref _freeParticles); set => Volatile.Write(ref _freeParticles, value); }

    /// <summary>
    /// 获取或设置刚体数量。
    /// </summary>
    public long RigidBodies { get => Volatile.Read(ref _rigidBodies); set => Volatile.Write(ref _rigidBodies, value); }

    /// <summary>
    /// 获取或设置常驻 chunk 数量。
    /// </summary>
    public long ResidentChunks { get => Volatile.Read(ref _residentChunks); set => Volatile.Write(ref _residentChunks, value); }

    /// <summary>
    /// 获取或设置常驻内存字节数。
    /// </summary>
    public long ResidentMemoryBytes { get => Volatile.Read(ref _residentMemoryBytes); set => Volatile.Write(ref _residentMemoryBytes, value); }

    /// <summary>
    /// 获取或设置当前 sim 频率。
    /// </summary>
    public double SimHz { get; set; }

    /// <summary>
    /// 线程安全地累加活跃 chunk 数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddActiveChunks(long delta)
    {
        _ = Interlocked.Add(ref _activeChunks, delta);
    }

    /// <summary>
    /// 线程安全地累加活跃 cell 数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddActiveCells(long delta)
    {
        _ = Interlocked.Add(ref _activeCells, delta);
    }

    /// <summary>
    /// 线程安全地累加自由粒子数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddFreeParticles(long delta)
    {
        _ = Interlocked.Add(ref _freeParticles, delta);
    }

    /// <summary>
    /// 线程安全地累加刚体数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddRigidBodies(long delta)
    {
        _ = Interlocked.Add(ref _rigidBodies, delta);
    }

    /// <summary>
    /// 线程安全地累加常驻 chunk 数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddResidentChunks(long delta)
    {
        _ = Interlocked.Add(ref _residentChunks, delta);
    }

    /// <summary>
    /// 线程安全地累加常驻内存字节数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddResidentMemoryBytes(long delta)
    {
        _ = Interlocked.Add(ref _residentMemoryBytes, delta);
    }
}
