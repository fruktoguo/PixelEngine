# Plan 00 — 技术栈定稿与全局约定（锚文档）

> 本文件锁定整个 PixelEngine 的技术选型、解决方案结构、目标框架与跨切面约定。所有其它 plan 文档以此为准、不得另立选型。权威设计依据：`docs/PixelEngine-架构与需求设计.md`（下称「架构文档」），开发宪法：`AGENTS.md`。
> 状态：`- [ ]` 未开始 / `- [x]` 完成 / `- [~]` 进行中 / `- [!]` 阻塞。

---

## 1. 运行时与语言

- 目标框架：**.NET 10 LTS**，语言 **C# 14**。
- 全项目：`<Nullable>enable</Nullable>`、`<LangVersion>14</LangVersion>`、`<ImplicitUsings>enable</ImplicitUsings>`、file-scoped namespace。
- sim / physics / interop / rendering 项目额外：`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`、`<Optimize>true</Optimize>`（Release）、`<ServerGarbageCollection>` 与 `<ConcurrentGarbageCollection>` 按 §12.4 实测定档（默认先 Workstation+Concurrent，基准后定）。
- CI 开 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`。

## 2. 发行/编译策略（调和「极致性能」与「广兼容」）

- **主发行：CoreCLR 自包含 + ReadyToRun（R2R composite）。** 拿到快启动 + 运行时 CPU 检测 + Tier-1 重 JIT + Dynamic PGO，sim 热方法仍运行时 light-up AVX2/AVX-512/AVX10.2。**不固定 ISA。**（架构 §12.3、§13）
- **次发行：NativeAOT，每 RID 单独产物。** 必须显式 `IlcInstructionSet`，仅限已知硬件分发。
- **开发态：纯 JIT。**
- 原因：NativeAOT 默认退化到 SSE2 baseline，会静默砍掉 SIMD（架构 §2 挑战五、R3）。

## 3. 目标 RID（6 个）

`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。Windows 为主，Linux/Mac 一等支持。

> **长期一等支持目标 RID 全集（不变）** 与 **当前发行激活子集** 分离：上述 6-RID 是长期一等支持全集，架构承诺、`ci.yml`/`plan/16` 的 6-RID 构建/测试矩阵与 `tools/ci-matrix-evidence-preflight.ps1`（cross 用 build-only）保持覆盖全集不收敛，作为 dormant RID 的编译保证后盾。**当前发行激活子集为 Windows 优先**：`win-x64` 为主发行、`win-arm64` 条件激活，`linux-*`/`osx-*` 四 RID 降为 dormant（完整实现、条件激活、非占位，翻 `active:true` 即恢复），激活真相源与门控见 `plan/15` §2.1（`tools/release-rids.json` 单一激活集）。**编辑器 shell（`apps/PixelEngine.Editor.Shell`）为开发期工具**，其发行 RID 与玩家包矩阵解耦、不受 6-RID 玩家矩阵约束（见 `plan/15`、`plan/19`）。

## 4. 技术栈选型表（定稿）

