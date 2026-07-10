# Plan 16 — M15 性能加固与目标硬件证据账本

> **状态迁移（2026-07-10）**：本文件保留详细设计与历史 checkbox；当前状态、顺序和完成条件以 [`plan/tasks/README.md`](tasks/README.md) 为唯一真相源。不要在本文件新增 live task；设计变化仍须同步到这里。

> **DOC-002 历史证据口径（2026-07-10）**：后文 checkbox 与“已通过/已完成”叙述冻结自旧计划快照 `179efc3a`，迁移基线为 `5af1541f`，均不构成 live 状态；证据等级以 [稳定 Evidence Index](../docs/evidence-index.md) 为准。未入索引的 `artifacts/`、`BenchmarkDotNet.Artifacts/`、`scratch/` 仅是可再生历史线索；替代报告与重跑命令见 [DOC-002 校正报告](../docs/evidence-2026-07-10-doc-002-legacy-plan-audit.md)。

> **DOC-002 性能证据校正**：后文 `artifacts/benchmark-run-ca-*` 目录只代表历史 Dry 入口探针；正式规模数据与未达目标结论以 [`PERF-003` 稳定报告](../docs/evidence-2026-07-10-perf-003-ca-throughput.md) 为准，目标硬件状态以 canonical `PERF-*` 为准。

> 本文件是 M15 的性能证据账本，承载 Engine Core、Web-first UI Runtime、Showcase Demo Game 和发行模式的性能门禁状态。它不新增子系统，只判断现有实现是否有足够证据证明可交付。
> 技术依据：`../docs/PixelEngine-架构与需求设计.md` §1.4、§12、§17.3、§19，`00-conventions-and-techstack.md`，`14-testing-benchmarking.md`，`15-build-packaging-distribution.md`。
> 状态标记：只使用 `- [x]`、`- [ ]`、`- [!]`。进行中状态必须拆成已完成子项与未完成或阻塞子项。

---

## 1. 当前产品职责

- [x] 本文件负责性能证据，不负责重新设计 CA、物理、渲染、Web-first UI Runtime、Unity-like Editor 或 Showcase Demo Game。
- [x] 本文件负责把性能纪律转化为可审计账本：SoA、零分配、多线程、SIMD、bounds-check、GC、GPU、dirty-rect、过载降级、内存上限、目标硬件和 profiling。
- [x] 本文件负责明确 M15 性能出口：目标硬件长跑、BenchmarkDotNet、硬件计数器、反汇编、真实窗口 HUD 与 release 编译模式证据必须闭合。
- [x] 本文件负责区分本机短基准、局部 benchmark、脚本探针、预检状态和最终验收；局部数据只能说明方向，不能写成完成。
- [x] 本文件负责保护不变式：CPU simulation authoritative、4-pass checkerboard、单缓冲原地、颜色不入 cell、绝不追帧、GPU pass 非权威。

---

## 2. 状态总览 checklist

- [!] 文档总状态：M15 性能证据未完成。工程纪律大多已落地，但 AVX-512、硬件计数器、目标硬件 cells/frame、目标帧预算和 full-active CA 达标仍阻塞。
- [x] SoA、Damage lane 预算、颜色不入 cell、RenderStyle 不写回 cell 已有源码和 plan 证据。
- [x] 零托管分配、JobSystem、并行覆盖、dirty-rect、过载降级、GC 协调和 profiling HUD 的工程门禁已有测试或基准证据。
- [x] R2R 主发行与 AOT 次发行的编译模式审计已接入 plan/15，但 release runner 的最终证据仍归 M15 阻塞。
- [!] full-active CA 当前未达最终目标：`docs/benchmark-reports/2026-07-02-plan14-short.md` 记录 `StepJobSystem(FullActiveLiquid)` 约 262,144 active cells / 38.327ms，约 54.7K active cells / 8ms，低于 2–4M active cells / 8ms 目标。

