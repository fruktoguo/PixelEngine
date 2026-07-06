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
    private long _noGcRegionEndFailures;
    private long _noGcRegionStartedLastFrame;
    private long _noGcRegionStartFailures;
    private long _noGcRegionStartAttempts;
    private long _noGcRegionSuccessfulFrames;
    private long _rigidBodies;
    private long _residentChunks;
    private long _residentMemoryBytes;
    private long _uiFontMissingGlyphs;

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
    /// 获取或设置 no-GC region 预算字节数；0 表示当前运行配置未启用。
    /// </summary>
    public long NoGcRegionBudgetBytes { get; set; }

    /// <summary>
    /// 获取 no-GC region 启动尝试次数。
    /// </summary>
    public long NoGcRegionStartAttempts => Volatile.Read(ref _noGcRegionStartAttempts);

    /// <summary>
    /// 获取 no-GC region 启动失败次数。
    /// </summary>
    public long NoGcRegionStartFailures => Volatile.Read(ref _noGcRegionStartFailures);

    /// <summary>
    /// 获取 no-GC region 成功覆盖帧数。
    /// </summary>
    public long NoGcRegionSuccessfulFrames => Volatile.Read(ref _noGcRegionSuccessfulFrames);

    /// <summary>
    /// 获取 no-GC region 结束失败次数；非零表示关键段内超预算或被外部终止。
    /// </summary>
    public long NoGcRegionEndFailures => Volatile.Read(ref _noGcRegionEndFailures);

    /// <summary>
    /// 获取最近一帧是否成功进入 no-GC region。
    /// </summary>
    public bool NoGcRegionStartedLastFrame => Volatile.Read(ref _noGcRegionStartedLastFrame) != 0;

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
    /// 获取或设置最近一帧音频派发排空事件数。
    /// </summary>
    public long AudioDrained { get; set; }

    /// <summary>
    /// 获取或设置最近一帧音频近坐标合并数。
    /// </summary>
    public long AudioCoalesced { get; set; }

    /// <summary>
    /// 获取或设置最近一帧音频丢弃数。
    /// </summary>
    public long AudioDropped { get; set; }

    /// <summary>
    /// 获取或设置最近一帧成功播放事件数。
    /// </summary>
    public long AudioPlayed { get; set; }

    /// <summary>
    /// 获取或设置活跃 positional voice 数。
    /// </summary>
    public long AudioActiveVoices { get; set; }

    /// <summary>
    /// 获取或设置窗口运行时长窗口平均渲染帧率；0 表示尚未收到墙钟帧间隔。
    /// </summary>
    public double RenderFramesPerSecond { get; set; }

    /// <summary>
    /// 获取或设置窗口运行时长窗口平均渲染帧耗时，单位毫秒；0 表示尚未收到墙钟帧间隔。
    /// </summary>
    public double RenderFrameMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置最近一帧真实渲染耗时，单位毫秒。
    /// </summary>
    public double RenderFrameLastMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置长窗口 99 分位渲染帧耗时，单位毫秒。
    /// </summary>
    public double RenderFrameP99Milliseconds { get; set; }

    /// <summary>
    /// 获取或设置基于 99 分位帧耗时计算的 1% low FPS。
    /// </summary>
    public double RenderFrameLow1PercentFps { get; set; }

    /// <summary>
    /// 获取或设置长窗口渲染帧耗时标准差，单位毫秒。
    /// </summary>
    public double RenderFrameJitterMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置当前渲染帧率统计窗口内的样本数。
    /// </summary>
    public int RenderFrameSampleCount { get; set; }

    /// <summary>
    /// 获取或设置最近一帧 CPU 实际工作耗时，单位毫秒；不包含 present/vsync 等待。
    /// </summary>
    public double FrameCpuWorkMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置最近一次异步回读到的整帧 GPU 执行耗时，单位毫秒；不可用时为 0。
    /// </summary>
    public double FrameGpuWorkMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置当前 GL 后端是否支持整帧 GPU timer query。
    /// </summary>
    public bool FrameGpuTimerAvailable { get; set; }

    /// <summary>
    /// 获取或设置最近一帧 present 提交与 UI 绘制的 CPU 工作耗时，单位毫秒。
    /// </summary>
    public double FramePresentSubmitMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置最近一帧游戏 UI 模型推送、Update 与事件 drain 耗时，单位毫秒。
    /// </summary>
    public double UiUpdateMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置最近一帧游戏 UI / GUI present 层合成耗时，单位毫秒。
    /// </summary>
    public double UiCompositeMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置最近一帧游戏 UI 后端实际光栅化或绘制耗时，单位毫秒；静态无脏帧为 0。
    /// </summary>
    public double UiPaintMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置最近一帧游戏 UI 离屏纹理或脏矩形上传耗时，单位毫秒；无上传型后端为 0。
    /// </summary>
    public double UiUploadMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置当前 UI present 降频间隔；1 表示每渲染帧都允许 paint/composite。
    /// </summary>
    public long UiPresentationIntervalFrames { get; set; }

    /// <summary>
    /// 获取或设置因 UI present 降频而累计跳过的 paint/composite 帧数。
    /// </summary>
    public long UiSkippedPresentationFrames { get; set; }

    /// <summary>
    /// 获取累计 UI 字体缺字码点数。
    /// </summary>
    public long UiFontMissingGlyphs => Volatile.Read(ref _uiFontMissingGlyphs);

    /// <summary>
    /// 获取或设置最近一帧 SwapBuffers / vsync / present 阻塞等待耗时，单位毫秒。
    /// </summary>
    public double FramePresentWaitMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置最近一帧已确认的非工作等待耗时，单位毫秒。
    /// </summary>
    public double FrameWaitMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置扣除 present/vsync 等待后的有效帧耗时，单位毫秒。
    /// </summary>
    public double EffectiveFrameMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置扣除 present/vsync 等待后的理论有效帧率。
    /// </summary>
    public double EffectiveFramesPerSecond { get; set; }

    /// <summary>
    /// 获取或设置当前窗口 VSync 是否开启。
    /// </summary>
    public bool VSyncEnabled { get; set; }

    /// <summary>
    /// 获取或设置活跃 ambient loop voice 数。
    /// </summary>
    public long AudioActiveAmbientVoices { get; set; }

    /// <summary>
    /// 获取或设置累计 voice 抢占次数。
    /// </summary>
    public long AudioVoiceSteals { get; set; }

    /// <summary>
    /// 获取或设置已加载音频 clip 数。
    /// </summary>
    public long AudioLoadedClips { get; set; }

    /// <summary>
    /// 获取或设置加载中的音频 clip 数。
    /// </summary>
    public long AudioLoadingClips { get; set; }

    /// <summary>
    /// 获取或设置最近一帧音频派发耗时，单位毫秒。
    /// </summary>
    public double AudioDispatchMilliseconds { get; set; }

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
    /// 批量发布音频派发与资源诊断，避免 HUD 读取到跨帧混合状态。
    /// </summary>
    public void SetAudioDiagnostics(
        long drained,
        long coalesced,
        long dropped,
        long played,
        long activeVoices,
        long activeAmbientVoices,
        long voiceSteals,
        long loadedClips,
        long loadingClips,
        double dispatchMilliseconds)
    {
        AudioDrained = drained;
        AudioCoalesced = coalesced;
        AudioDropped = dropped;
        AudioPlayed = played;
        AudioActiveVoices = activeVoices;
        AudioActiveAmbientVoices = activeAmbientVoices;
        AudioVoiceSteals = voiceSteals;
        AudioLoadedClips = loadedClips;
        AudioLoadingClips = loadingClips;
        AudioDispatchMilliseconds = dispatchMilliseconds;
    }

    /// <summary>
    /// 记录一次 no-GC region 启动尝试。
    /// </summary>
    public void RecordNoGcRegionStartAttempt(bool started)
    {
        _ = Interlocked.Increment(ref _noGcRegionStartAttempts);
        Volatile.Write(ref _noGcRegionStartedLastFrame, started ? 1 : 0);
        if (!started)
        {
            _ = Interlocked.Increment(ref _noGcRegionStartFailures);
        }
    }

    /// <summary>
    /// 记录一帧 no-GC region 成功结束。
    /// </summary>
    public void RecordNoGcRegionSuccess()
    {
        _ = Interlocked.Increment(ref _noGcRegionSuccessfulFrames);
    }

    /// <summary>
    /// 记录 no-GC region 结束失败。
    /// </summary>
    public void RecordNoGcRegionEndFailure()
    {
        _ = Interlocked.Increment(ref _noGcRegionEndFailures);
        Volatile.Write(ref _noGcRegionStartedLastFrame, 0);
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

    /// <summary>
    /// 线程安全地累加 UI 字体缺字码点数。
    /// </summary>
    /// <param name="delta">增量。</param>
    public void AddUiFontMissingGlyphs(long delta)
    {
        if (delta <= 0)
        {
            return;
        }

        _ = Interlocked.Add(ref _uiFontMissingGlyphs, delta);
    }
}