| 领域 | 选型 | NuGet / 来源 | 备注 |
|---|---|---|---|
| 窗口/输入/GL/AL | **Silk.NET 2.x** | `Silk.NET.Windowing`、`Silk.NET.Input`、`Silk.NET.OpenGL`、`Silk.NET.OpenAL` | MIT、.NET Foundation；OpenGL 3.3 Core 基线 |
| 数学/SIMD | **System.Numerics + System.Runtime.Intrinsics** | BCL | `Vector<T>` + `Avx2/Avx512/Avx10v2` + scalar fallback；`Silk.NET.Maths` 仅在与 GL 交互便利处可选 |
| 物理 | **Box2D v3.1.1（vendored C 源，自建绑定）** | `native/box2d/`（git submodule 或 vendored）+ `PixelEngine.Interop` 内 `[LibraryImport]` 薄绑定 | 唯一 native 依赖；自建 task-callback 桥（架构 §14.2）；dual-build 静/动 × 6 RID |
| 编辑器 UI | **Dear ImGui via Hexa.NET.ImGui** | `Hexa.NET.ImGui`、`Hexa.NET.ImGui.Backends`（GL/GLFW 后端）；含 ImGuizmo/ImPlot/ImNodes | 即时模式、AOT 友好、活跃维护；停靠式面板 |
| 脚本编译 | **Roslyn** | `Microsoft.CodeAnalysis.CSharp` | 热重载编译用户脚本 |
| 脚本隔离 | **可回收 AssemblyLoadContext** | BCL `System.Runtime.Loader` | unload/reload 实现 Unity 式迭代 |
| 内容序列化 | **System.Text.Json + 源生成器** | BCL `System.Text.Json` | materials/reactions/场景；AOT 友好 |
| 存档压缩 | **LZ4** | `K4os.Compression.LZ4` | chunk RLE + LZ4（架构 §11） |
| 测试 | **xUnit** | `xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk` | 含质量守恒/边界/oracle 性质测试 |
| 基准 | **BenchmarkDotNet** | `BenchmarkDotNet`（含 `[DisassemblyDiagnoser]`） | perf 门禁 |
| ECS | **不用通用 ECS** | — | sim 是 SoA 网格 DOD；Demo 稀疏实体用轻量手写组件数组（架构 §13.1） |
| 互操作 | **`[LibraryImport]` source-gen** | BCL | 禁新 `DllImport` |
| 跨界缓冲内存 | **POH / NativeMemory** | BCL `GC.AllocateArray(pinned:true)`、`NativeMemory` | 零拷贝双缓冲于 sim/physics/render |

> 库命名陷阱（架构 R10）：存在两个 "Box2D.NET"。本项目**不**用第三方托管绑定，而是自建 `[LibraryImport]` 绑定到 vendored Box2D v3.1.1 C 源，以获得 task 桥完全控制与单 native 依赖。

### 4.1 可选/门控依赖（不在默认构建,采纳时须登记进 CPM 并由 plan/15 按 RID 打包）

| 领域 | 选型 | 来源 | 门控/默认 | 备注 |
|---|---|---|---|---|
| GPU 计算(增项) | **ComputeSharp** | `ComputeSharp`(DX12,Windows-only) | 默认走 Silk.NET GL compute(跨平台);仅 Windows 高性能路径门控启用 | 架构 §9.5 列为可选;plan/09 G1–G4 门控 |
| Ogg Vorbis 解码 | **NVorbis** | `NVorbis`(纯托管) | 默认仅内建 WAV/PCM;需 Ogg 时启用 | 纯托管,符合不变式 #10(不新增 native);plan/10 |
| 交互界面/游戏 UI(HTML) 主后端 | **RmlUi(HTML/CSS 子集,无 JS)** | `native/rmlui/`(vendored C++ 源+静态链 FreeType)+ `PixelEngine.UI` 内 `[LibraryImport]` 薄绑定 | `PixelEngine.UI` 默认 HTML UI 核后端;dynamic-only 落 `runtimes/<rid>/native`,**不进 Box2D dual-build** | RML/RCSS 子集渲染、MIT;符合不变式 #10 处置(仅 Box2D dual-build,其余 native 一律 dynamic-only,见 plan/20 §7);无法忠实渲染 AI 生成的标准 HTML5,故 Demo 大 UI 保留 ImGui 平价实现 |
| 交互界面/游戏 UI(HTML) 可选高保真后端 | **Ultralight** | `native/ultralight/`(WebKit-fork 原生 DLL)+ `PixelEngine.UI` 薄绑定 | 可选、门控启用;dynamic-only 落 `runtimes/<rid>/native`,不进 Box2D dual-build | 标准 HTML5/CSS3/JS 高保真;**商业许可(<$100K 免费档)**、原生 DLL 体积大、AOT 绑定不确定;符合不变式 #10 处置(dynamic-only,见 plan/20 §7) |
| 交互界面/游戏 UI 纯托管基线 | **ManagedFallbackBackend** | `PixelEngine.UI`(纯托管) | 恒在、作无 native 基线回退 | 纯托管盒模型/文本布局子集,零 native 依赖,天然符合不变式 #10;plan/20 |

> 前两项(ComputeSharp/NVorbis)均为「可选增强,有默认回退」,不破坏「广兼容」基线;未启用时不进构建。RmlUi/Ultralight 为 `PixelEngine.UI` 的 HTML UI 后端门控依赖(主/可选),`ManagedFallbackBackend` 为恒在纯托管基线;三者均符合不变式 #10 处置(#10 原文保持,UI native 与 OpenAL/ANGLE 同级 dynamic-only,不进 Box2D dual-build),后端选型与 #10 冲突处理详见 plan/20 §7。其余一律以 §4 主表为准。

