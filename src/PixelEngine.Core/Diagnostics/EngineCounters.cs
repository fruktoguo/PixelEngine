namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 保存诊断 HUD 与预算监测需要的引擎计数器。
/// </summary>
public sealed class EngineCounters
{
    private long _activeChunks;
    private long _activeCells;
    private long _freeParticles;
    private long _freeParticlesDepositedThisTick;
    private long _freeParticlesDroppedThisTick;
    private long _freeParticlesKilledThisTick;
    private long _freeParticlesSpawnedThisTick;
    private long _materialRemapFallbackHits;
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
    /// 获取或设置本 tick 生成的自由粒子数量。
    /// </summary>
    public long FreeParticlesSpawnedThisTick { get => Volatile.Read(ref _freeParticlesSpawnedThisTick); set => Volatile.Write(ref _freeParticlesSpawnedThisTick, value); }

    /// <summary>
    /// 获取或设置本 tick 沉积的自由粒子数量。
    /// </summary>
    public long FreeParticlesDepositedThisTick { get => Volatile.Read(ref _freeParticlesDepositedThisTick); set => Volatile.Write(ref _freeParticlesDepositedThisTick, value); }

    /// <summary>
    /// 获取或设置本 tick 因寿命或回退删除的自由粒子数量。
    /// </summary>
    public long FreeParticlesKilledThisTick { get => Volatile.Read(ref _freeParticlesKilledThisTick); set => Volatile.Write(ref _freeParticlesKilledThisTick, value); }

    /// <summary>
    /// 获取或设置本 tick 丢弃的自由粒子或粒子事件数量。
    /// </summary>
    public long FreeParticlesDroppedThisTick { get => Volatile.Read(ref _freeParticlesDroppedThisTick); set => Volatile.Write(ref _freeParticlesDroppedThisTick, value); }

    /// <summary>
    /// 获取或设置 material 重映射 fallback 命中次数。
    /// </summary>
    public long MaterialRemapFallbackHits { get => Volatile.Read(ref _materialRemapFallbackHits); set => Volatile.Write(ref _materialRemapFallbackHits, value); }

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
    /// 获取或设置当前 GPU compute 后端枚举值。
    /// </summary>
    public long GpuComputeSelectedBackend { get; set; }

    /// <summary>
    /// 获取或设置 G1 GL compute 门控是否命中。
    /// </summary>
    public long GpuComputeGlAvailable { get; set; }

    /// <summary>
    /// 获取或设置 G2 ComputeSharp 门控是否命中。
    /// </summary>
    public long GpuComputeSharpAvailable { get; set; }

    /// <summary>
    /// 获取或设置 G3 基线回退门控是否命中。
    /// </summary>
    public long GpuComputeBaselineFallback { get; set; }

    /// <summary>
    /// 获取或设置 G4 compute bloom 开关是否开启。
    /// </summary>
    public long GpuComputeBloomEnabled { get; set; }

    /// <summary>
    /// 获取或设置 G4 Radiance Cascades 开关是否开启。
    /// </summary>
    public long GpuComputeRadianceCascadesEnabled { get; set; }

    /// <summary>
    /// 获取或设置 G4 GPU 粒子开关是否开启。
    /// </summary>
    public long GpuComputeParticlesEnabled { get; set; }

    /// <summary>
    /// 获取或设置 G4 非权威 air/smoke 开关是否开启。
    /// </summary>
    public long GpuComputeAirSmokeEnabled { get; set; }

    /// <summary>
    /// 获取或设置当前 sim 频率。
    /// </summary>
    public double SimHz { get; set; }

    /// <summary>
    /// 批量发布 GPU compute 后端与 G1-G4 门控诊断，避免 HUD 读取到跨帧混合状态。
    /// </summary>
    /// <param name="selectedBackend">当前后端枚举值。</param>
    /// <param name="glAvailable">G1 GL compute 是否可用。</param>
    /// <param name="computeSharpAvailable">G2 ComputeSharp 是否可用。</param>
    /// <param name="baselineFallback">G3 基线回退是否命中。</param>
    /// <param name="bloomEnabled">G4 compute bloom 是否开启。</param>
    /// <param name="radianceCascadesEnabled">G4 Radiance Cascades 是否开启。</param>
    /// <param name="particlesEnabled">G4 GPU 粒子是否开启。</param>
    /// <param name="airSmokeEnabled">G4 非权威 air/smoke 是否开启。</param>
    public void SetGpuComputeDiagnostics(
        long selectedBackend,
        long glAvailable,
        long computeSharpAvailable,
        long baselineFallback,
        long bloomEnabled,
        long radianceCascadesEnabled,
        long particlesEnabled,
        long airSmokeEnabled)
    {
        GpuComputeSelectedBackend = selectedBackend;
        GpuComputeGlAvailable = glAvailable;
        GpuComputeSharpAvailable = computeSharpAvailable;
        GpuComputeBaselineFallback = baselineFallback;
        GpuComputeBloomEnabled = bloomEnabled;
        GpuComputeRadianceCascadesEnabled = radianceCascadesEnabled;
        GpuComputeParticlesEnabled = particlesEnabled;
        GpuComputeAirSmokeEnabled = airSmokeEnabled;
    }

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
    /// 线程安全地累加本 tick 生成的自由粒子数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddFreeParticlesSpawnedThisTick(long delta)
    {
        _ = Interlocked.Add(ref _freeParticlesSpawnedThisTick, delta);
    }

    /// <summary>
    /// 线程安全地累加本 tick 沉积的自由粒子数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddFreeParticlesDepositedThisTick(long delta)
    {
        _ = Interlocked.Add(ref _freeParticlesDepositedThisTick, delta);
    }

    /// <summary>
    /// 线程安全地累加本 tick 因寿命或回退删除的自由粒子数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddFreeParticlesKilledThisTick(long delta)
    {
        _ = Interlocked.Add(ref _freeParticlesKilledThisTick, delta);
    }

    /// <summary>
    /// 线程安全地累加本 tick 丢弃的自由粒子或粒子事件数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddFreeParticlesDroppedThisTick(long delta)
    {
        _ = Interlocked.Add(ref _freeParticlesDroppedThisTick, delta);
    }

    /// <summary>
    /// 线程安全地累加 material 重映射 fallback 命中次数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddMaterialRemapFallbackHits(long delta)
    {
        _ = Interlocked.Add(ref _materialRemapFallbackHits, delta);
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