2026-07-10 本机正式规模校准：新增 `CellThroughputBenchmark.FullActive2M`（23×23 active chunks，2,166,784 cells），Ryzen 7 5800X / .NET 10.0.8 / 8 worker（8 physical cores）的 `StepJobSystem` 平均 12.965ms，折算约 1.337M cells/8ms；movement 热路径局部优化已提交，但仍不能关闭 full-active 目标，详见 `docs/evidence-2026-07-10-perf-003-ca-throughput.md`。
- [!] 硬件计数器未闭合：当前工具能报告平台或权限边界，但 `Cache Misses` 和 `Branch Mispredictions` 真实列需要 elevated ETW 或目标 runner。
- [!] AVX-512 未闭合：当前本机 Ryzen 7 5800X 只有 AVX2 证据，不能验证 Vector512 可用性或降频净损。
- [!] 目标硬件未闭合：缺 win-arm64、linux-x64、linux-arm64、osx-x64、osx-arm64 代表硬件或可信 runner 的 BenchmarkDotNet 长跑。
- [!] `ready`、`counters_present`、`target_performance_evidence_attached_pending_review` 只表示入口或证据待审，不是性能验收通过。

---

## 3. 已实现证据 checklist

- [x] SoA 数据布局已落地：`Material`、`Flags`、`Lifetime`、`Temperature`、`Render` 独立连续数组，AoS 仅工具或编辑路径。
- [x] Damage lane 预算已登记：per-cell 从 4B 增至 5B，常驻 chunk 从 16KB 增至 20KB，plan/03、plan/07、plan/16 口径对齐。
- [x] Damage 平面遵守单缓冲原地更新，刚体像素通过 `IRigidDamageSink` 路由重建而不累加 Damage。
- [x] 颜色不入 cell：材质纹理、温度 glow、RenderStyle、裂纹、描边和 debug overlay 均在渲染相位生成。
- [x] CA、粒子、render buffer、反应、温度、序列化和 JobSystem 多 worker 派发均记录为稳态零托管分配目标，并有 `MemoryDiagnoser` 或单测覆盖。
- [x] Demo 破坏 API 已走安全相位命令队列：`DamageCircle`、`DamageBeam`、`AddHeat` enqueue/flush 后写 Damage、标 dirty、触发 KeepAlive，且稳态 0B 分配有测试覆盖。
- [x] 多线程覆盖已落地：CA checkerboard、Box2D task bridge、render buffer、CCL/形状重建、粒子积分、温度 stencil、序列化字节准备均走持久线程池或后台字节路径。
- [x] 小任务回退已落地：活跃任务或活跃 chunk 少时回退单线程，避免 barrier 开销主导。
- [x] SIMD 规则已落实：温度 stencil、palette→BGRA、bulk fill/clear、dirty flag 扫描等可向量化；sand/liquid movement 明确保留 scalar。
- [x] RenderStyle 着色质量档已接入：开启时禁用 zoom palette 行复制，按世界空间逐像素计算 BGRA；关闭时恢复 palette 快路径。

当前实现补充：Full 样式档的整数放大视口改为按世界 cell 一次采样、按屏幕重复填充；稳定帧缓存按 camera/material/resident chunk/dirty 元数据校验复用，活动温度 chunk 保守地禁用整帧复用但不再阻塞其他 chunk 的 palette 快路径。`RenderBufferViewportBenchmarks` 在 Ryzen 7 5800X / .NET 10 上记录 1280x720 强制重建 6.458ms、稳定复用 3.137us，均为零托管分配。
- [x] bounds-check 消除流程已建立：热路径使用 `MemoryMarshal.GetArrayDataReference`、`Unsafe.Add` 或 fixed 指针漫游，并以 disassembly guard 守门。
- [x] GC 策略已建立：Workstation 与 Server GC 实测定档，`EngineGcCoordinator` 串行化 GC latency mode 与 NoGCRegion 等进程级状态，并由 `PerformanceHardeningMemoryDisciplineTests` 覆盖失败/结束路径的 gate 释放与并发 latency mode 阻塞语义。
- [x] GPU 下放边界已明确：光照、bloom、高密度粒子和可选非权威 pass 可走 GPU；权威 cell 网格留 CPU，无 GPU readback 卡流水线。
- [x] dirty-rect 证据已建立：静止 chunk 收缩为空、sleeping 区零迭代、满屏静止 vs 满屏激活基准用于证明静止边际成本趋近零。
- [x] 过载降级链已实现：热场、光照、远处 active chunk、整体 sim 30Hz、真实减速五级降级，绝不 accumulator 追帧。
- [x] RenderStyle 质量降级已接入一级过载，并与二级光照降级独立。
- [x] Web-first UI Runtime 相位计时底座已落地：HUD 可分列 `ui.update`、`ui.paint`、`ui.upload`、`ui.composite`，静态 UI paint 为 0，dirty upload smoke 已覆盖。
- [x] 大世界内存上限已记录：常驻 world 有可配置上限，LRU 驱逐、RLE+LZ4、Damage lane 20KB/chunk 预算已纳入。
- [x] profiling 工具链已接入：BenchmarkDotNet、MemoryDiagnoser、DisassemblyDiagnoser、DOTNET_JitDisasm、真实窗口 HUD、window_frame_probe、GPU compute timer query 生命周期验收和 release 编译模式审计均有路径；本地 BenchmarkDotNet 入口已加固为 `tools/run-benchmark.ps1` 隔离工作副本执行，排除 `.claude/worktrees` 同名项目导致的 0 benchmark 执行假成功，并保留 `runtimes/<rid>/native` 以覆盖 Box2D/RmlUi native benchmark。

