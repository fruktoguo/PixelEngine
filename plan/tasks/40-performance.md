# 性能任务

本轨道区分“性能设施存在”和“产品性能达标”。旧计划中已完成的 BenchmarkDotNet、Profiler、SIMD 和 preflight 基础设施归 `BASE-016`；下列任务才是当前真实性能关键路径。

## 基线与首要瓶颈

- [x] `PERF-001` 在当前 HEAD 建立可复现的正式性能基线，替换 `NA`、Dry/ColdStart 单次报告和旧 commit 数据。
  - 优先级：P0。
  - 依赖：`CI-001` 可并行；本地无需等待远端。
  - 设计来源：`plan/14-testing-benchmarking.md`；`plan/16-performance-hardening.md`。
  - 验收：至少覆盖 full-active CA、typical dirty、render-buffer、physics、粒子和 UI allocation；每项有 warmup、多次 measured iteration、commit/RID/硬件信息和稳定报告路径。

- [x] `PERF-002` 优化 CPU `BuildRenderBuffer` 路径，使普通 720p/1080p Demo 不再由 render-buffer 主导掉帧。
  - 优先级：P0。
  - 依赖：`PERF-001`。
  - 当前事实：同场景同硬件优化后 0 active-cell 窗口短跑 render-buffer 平均 7.475ms、p99 13.079ms（前值 15.362ms/22.644ms）；1280x720 隔离基准强制重建平均 6.458ms，稳定复用 3.137us，均为 0B allocation。
  - 设计来源：`plan/08-rendering.md`；`plan/16-performance-hardening.md`；架构 §1.4/§12。
  - 验收：用同场景、同分辨率、同硬件前后对比；render-buffer 与 render+lighting+post 分项满足正式校准预算；画面逐像素/容差回归通过；稳态零分配。

- [~] `PERF-003` 让 full-active CA 达到 2–4M cells/8ms 目标，或基于代表硬件和产品场景正式重校准架构指标。
  - 优先级：P0。
  - 依赖：`PERF-001`。
  - 当前事实：历史正式记录为 262,144 cells/38.327ms，距离原目标约 36.6–73.1 倍。
  - 设计来源：`plan/03-simulation-kernel.md`；`plan/16-performance-hardening.md`；架构 §12.7/§12.8。
  - 验收：不得只优化 benchmark fixture；保留质量守恒和 checkerboard 不变式；若重校准，必须同步产品分辨率、活跃率假设、降级策略和架构置信度。

## 零分配与数据结构

- [ ] `PERF-004` 消除 `SimulationKernel` 批量编辑捕获 lambda和 `PhysicsSystem.SyncStep` 实例方法组委托的帧/交互路径分配。
  - 优先级：P1。
  - 依赖：`BASE-003`、`BASE-006`。
  - 验收：改为无捕获 callback、泛型静态路径或显式循环；BenchmarkDotNet/分配测试证明对应调用 0 B；行为测试全绿。

- [ ] `PERF-005` 池化刚体破坏重建中的 Dictionary/HashSet/List/plans/worker scratch，控制高频破坏时的 GC 和延迟尖峰。
  - 优先级：P1。
  - 依赖：`ARCH-003` 可并行。
  - 验收：破坏 burst benchmark 报告分配和 p99；池对象完整清理；CCL/凸分解/碎片守恒测试不回归。

- [ ] `PERF-006` 把 resident chunk 批量加入改为单次 snapshot rebuild 和指数扩容，消除一批 N 个 chunk 的 O(n²) 复制/分配。
  - 优先级：P1。
  - 依赖：`BASE-007`。
  - 验收：提供 batch mutation API；单批只重建一次稳定 snapshot；1/16/64/256 chunk benchmark 与驻留正确性测试通过。

- [ ] `PERF-007` 复用或池化 Demo 坍塌扫描的四组工作数组，禁止最大半径单次约 4.1MB 分配和跨帧重试重复分配。
  - 优先级：P1。
  - 依赖：`BASE-015`。
  - 验收：默认/最大半径分配测试；多岛、边界、支撑地形和继续开火回归通过；scratch 生命周期明确。

## 目标硬件证据

- [!] `PERF-008` 取得玩家包真实窗口至少 60 秒、3600 帧的 phase p99 和降级策略证据。阻塞：`PERF-003` 尚未闭合，且需要冻结候选 commit/目标硬件。
  - 优先级：P1。
  - 验收：同源 manifest 覆盖 CA、render、physics、logic/audio p99，固定步长不追帧、player package、real window、timeline 和降级观测。

- [!] `PERF-009` 完成 ETW Cache Misses/Branch Mispredictions、AVX-512 降频净损和反汇编证据。阻塞：需要管理员 ETW session、相应 CPU 和冻结 commit。
  - 优先级：P2。
  - 验收：不是权限预检或 `ready`；报告必须包含实际计数列、目标 CPU、运行时版本、指令和净性能结论。

- [!] `PERF-010` 完成目标 GPU 的 CPU/GPU 粒子长基准。阻塞：需要代表 GPU、驱动信息和至少 300 measured frames。
  - 优先级：P2。
  - 验收：CPU/GPU 同 commit/run id；粒子数至少 100K；GPU 路径无 CPU stamp；比较报告可从原始 probe 重算且 GPU 更快。

- [ ] `PERF-011` 完成温度/反应热点、GC 模式和逻辑/音频预算的目标场景定档。
  - 优先级：P2。
  - 验收：真实内容规模覆盖温度 stencil、reaction hit/miss、Server/Concurrent GC 组合和 logic/audio phase；报告包含分配、pause、cache/branch、p99，并选定唯一生产 GC 配置。

- [!] `PERF-012` 完成 R2R Tier-1 runtime light-up 与 NativeAOT ISA 证据。阻塞：需要 `EVID-003` 对应 x64/arm64 runner 和候选发行包。
  - 优先级：P2。
  - 验收：R2R 证明运行时重 JIT/light-up；AOT x64 证明目标 YMM/ZMM 指令，arm64 证明 NEON；不得以 load-only 或 skip 代替。