## 5. 解决方案结构（定稿）

```
PixelEngine.sln
├─ src/
│  ├─ PixelEngine.Core/          数学/内存(POH,NativeMemory,池)/持久线程池+barrier/RNG/事件总线/时间/诊断
│  ├─ PixelEngine.Interop/       Box2D v3 [LibraryImport] 绑定 + [UnmanagedCallersOnly] task 桥
│  ├─ PixelEngine.Simulation/    CA 内核:CellGrid(SoA)/Chunk/dirtyrect/checkerboard/movement/material/reaction/temperature/particles
│  ├─ PixelEngine.Physics/       CCL/marchingsquares/DP/凸分解(PolyPartition)/Box2D 桥/两世界同步/角色控制器
│  ├─ PixelEngine.World/         chunk hashmap 驻留/border ring/激活半径/相机视口/内存上限 LRU
│  ├─ PixelEngine.Serialization/ chunk 二进制(RLE+LZ4)/world manifest/版本迁移/material name↔id 重映射
│  ├─ PixelEngine.Content/       MaterialDef/Reaction 加载/[tag]展开/name↔id 表/材质纹理/资产
│  ├─ PixelEngine.Rendering/     Silk.NET 封装/窗口/纹理流式(PBO)/粒子合成/光照/bloom/post/GPU compute
│  ├─ PixelEngine.Audio/         OpenAL 封装/positional source 池/事件驱动材质音效
│  ├─ PixelEngine.Scripting/     Roslyn 编译/ALC 热重载/Behaviour&Component API/世界脚本接口/IDE 启动
│  ├─ PixelEngine.Gui/           中性玩家 HUD ImGui host:HexaImGuiBackend/IGuiContext 运行时适配/GuiRenderBridge/GuiFontManager(含 CJK 字体栈);编辑器可复用中性类型但保留面板专用 backend/bridge
│  ├─ PixelEngine.UI/            游戏内交互大 UI(HTML):IGameUiBackend/RmlUi 主后端/Ultralight 可选/ManagedFallbackBackend 基线/C#↔UI 桥/输入仲裁(依赖 Gui,Rendering,Core)
│  ├─ PixelEngine.Editor/        ImGui 管理UI:面板框架/材质编辑器/世界编辑/调试叠层/检视器/资源浏览/sim 控制(依赖 Gui 与各子系统;由编辑器壳注入,Hosting 不再引用)
│  └─ PixelEngine.Hosting/       引擎宿主:Engine 门面/主循环(帧相位)/子系统装配/EngineContext/项目装载/窗口所有权解耦/抽象 GUI+相位[10]钩子接口(编辑器实现由壳注入)
├─ apps/
│  └─ PixelEngine.Editor.Shell/  独立编辑器应用:顶层 EXE/独立窗口/单独进程/in-process 宿主 Edit&Play/GameObject authoring UX/gizmo/prefab/.scene/BuildSettings 编辑器内打包(依赖 Hosting,Editor,Gui;见 plan/19)
├─ demo/
│  └─ PixelEngine.Demo/          落沙游戏(玩家运行时):玩家控制器脚本/输入/相机/UI/关卡内容(仅依赖引擎公开 API;依赖 Hosting+可选 UI,不含 Editor)
├─ tests/
│  ├─ PixelEngine.Simulation.Tests/
│  ├─ PixelEngine.Physics.Tests/
│  ├─ PixelEngine.Serialization.Tests/
│  └─ PixelEngine.Scripting.Tests/
├─ bench/
│  └─ PixelEngine.Benchmarks/    BenchmarkDotNet
├─ content/                      materials.json / reactions.json / 材质纹理 / 音效 / 默认场景
├─ native/                       box2d/ (vendored v3.1.1) + build 脚本(CMake, dual-build per-RID)
├─ tools/                        构建/打包/codesign 脚本
├─ Directory.Build.props         全局 MSBuild 属性(LangVersion/Nullable/分析器)
├─ Directory.Packages.props      中央包版本管理(CPM)
└─ PixelEngine.sln
```

