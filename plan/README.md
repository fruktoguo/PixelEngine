# PixelEngine 计划文档索引

`plan/` 保存 PixelEngine 的产品、架构和子系统设计。当前任务状态、执行顺序、依赖和完成条件统一由 [`tasks/README.md`](tasks/README.md) 管理；本文件只负责文档导航，不维护第二套进度表。

## 1. 从哪里开始

| 目的 | 入口 |
|---|---|
| 选择下一项工作、查看状态 | [`tasks/README.md`](tasks/README.md) |
| 查看 canonical task 全量覆盖 | [`tasks/90-legacy-coverage.md`](tasks/90-legacy-coverage.md) |
| 理解产品目标 | [`../docs/PixelEngine-核心目标与产品定位.md`](../docs/PixelEngine-核心目标与产品定位.md) |
| 理解架构与性能不变式 | [`../docs/PixelEngine-架构与需求设计.md`](../docs/PixelEngine-架构与需求设计.md) |
| 查看开发纪律 | [`../AGENTS.md`](../AGENTS.md) |
| 查看历史里程碑依赖图 | [`17-roadmap-execution-order.md`](17-roadmap-execution-order.md) |

文档权威顺序为：`AGENTS.md` 与架构不变式 > 产品目标 > 子系统详细设计 > canonical task。发生冲突时，先修正上层设计和任务定义，再实现代码。

## 2. 子系统设计

编号文档保留完整设计、历史清单和历史证据。文件内原有 checkbox 是迁移快照，不再代表当前执行状态，也不应继续新增 live task。

| # | 文档 | 范围 |
|---|---|---|
| 00 | [`00-conventions-and-techstack.md`](00-conventions-and-techstack.md) | 技术栈、解决方案结构、全局约定、依赖边界 |
| 01 | [`01-project-setup.md`](01-project-setup.md) | 工程骨架、CPM、native 构建、CI 与 Git |
| 02 | [`02-core-infrastructure.md`](02-core-infrastructure.md) | Core 数学、内存、线程池、事件、时间与诊断 |
| 03 | [`03-simulation-kernel.md`](03-simulation-kernel.md) | CA 网格、chunk、dirty rectangle、checkerboard、parity 与 KeepAlive |
| 04 | [`04-materials-reactions-temperature.md`](04-materials-reactions-temperature.md) | 材质、反应、温度、相变与 Content schema |
| 05 | [`05-particles-lifecycle.md`](05-particles-lifecycle.md) | 自由粒子、cell/particle 转换与生命周期 |
| 06 | [`06-physics-collision-rigidbody.md`](06-physics-collision-rigidbody.md) | 像素碰撞、刚体、Box2D task bridge 与双向耦合 |
| 07 | [`07-world-streaming-serialization.md`](07-world-streaming-serialization.md) | 世界流式、驻留、存档、ID remap 与迁移 |
| 08 | [`08-rendering.md`](08-rendering.md) | 纹理流式、粒子合成、光照、后处理与相机 |
| 09 | [`09-gpu-compute.md`](09-gpu-compute.md) | GL compute、Radiance Cascades 与非权威 GPU pass |
| 10 | [`10-audio.md`](10-audio.md) | OpenAL、source pool、材质音效与限频去重 |
| 11 | [`11-scripting-system.md`](11-scripting-system.md) | Behaviour/Component API、Roslyn 与 ALC 热重载 |
| 12 | [`12-editor-tooling-ui.md`](12-editor-tooling-ui.md) | Editor 面板、检视器、资源浏览与调试工具 |
| 13 | [`13-demo-game.md`](13-demo-game.md) | Demo 玩法、关卡、反馈与公开 API dogfood |
| 14 | [`14-testing-benchmarking.md`](14-testing-benchmarking.md) | 性质测试、oracle、BenchmarkDotNet 与门禁 |
| 15 | [`15-build-packaging-distribution.md`](15-build-packaging-distribution.md) | R2R/AOT、build-player、native 分发与 Release |
| 16 | [`16-performance-hardening.md`](16-performance-hardening.md) | 多线程、零分配、SIMD、GPU 与 profiling |
| 17 | [`17-roadmap-execution-order.md`](17-roadmap-execution-order.md) | M0-M15 历史依赖图和里程碑定义 |
| 18 | [`18-hosting-runtime.md`](18-hosting-runtime.md) | Engine 门面、12 相位循环、场景与 Play/Edit 模式 |
| 19 | [`19-standalone-editor-app.md`](19-standalone-editor-app.md) | 独立 Editor、authoring、prefab、scene 与构建面板 |
| 20 | [`20-interactive-html-ui.md`](20-interactive-html-ui.md) | PixelEngine.UI、透明合成、输入仲裁与 IME |

## 3. 主任务目录

canonical task 按职责拆成以下轨道：

| 文档 | 内容 |
|---|---|
| [`tasks/10-completed-baseline.md`](tasks/10-completed-baseline.md) | 已确认可依赖的能力基线 |
| [`tasks/20-scope-decisions.md`](tasks/20-scope-decisions.md) | Windows-first、可选 native、M14/M15 与 Demo 路线决策 |
| [`tasks/30-correctness-architecture.md`](tasks/30-correctness-architecture.md) | 正确性、职责边界、故障传播与公开 API |
| [`tasks/40-performance.md`](tasks/40-performance.md) | render buffer、CA、零分配与目标硬件证据 |
| [`tasks/50-product-editor-ui-demo.md`](tasks/50-product-editor-ui-demo.md) | Editor、UI、IME、Demo 与用户文档 |
| [`tasks/60-validation-release.md`](tasks/60-validation-release.md) | CI、测试质量、证据、玩家包与发行 |
| [`tasks/70-evidence-contracts.md`](tasks/70-evidence-contracts.md) | 外部 evidence preflight 的 manifest、scope、阈值与状态语义 |
| [`tasks/90-legacy-coverage.md`](tasks/90-legacy-coverage.md) | 旧计划迁移覆盖和冲突处理 |

当前主序列是：恢复 CI 与可信基线，修复正确性和性能缺口，闭合 M14 产品面，最后以 M15 的真实硬件、远端 CI 和发行证据收口。可选 `OPT-*` 不阻塞 Windows-first 1.0。

## 4. 迁移快照

`2026-07-10`、commit `179efc3a` 的旧计划快照包含 21 份编号文档和 1692 个 checkbox：1498 完成、44 未开始、0 进行中、150 阻塞。它们已归并为 72 个唯一任务，详细映射由 [`tasks/source-coverage.json`](tasks/source-coverage.json) 记录。

旧 checkbox 不会删除，因为它们仍承载设计细节和历史证据；但是完成率不得再从旧 checkbox 机械推导。canonical task 的 `[x]` 只有在实现、测试、验收和要求的证据都完成后才能设置。

## 5. 维护流程

1. 从 [`tasks/README.md`](tasks/README.md) 按依赖选择任务，将唯一状态项改为 `[~]`。
2. 阅读任务引用的编号设计文档；若设计过时，先更新详细设计和 task。
3. 实现并完成任务定义的验证；外部设备、凭据或人工验收缺失时保持 `[!]`。
4. 将任务改为 `[x]`，同步证据索引，并按 `AGENTS.md` 的提交节点立即提交。
5. 修改计划文档后运行任务目录校验：

```pwsh
./tools/validate-task-catalog.ps1
```

该脚本检查任务 ID 唯一性、canonical checkbox 格式、21 份旧计划计数、1692 项快照总数、旧计划映射和审计新增任务覆盖。