---

## 4. 未完成目标 checklist

- [ ] 在目标硬件上证明 full-active CA 达到 2–4M active cells / 8ms 或重新校准产品可接受目标并更新架构 §1.4 与 §12.8。
- [ ] 在目标硬件上证明帧预算：CA p99 ≤8ms、渲染+光照+post p99 ≤4ms、物理+重建 p99 ≤4ms、逻辑+音频 p99 ≤1ms。
- [ ] 在 6-RID 代表硬件或可信 runner 上跑 `CellThroughputBenchmark.StepJobSystem`，覆盖 FullActiveLiquid 和典型 dirty-rect 场景。
- [ ] 在 elevated ETW 或等价权限下采集 hardware counters，证明瓶颈分析基于 cache misses 与 branch mispredictions，而不是带宽估算。
- [ ] 在 AVX-512 目标机上验证 Vector512 gate、AVX-512 codegen 和无降频净损。
- [ ] 在 Release R2R 产物上证明 Tier-1 重 JIT 和 runtime SIMD light-up，不能只看 Debug/JIT 或本机短样本。
- [ ] 在 NativeAOT 产物上证明显式 ISA 生效，x64 有 ymm 或 avx512 变体 zmm，arm64 有 NEON，不能用 skip 报告代替。
- [ ] 在 Showcase Demo Game 的熔岩矿洞高活跃场景中跑目标硬件长跑，证明持续破坏、Damage lane、碎屑、粒子和 UI 不破坏帧预算。
- [ ] 在 Web-first UI Runtime 的 RmlUi/Ultralight 后端最终形态下补 UI phase 目标硬件证据，证明 UI 尖刺只丢渲染帧，不拖慢 sim。

---

## 5. 证据债 / 阻塞 checklist

- [!] 阻塞：本机 Short benchmark 只能作为校准，不是目标硬件长跑，也不是 full-active CA 达标证据。
- [!] 阻塞：full-active CA 当前数据明显低于最终目标，不能因为 typical dirty-rect 表现可用而勾选总体帧预算达标。
- [!] 阻塞：局部 benchmark、单方法 disassembly、local probe 和 process smoke 只能证明工具链入口，不代表 M15 性能验收完成。
- [!] 阻塞：`tools/hardware-counter-preflight.ps1` 报 `blocked_non_admin`、`ready` 或 `counters_present` 时，只说明权限或列检查状态，不说明 cache/branch 计数已纳入结论。
- [!] 阻塞：`tools/performance-target-evidence-preflight.ps1` 报 `target_performance_evidence_attached_pending_review` 时，只代表 evidence manifest 完整且待人工复核，不代表性能完成。
- [!] 阻塞：AVX-512 无目标硬件时不能通过思想判断代替实测；需要 `vector512HardwareAccelerated=true`、`avx512Enabled=true`、`noNetDownclockLoss=true` 等机器可读字段和 BenchmarkDotNet 报告。
- [!] 阻塞：cells/frame 证据必须逐 RID 声明 `rid`、`representativeHardware=true`、`benchmarkDotNet=true`、`activeCellsPerFrame>=2000000`、`caFrameMs<=8`、`measuredIterations>=3`。
- [!] 阻塞：frame budget 证据必须来自玩家包真实窗口的引擎诊断长跑，含 `demoScene=lava-mine`、`playerPackageRun=true`、`realWindowRun=true`、`fixedTickNoCatchUp=true`、`degradationPolicyObserved=true`、`frameTimelineCaptured=true`、`sampleSeconds>=60`、`frameSamples>=3600` 和各 phase p99 字段。
- [!] 阻塞：不同 benchmarkRunId、gitCommit、SHA256 或 run identity 的报告不能拼接成一个完成证据包。
- [!] 阻塞：UI native 后端、IME、真实窗口透明合成和发行 gate 未闭合时，UI 相位计时不能被写成最终 Web-first UI Runtime 性能完成。