依赖方向（定稿，绝不反向）：主链 `{Demo(玩家运行时), apps/PixelEngine.Editor.Shell(编辑器顶层)} → Hosting → {Scripting, Rendering, Audio, Physics, World, Serialization, Content, Simulation, UI, Gui} → Interop → Core`。补充边：`Editor → {Gui, 子系统}`；`apps/PixelEngine.Editor.Shell → {Hosting, Editor, Gui}`；`UI → {Gui, Rendering, Core}`；`Hosting → Gui`（**Hosting 不再引用 Editor** —— Hosting 暴露抽象 GUI/相位[10]钩子接口，Editor 实现由编辑器壳在开发构建注入）；`Demo → Hosting`（+可选 UI，**不含 Editor**）。要点：**Demo 不再依赖 Editor**（玩家运行时纯净化，玩家 HUD 经 `PixelEngine.Gui` 中性 host 而非 Editor）；**`apps/PixelEngine.Editor.Shell` 与 Demo 同层、均只依赖引擎公开 API**；`PixelEngine.Gui` 为 Rendering 之上、Editor 之下的**中性玩家 HUD GUI host 层**（编辑器可复用中性类型，但面板 backend / dockspace / bridge 仍归 `PixelEngine.Editor`）；`PixelEngine.UI` 只依赖 `{Gui, Rendering, Core}`。Editor 依赖各子系统只读 API；Simulation 不依赖 Rendering/Physics。

### 5.1 类型归属裁定（消除跨文档歧义,各 leaf 文档据此对齐）

- **`MaterialDef` / `Reaction` / `CellType` / `AudioCueSet` 等数据模型类型定义在 `PixelEngine.Simulation`**(热路径需直接持有 `MaterialDef[]/Reaction[]`,且依赖不得反向)。`PixelEngine.Content` 是**加载器**：依赖 Simulation、把 materials/reactions JSON 反序列化 + `[tag]` 展开 + 注册进 Simulation 的运行时表(故有 `Content → Simulation` 边)。Audio 经 Simulation 的 `MaterialDef.AudioCues` 读取音效线索,不反向依赖。
- **`AudioEvent` / `AudioEventType` 及事件总线契约定义在 `PixelEngine.Core`**(sim/physics 生产、audio/editor/玩法消费,需跨层可见,避免 `Simulation → Audio` 反向)。
- **`PixelEngine.Content` 没有独立 plan 文档**:其数据模型与加载器设计分布在 `plan/04`(schema + 运行时表 + 加载器),持久化 id 重映射在 `plan/07`,编辑器内材质热重载在 `plan/12`。
- **12 相位帧循环的编排者是 `PixelEngine.Hosting`**(见 `plan/18`):各子系统文档只描述「自己在哪个相位、读写什么」,按顺序调用它们的主循环、barrier、dt 一致性、过载降级决策都在 Hosting。

## 6. 项目级 .csproj 公共属性

- [x] `Directory.Build.props`：统一 `TargetFramework=net10.0`、`LangVersion`、`Nullable`、`ImplicitUsings`、分析器、`Deterministic`、符号包。
- [x] `Directory.Packages.props`：中央包版本管理（CPM，`ManagePackageVersionsCentrally=true`），所有版本集中锁定。
- [x] 热路径项目（Core/Simulation/Physics/Interop/Rendering）开 `AllowUnsafeBlocks`，并把零分配/SIMD 相关分析器提升为 error。

## 7. 跨切面约定

- **相位顺序**：所有子系统按架构 §3.3 的 12 相位帧循环协作，靠相位顺序而非锁避免竞争。任何新子系统必须明确自己在哪个相位、读写哪些权威数据。
- **坐标系**：世界以 cell 整数坐标为权威；y 轴向下（屏幕坐标），CA bottom-up 扫描指世界 y 递减方向。物理用「1 物理单位 = 16 px」缩放（架构 §8.1，建库即定，可调常量）。
- **常量集中**：`ChunkSize=64`、`MoveCap=32`、`PhysicsPixelsPerMeter=16`、`TempFieldDownscale=4` 等编译期常量集中在 `PixelEngine.Core` 的 `EngineConstants`，便于 JIT 优化与统一调参。
- **每-cell 字节预算（承重墙，架构 §7.1/§12.2）**：cell SoA 每-cell 字节数与常驻 chunk 内存是架构承重墙；任何新增 per-cell lane 必须经显式预算评审记录，不得静默突破。本轮新增 per-cell **Damage(byte) 累计伤害 lane**（demo-playability 持久破坏模型，见 `plan/03`）使 cell **4B→5B/cell（+25%）、每常驻 chunk 16KB→20KB**，为单缓冲原地写（守 #1，不双缓冲），进 `ChunkSnapshot`/`ChunkCodec` 并 bump `SaveFormatVersion`（存档契约见 `plan/07`，旧档 Damage=0 迁移）。plan/03/07/16 三处预算数字须与此一致。
- **诊断**：所有子系统向 `Core` 的诊断/计时器注册分项耗时，供编辑器性能 HUD 与过载降级（架构 §4.3）使用。
- **文档注释**：公开 API 全部带中文 XML 注释（脚本 IntelliSense 依赖）。

