# 已完成能力基线

本文件把旧计划中 1498 个 `[x]` 归并为可依赖的能力包。归并不删除原始设计和测试证据；详细来源由 `90-legacy-coverage.md` 关联。完成状态只代表列出的边界，不自动包含同领域尚未完成的性能、真实窗口或发行任务。

## 工程与基础设施

- [x] `BASE-001` .NET 10/C# 14 解决方案、CPM、Directory.Build、项目依赖方向、Box2D dual-build 本地入口和 32 项目 solution 已建立。边界：不包含远端 CI 成功，见 `CI-001`–`CI-003`。
- [x] `BASE-002` Core 数学、POH/原生内存、持久 JobSystem、barrier、确定性 RNG、事件总线、FrameClock 和诊断计数器已实现并有测试。

## 模拟、物理与世界

- [x] `BASE-003` 64×64 chunk SoA、单缓冲原地 CA、parity、dirty rectangle、4-pass checkerboard、32px move cap、KeepAlive 和 movement rules 已实现。
- [x] `BASE-004` 材质/反应/温度/相变、tag 展开、Content schema、稳定 name↔id 和 Damage 材质字段已实现。
- [x] `BASE-005` 自由粒子池、cell↔particle handshake、生命周期和粒子合成输入已实现。
- [x] `BASE-006` Box2D 桥、task callback、CCL/轮廓/简化/凸分解、刚体 erase→step→inverse stamp、破坏重建和角色控制器已实现。
- [x] `BASE-007` chunk 驻留/border ring/LRU、异步流式、RLE+LZ4、save/load、版本迁移和材质 remap 已实现。

## 渲染与音频

- [x] `BASE-008` OpenGL 窗口、CPU render buffer、PBO 上传、材质纹理、粒子、fog、光照、bloom、post-process、GL compute 和 Radiance Cascades 基线已实现。边界：不包含 ComputeSharp，也不表示帧预算达标。
- [x] `BASE-009` OpenAL、WAV/Ogg 解码、source pool、流式 ambient、材质音效、限频去重和纯托管/静默降级已实现。

## 宿主、脚本与工具

- [x] `BASE-010` EngineBuilder/Engine/EngineContext、12 相位主循环、场景、headless、Play/Edit、过载降级和窗口所有权解耦已实现。
- [x] `BASE-011` Behaviour/Scene/公开脚本 facade、Roslyn 编译、可回收 ALC 热重载、状态迁移、异常隔离、IDE/项目生成已实现。
- [x] `BASE-012` 中性 `PixelEngine.Gui`、ImGui host/CJK 字体以及材质、世界、调试、性能、Inspector、资源、存读档和 sim 控制面板已实现。

## Editor、UI 与 Demo

- [x] `BASE-013` 独立 Editor Shell、项目模型、Hierarchy/Inspector/Scene View/Game View、prefab、`.scene` v2、Settings、Console、Play/Edit 和 Build Settings 自动化主链已实现。
- [x] `BASE-014` `PixelEngine.UI`、ManagedFallback、RmlUi native 基线、same-window/same-GL layer、C#↔UI bridge、输入仲裁、字体和 DOM 数据桥已实现。边界：不包含 Ultralight 真后端和最终人工体验。
- [x] `BASE-015` Showcase Demo 的角色、横向熔岩路线、六武器、破坏/爆炸、动态刚体、材质反应、粒子、光照、音频、HUD/menu/settings/pause/result 和重开自动化链已实现。

## 测试与交付底座

- [x] `BASE-016` xUnit 单元/性质/集成测试、BenchmarkDotNet 项目、反汇编/benchmark/preflight 脚本和本地串行测试入口已建立。边界：不包含远端门禁成功、coverage 阈值和目标硬件结论。
- [x] `BASE-017` build-player、R2R/AOT publish 脚本、app/content 布局、player-only audit、NDJSON 构建协议、SHA256 和本地 win-x64 玩家包已实现。
- [x] `BASE-018` 产品定位、架构文档、21 份子系统设计和 M0–M15 历史路线图已建立。边界：执行状态迁移及其校验单独由 `PLAN-001` 管理。

## 基线使用规则

- 新任务可以依赖本文件列出的能力，但必须同时检查对应“边界”。
- 若回归证明某项基线不成立，先把对应 `BASE-*` 改为 `[!]` 并写明回归，再创建或关联修复任务。
- 历史测试数量、截图或 commit 文本不能单独证明基线；以当前可重跑实现和稳定证据为准。