---

## 6. 验证命令与证据路径 checklist

- [x] 当前短样本证据路径：`docs/benchmark-reports/2026-07-02-plan14-short.md` 记录 Ryzen 7 5800X / .NET 10.0.8 的 Short 校准，并明确 full-active CA 未达目标。
- [x] 真实窗口 HUD 样本路径：`docs/runtime-reports/2026-07-04-performance-hud-steady-window-samples.md` 记录 CPU busy、GPU elapsed、present wait、滚动百分位和负载计数样本。
- [x] 目标硬件预检入口：`tools/performance-target-evidence-preflight.ps1` 检查 performance manifest、scope/hash、benchmarkRunId、gitCommit、cells/frame、frame budget、AVX-512 与硬件计数器字段；缺 manifest / schema / scope 时必须报告 `blocked_missing_target_performance_manifest`、`blocked_invalid_target_performance_evidence`、`blocked_missing_target_performance_scope_evidence`，完整待审状态只能是 `target_performance_evidence_attached_pending_review`；scope 必须覆盖 `avx512_downclock_net_loss`、`hardware_counters_cache_branch`、`frame_budget_target_hardware` 与逐 RID 的 `cells_frame/<rid>`；机器可读字段必须覆盖 `targetCpuName`、`dotnetVersion`、`benchmarkRunId`、`gitCommit`、`vector512HardwareAccelerated`、`avx512Enabled`、`noNetDownclockLoss`、`elevatedEtwKernelSession`、`cacheMissesPresent`、`branchMispredictionsPresent`、`targetHardware`、`source`、`scenario`、`demoScene`、`sampleSeconds`、`frameSamples`、`fixedTickNoCatchUp`、`playerPackageRun`、`realWindowRun`、`degradationPolicyObserved`、`frameTimelineCaptured`、`caP99Ms`、`renderP99Ms`、`physicsP99Ms`、`logicAudioP99Ms`、`representativeHardware`、`activeCellsPerFrame`、`caFrameMs`、`measuredIterations`、`iterationCount`。
- [x] 硬件计数器预检入口：`tools/hardware-counter-preflight.ps1` 检查平台、管理员权限、BenchmarkDotNet hardware counter 列和报告边界。
- [x] 关键 BenchmarkDotNet 入口：`bench/PixelEngine.Benchmarks`，重点覆盖 `CellThroughputBenchmark.StepJobSystem`、FullActiveLiquid、dirty-rect、JobSystem、RenderStyle、GameUi allocation；本地 `tools/benchmark-regression.ps1` 回归门禁已加固为按 `Mean` 表头取值、参数化行必须唯一匹配，防止 Error/StdDev 时间列或多参数第一行误判为通过；实际运行委托 `tools/run-benchmark.ps1` 在临时工作副本中执行并拒绝 `Generate Exception` / `DllNotFoundException` / `There are not any results runs` / `executed benchmarks: 0`，避免忽略 worktree 污染 BDN 项目发现或 native 缺失结果假成功。
- [x] full-active CA 热路径局部优化证据：checkerboard 装桶阶段保存已验证的 `ChunkNeighborhood` 并传给 `ChunkUpdater` / `NeighborWindow`，避免 active chunk 更新阶段重复 `ResolveNeighborhood`；`NeighborWindow` 同步缓存 3×3 驻留 chunk 对象，movement / reaction 的跨 chunk dirty 与 KeepAlive 标记复用 target slot 直接取 chunk，不再额外回查 chunk map；`ChunkUpdater` 的中心 chunk movement / lifetime dirty 标记改为直接本地写入，避免同 chunk 内移动在热路径额外回查 chunk map；movement helper 复用源材质 density / dispersion，`NeighborWindow.CanDisplaceForMove` 在水平扫描阶段一次解析 target slot / local 后直接读取 material / flags，移除 `ChunkUpdater.CanDisplace` 的双重 `GetMaterial` / `GetFlags` 地址解析；`NeighborWindow.TryReadNonEmptyMoveTarget` 在垂直下落扫描中保持空目标只读 material，并对非空目标一次解析读取 material / flags，避免 `TryMoveDown` 的 `GetMaterial` 后再 `GetFlags` 重复地址解析；movement 的最终可置换判断、RigidOwned damage 通知、cell swap、Damage 清零与两端 parity 标记已合并到 `NeighborWindow.TryMoveCell`，一次计算 source/target slot 与 local index 后直接操作 SoA ref，避免 `TryMoveTo` 先 `CanDisplace`、再 `Swap`、再多次 `GetFlags/SetFlags` 的重复地址解析；本轮新增 `ChunkUpdater` 中心 chunk `lifetimeBase` + `Unsafe.Add` ref local，常见 lifetime=0 路径不再通过 `NeighborWindow.GetLifetime/GetMaterial/GetFlags` 重复 slot/local 解析，只有非零 lifetime 才进入 `ProcessLifetime` 并在 sink 后重读本地 SoA；movement 后直接复用当前 `material` 作为 `activeMaterial`，避免对已知活跃 cell 再做一次 `NeighborWindow.GetMaterial(activeX, activeY)`；`CheckerboardSchedulerTests.StepCaWithJobSystemResolvesNeighborhoodOncePerActiveChunk` 锁定每 active chunk 每步一次邻域解析，`CheckerboardSchedulerTests.StepCaInternalMoveMarksCenterDirtyWithoutExtraChunkLookup` 锁定内部移动只发生 3×3 邻域解析的 9 次查表，`CheckerboardSchedulerTests.StepCaBoundaryMoveMarksKeepAliveWithoutExtraChunkLookup` 锁定跨 chunk movement 也只发生邻域解析的 9 次查表，`PerformanceHardeningToolingDisciplineTests.SimulationHotNeighborAccessUsesUnsafeBaseRefs` 锁定 lifetime 基址漫游与 active material 复用；`artifacts/benchmark-run-ca-density-cache`、`artifacts/benchmark-run-ca-move-cell`、`artifacts/benchmark-run-ca-horizontal-displace`、`artifacts/benchmark-run-ca-vertical-target` 与 `artifacts/benchmark-run-ca-neighbor-window-chunk-cache` 的 Dry BDN 运行证明 `StepJobSystem` 三档 profile 入口可执行。该项只关闭局部重复读取风险，不代表 full-active CA 已达最终目标。
- [!] 最终 cells/frame 命令：在每个代表 RID 上运行 Release BenchmarkDotNet，保留完整报告和 SHA256，再交给 `performance-target-evidence-preflight`。
- [!] 最终 hardware counter 命令：在 Windows elevated ETW 或等价目标 runner 上采集 `Cache Misses` 与 `Branch Mispredictions`，不能用列缺失报告替代。
- [!] 最终 frame budget 命令：用真实窗口或 headless 诊断长跑至少 60 秒，导出每 phase p99、样本数、场景名和固定 tick 无追帧字段。
- [!] 最终 AVX-512 命令：在 AVX-512 机器上跑 Vector512 gate 与对照 benchmark，报告启用和禁用的净差异。

