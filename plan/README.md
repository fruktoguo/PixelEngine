# PixelEngine 计划总览（plan/ 索引）

本文件夹是 PixelEngine 的完整实现计划。每个文档是一个子系统/阶段的完整、带勾选条目的计划，覆盖目标、技术栈、详细设计、实现清单、验收标准、依赖与提交节点。一步到位、无 MVP、无临时实现——细节见 `../AGENTS.md`。

权威设计依据：`../docs/PixelEngine-架构与需求设计.md`（架构文档，19 章）。技术栈定稿：`00-conventions-and-techstack.md`。

## 文档清单

| # | 文档 | 范围 | 关键依赖 |
|---|---|---|---|
| 00 | `00-conventions-and-techstack.md` | 技术栈定稿、解决方案结构、全局约定（锚文档） | — |
| 01 | `01-project-setup.md` | 解决方案/项目骨架、CPM、Directory.Build、native 构建、CI、git | 00 |
| 02 | `02-core-infrastructure.md` | Core：数学/内存/线程池+barrier/RNG/事件总线/时间/诊断/常量 | 01 |
| 03 | `03-simulation-kernel.md` | CA 内核：CellGrid(SoA)/chunk/dirtyrect/checkerboard/movement/parity/KeepAlive | 02 |
| 04 | `04-materials-reactions-temperature.md` | 材质定义/反应表([tag]展开)/温度场/相变/数据驱动(含 Content 加载器与 schema) | 03 |
| 05 | `05-particles-lifecycle.md` | 自由粒子池/cell↔particle handshake/生命周期 | 03 |
| 06 | `06-physics-collision-rigidbody.md` | CCL/marchingsquares/DP/凸分解/Box2D 桥+task 桥/两世界同步/角色控制器 | 02、03、Interop |
| 07 | `07-world-streaming-serialization.md` | chunk 驻留/border ring/流式装卸/内存上限/存档/id 重映射/版本迁移 | 03、04 |
| 08 | `08-rendering.md` | Silk.NET/纹理流式(PBO)/粒子合成/光照/bloom/post/相机 | 02、03、05 |
| 09 | `09-gpu-compute.md` | GPU 计算：光照(含 Radiance Cascades)/bloom/高密度粒子/可选非权威 sim pass | 08 |
| 10 | `10-audio.md` | OpenAL/positional source 池/事件驱动材质音效/限频去重 | 02 |
| 11 | `11-scripting-system.md` | 项目引用模型/Behaviour&Component API/Roslyn+ALC 热重载/世界脚本接口/IDE 启动 | 02、Hosting |
| 12 | `12-editor-tooling-ui.md` | ImGui 管理UI：面板框架/材质编辑器/世界编辑/调试叠层/检视器/资源浏览/sim 控制 | 08、03、06、11 |
| 13 | `13-demo-game.md` | 落沙游戏 Demo：可操作角色/玩法/关卡/内容（仅依赖引擎公开 API） | 11、全部引擎子系统 |
| 14 | `14-testing-benchmarking.md` | 测试策略/性质测试/oracle 比对/基准/CI 门禁 | 02–13 |
| 15 | `15-build-packaging-distribution.md` | 6-RID/R2R/AOT/native dual-build/打包/codesign/分发 | 01、06 |
| 16 | `16-performance-hardening.md` | 跨切面性能加固清单：多线程/内存/SIMD/GC/GPU/profiling | 02–13 |
| 17 | `17-roadmap-execution-order.md` | 执行顺序/依赖图/里程碑映射(M0–M12)/提交节点总表 | 全部 |
| 18 | `18-hosting-runtime.md` | 引擎宿主:Engine 门面/12 相位主循环编排/子系统装配/场景/Play-Edit 模式/过载降级编排/headless | 02–10 |

> 模块归属与编号说明（见 `00-conventions-and-techstack.md §5.1`）：数据模型类型(`MaterialDef`/`Reaction`/`CellType`/`AudioCueSet`)归 `Simulation`、事件类型(`AudioEvent`)归 `Core`；`PixelEngine.Content` 无独立文档,其设计分布于 `plan/04`(加载器+schema)、`plan/07`(id 重映射)、`plan/12`(编辑器热重载);**12 相位帧循环的编排者是 `Hosting`(`plan/18`)**。

## 执行顺序（vertical-slice-first）

地基先行：**00 → 01 → 02**（约定/骨架/Core）。
世界主体：**03 → 05 → 04 → 07**（CA 内核 → 粒子 → 材质反应 → 世界/存档）。
可视可听：**08 → 09 → 10**（渲染 → GPU 计算 → 音频）。
碰撞物理：**06**（像素碰撞与刚体，刻意置于 sim 稳定之后）。
宿主与可编程：**18 → 11**（引擎宿主/主循环编排 → 脚本系统）。
可编辑：**12**（编辑器 UI）。
集成与交付：**13 → 14 → 15 → 16**（Demo → 测试 → 打包 → 性能加固）。
**17** 给出精确的依赖图与里程碑（M0–M12）映射，实际编码以 17 为节奏表。

> 顺序中部分项可并行（如 08 渲染可与 04/05 并行起步），精确并行关系见 `17`。但不得在前置未完成时声称后置完成。

## 进度总览

| 文档 | 状态 | 备注 |
|---|---|---|
| 00 约定/技术栈 | - [ ] | |
| 01 项目骨架 | - [x] | 解决方案、项目骨架、Box2D dual-build、CI 已完成 |
| 02 Core | - [ ] | |
| 03 CA 内核 | - [ ] | |
| 04 材质/反应/温度 | - [ ] | |
| 05 粒子/生命周期 | - [ ] | |
| 06 物理/碰撞/刚体 | - [ ] | |
| 07 世界/流式/存档 | - [ ] | |
| 08 渲染 | - [ ] | |
| 09 GPU 计算 | - [ ] | |
| 10 音频 | - [ ] | |
| 11 脚本系统 | - [ ] | |
| 12 编辑器 UI | - [ ] | |
| 13 Demo 游戏 | - [ ] | |
| 14 测试/基准 | - [ ] | |
| 15 打包/分发 | - [ ] | |
| 16 性能加固 | - [ ] | |
| 17 路线图 | - [ ] | |
| 18 宿主/运行时 | - [ ] | |

## 使用约定

- 完成条目即勾选；完成文档定义的「提交节点」立即用中文 git 提交（`AGENTS.md §6`）。
- 计划与架构文档/不变式冲突时，先改计划再改代码。
- 阻塞项标 `- [!] 阻塞：原因` 并上报，不写假实现绕过。