## 8. 本文件验收清单

- [x] 所有其它 plan 文档的「技术栈」段不与本表冲突（已复核 plan/01–18：均继承 .NET 10/C# 14、Silk.NET、Box2D 自建 `[LibraryImport]`、Hexa.NET.ImGui、Roslyn+ALC、System.Text.Json、K4os LZ4、xUnit、BenchmarkDotNet、ComputeSharp/NVorbis 可选门控与无通用 ECS 约束；未发现另立选型）。
- [x] 解决方案结构与 §5 一致，依赖方向被 `.csproj` ProjectReference 强制（无反向依赖）。
- [!] 阻塞：复核 `apps/PixelEngine.Editor.Shell` 与 `src/PixelEngine.UI` 对引擎无反向依赖：EditorShell 与 Demo 同层、仅依赖 `{Hosting, Editor, Gui}` 公开 API；`PixelEngine.UI` 仅依赖 `{Gui, Rendering, Core}`，无子系统反向引用。当前 EditorShell 依赖方向已复核，`src/PixelEngine.UI` 尚未落地，待 plan/20 实现后复核。
- [x] 复核 `PixelEngine.Hosting.csproj` **不再 ProjectReference `PixelEngine.Editor`**：Hosting 通过抽象 GUI/相位[10]钩子接口解耦，Editor 实现由 `apps/PixelEngine.Editor.Shell`（开发构建）注入；玩家包（Demo）不含 `PixelEngine.Editor.dll` 与编辑器专属面板闭包，允许玩家 HUD 所需 `Hexa.NET.ImGui`（GUI 宿主中性化重构落地为前置，玩家包审计断言见 plan/15）。
- [x] 复核 `PixelEngine.Gui` 为 Rendering 之上、Editor 之下的**中性玩家 HUD GUI host 层**：玩家 HUD 使用 `PixelEngine.Gui.HexaImGuiBackend`、`IGuiContext` 运行时适配与字体栈（GuiFontManager，含 CJK）；Demo 玩家 HUD 经 `PixelEngine.Gui` 而非经 Editor（`ScriptGuiContext`）取得 host。
- [!] 阻塞：发行与 Box2D dual-build 工具链已在 `plan/15`、`release.yml`、`tools/audit-release-artifacts.*` 与 `tools/release-evidence-preflight.ps1` 落地，且本机 `win-x64` R2R/AOT 发行验证通过；`tools/release-evidence-preflight.ps1` 会把缺 manifest 标为 `blocked_missing_release_manifest`、schema/JSON 错误标为 `blocked_invalid_release_evidence`、缺 RID/channel/signing/hash/upload scope 标为 `blocked_missing_release_scope_evidence`、非 tag `workflow_dispatch` 上传标为 `blocked_not_tag_release`、证据齐全标为 `release_evidence_attached_pending_review` 且默认非零退出；其中 deterministic hash 报告必须包含 6 RID × 2 channel 全部 `match` 明细行，不能只靠 `conclusion=success` 通过，所有 markdown 报告还必须与 `workflow_run` 的 `run_id` / `sha` 同源，不能拼接不同 GitHub Actions run 或不同 commit 的证据。完整 6 RID 发行管线仍需对应 runner、目标硬件、macOS 签名凭据与 GitHub Release 产物证据经人工复核闭合。

## 9. 提交节点

- 提交：`docs(plan): 建立 plan 锚文档(技术栈定稿与全局约定)`（随 plan 骨架一起首提）。