---

## 7. 依赖与下一闭合节点 checklist

- [x] 上游依赖：plan/02 提供 JobSystem、GC 协调、诊断计时、POH/NativeMemory 和对象池。
- [x] 上游依赖：plan/03 提供 SoA、Damage lane、dirty-rect、checkerboard、parity 和 bounds-check 消除热路径。
- [x] 上游依赖：plan/04、plan/05、plan/06、plan/07、plan/08、plan/09 提供温度、粒子、物理、流式、渲染和 GPU 的被审计实现。
- [x] 上游依赖：plan/13 提供 Showcase Demo Game 的熔岩矿洞高活跃 profiling 场景。
- [x] 上游依赖：plan/14 提供 BenchmarkDotNet、反汇编、preflight、真实窗口 probe 和 CI 纪律测试。
- [x] 协同依赖：plan/15 提供 R2R/AOT、release artifact、SIMD 探针和 active RID 证据来源。
- [x] 协同依赖：plan/20 提供 Web-first UI Runtime 的 UI phase、dirty upload、RmlUi/Ultralight 后端和真实窗口合成证据来源。
- [ ] 下一闭合节点：继续补 full-active CA 热路径优化或目标重校准，再跑本地 BenchmarkDotNet 对照与目标硬件长跑。
- [ ] 下一闭合节点：补 elevated hardware counters 和 AVX-512 机器实测，再更新本账本阻塞项。
- [ ] 下一闭合节点：把 Showcase Demo Game 熔岩矿洞、Web-first UI Runtime 和发行产物放进同一目标硬件长跑证据包。
- [!] M15 出口阻塞：只要 full-active CA、AVX-512、硬件计数器、目标硬件、frame budget 任一项未闭合，plan/16 不能改为完成。

---

## 8. 验收标准 checklist

- [x] SoA 与预算验收：sim 热数据 SoA、Damage lane 5B/cell、20KB/chunk、颜色不入 cell、RenderStyle 不写回 cell。
- [x] 零分配验收：CA、粒子、render buffer、反应、温度、序列化、JobSystem 多 worker 派发均有 0B 分配目标和证据路径。
- [x] 多线程验收：CA checkerboard、Box2D task bridge、render buffer、CCL、粒子、温度、序列化均不使用每帧 `Parallel.For`。
- [x] dirty-rect 验收：静止区零迭代和满屏静止近零 sim 成本已有证据路径。
- [x] 过载降级验收：五级降级和 RenderStyle/UI present cadence 已接入，保持绝不追帧。
- [x] GC 验收：GC latency mode 与 NoGCRegion 由 `EngineGcCoordinator` 串行化，`PerformanceHardeningMemoryDisciplineTests.TryBeginNoGcRegionReleasesCoordinatorGateWhenStartFails` / `NoGcRegionCoordinatorBlocksLatencyModeChangesUntilRegionEnds` 已锁定 gate 释放与并发阻塞纪律，压测下 Gen0 不增长的证据路径已登记。
- [!] SIMD 验收：AVX2 与 scalar fallback 已有基础，AVX-512 目标硬件实测仍阻塞。
- [!] 延迟+分支验收：硬件计数器入口已建立，但真实 cache miss 和 branch misprediction 数据仍阻塞。
- [!] cells/frame 验收：当前 full-active CA 未达 2–4M active cells / 8ms 目标，必须优化或正式重校准后再勾选。
- [!] 帧预算验收：目标硬件 p99 长跑仍缺，不能用本机短样本或脚本探针替代。
- [!] UI 相位验收：ManagedFallback/dirty upload 底座可用，但 RmlUi/Ultralight 最终后端和真实产品路线性能证据仍未闭合。
- [x] 零冲突验收：本账本所有已完成项均保持 CPU sim 权威、4-pass checkerboard、单缓冲原地、颜色不入 cell、GPU 非权威和不追帧。

---

## 9. 提交节点 checklist

- [x] 已完成历史节点：`docs(plan): 增加跨切面性能加固审计清单(plan/16)`。
- [x] 已完成历史节点：随各子系统提交同步勾选 SoA、零分配、多线程、dirty-rect、GC、RenderStyle、UI phase 和 profiling 工具链条目。
- [ ] 待完成 M15 节点：`perf(core): 优化或重校准 full-active CA cells/frame 并补目标硬件 BenchmarkDotNet 证据`。
- [ ] 待完成 M15 节点：`perf(core): 闭合硬件计数器、AVX-512、帧预算与 Showcase Demo Game 长跑证据`。
- [!] 阻塞提交不得提前写：只基于本机短基准、pending review、ready、counters present、local probe 或 process smoke 的提交，不能写成性能验收完成。
