# Plan 01 — 项目骨架与构建管线

> 本文档记录 PixelEngine 工程骨架、构建管线、native dual-build 入口与 CI / publish 预检底座；早期“空项目 / bootstrap”内容仅作为历史 M0 工程地基证据，不再代表当前产品目标。权威依据：`AGENTS.md`（开发宪法）、`plan/00-conventions-and-techstack.md`（技术栈定稿，下称「plan/00」）、`docs/PixelEngine-架构与需求设计.md`（架构文档，下称「架构 §x.y」）。
> 锁定的当前全局决策（不可改）：脚本系统 = 项目引用模型 + Roslyn + 可回收 ALC 热重载；Unity-like Editor = 独立 Editor Shell + Editor ImGui 面板层；Web-first UI Runtime = `PixelEngine.UI` 透明 HTML UI（ManagedFallback 恒在、RmlUi 默认、Ultralight 可选）；Showcase Demo Game = 功能完整但聚焦的 showcase Demo，仅依赖引擎公开 API；一步到位、无 MVP、无临时实现；能多线程 / 省内存 / 上 GPU 就全上。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成并有可追溯证据 / `- [!]` 外部证据、人工验收、硬件、发行或 native 阻塞；进行中事项必须拆成已完成子项与未完成/阻塞子项。

---

## 0. 状态账本（2026-07-06）

### 0.1 当前产品职责

- [x] 本文件承载工程骨架底座：解决方案结构、MSBuild / CPM、native Box2D dual-build 脚手架、CI 矩阵、publish 冒烟与本地验证入口。
- [x] 本文件支撑当前产品四面：Engine Core 项目与热路径属性、Unity-like Editor 的 `apps/PixelEngine.Editor.Shell` 注入边界、Web-first UI Runtime 的 `src/PixelEngine.UI` / `src/PixelEngine.Gui` 工程位置、Showcase Demo Game 的 player-only 依赖约束。
- [x] 早期“空项目 / 最小可运行壳 / bootstrap”只作为历史 M0 工程地基证据保留；当前产品完成度以 leaf plan、plan/17 DAG 与真实源码 / 测试 / 证据为准。

### 0.2 状态总览 checklist

- [x] 全局工程配置、CPM、`.editorconfig`、`.gitignore`、`.gitattributes`、`.vscode` 与 `global.json` 已完成。
- [x] 初始解决方案、Core / Interop / Simulation / Content / Serialization / World / Physics / Rendering / Audio / Scripting / Editor / Hosting / Demo / tests / bench 骨架已完成，并作为后续演进的历史底座。
- [x] 当前结构已由 plan/00 对齐为新增 `PixelEngine.Gui`、`PixelEngine.UI` 与 `apps/PixelEngine.Editor.Shell` 的产品结构；Hosting 不再引用 Editor，Demo 不含 Editor。
- [x] Box2D v3.1.1 vendoring、CMake dual-build 脚手架、MSBuild native targets 与当前 RID 构建入口已完成。
- [!] 完整 6-RID CI 运行证据、R2R/NativeAOT release artifact、目标硬件 runner 与签名公证仍归 plan/15 / M15 证据债。

### 0.3 已实现证据 checklist

- [x] `dotnet build PixelEngine.sln -c Release`、`dotnet test`、Demo banner / run 冒烟、BenchmarkSwitcher 启动与当前 RID native build 曾作为 M0 骨架证据登记。
- [x] `dotnet list <proj> reference` 依赖方向核验曾作为工程骨架证据登记。
- [x] `.github/workflows/ci.yml` 定义 6-RID build+test 与 R2R/AOT publish 冒烟入口，覆盖早期 R5 风险的工程预防。
- [x] 后续新增的 player-only 审计、Hosting 解耦与 UI / Gui 依赖纪律证据已转由 plan/00、plan/15、plan/19、plan/20 承载。

### 0.4 未完成目标 checklist

- [x] 本文 §3.2 已改写为当前 `Gui` / `UI` / `Editor.Shell` 后的完整工程清单：`PixelEngine.sln` 当前登记 31 个项目，仓库另有 `tools/PixelEngine.Tools.DeterministicPackage` 作为 package 脚本直调工具项目，不再把早期 `18` 项目表误读为当前结构。
- [x] 新增测试项目、UI / Editor / Demo 专项测试项目与工具项目已在 plan/00、plan/15、plan/19、plan/20 或对应 leaf plan 登记；本文只保留当前清单与 M0 历史口径，不另立新架构。

### 0.5 证据债 / 阻塞 checklist

- [!] 6-RID CI 矩阵存在不等于 6-RID 发行验收完成；交叉 build-only、workflow_dispatch、短跑或本地 probe 不得写成最终验收。
- [!] Box2D dual-build 脚手架存在不等于完整 native 发行证据；codesign、notarization、artifact hash 与 release upload 仍归 plan/15 / M15。
- [!] Editor Shell、Web-first UI Runtime 与 Showcase Demo Game 的真实窗口 / 人工体验验收不归本文完成，仍由 plan/13、plan/19、plan/20、plan/17 闭合。

### 0.6 验证命令与证据路径 checklist

- [x] 基础构建：`dotnet build PixelEngine.sln -c Release`。
- [x] 基础测试：`dotnet test PixelEngine.sln -c Release` 或按项目定向测试。
- [x] native 当前 RID：`tools/build-native.ps1` / `tools/build-native.sh`。
- [x] publish 冒烟入口：`tools/verify-publish.ps1`。
- [!] 外部发行证据：GitHub Actions workflow run、release manifest、artifact hash、签名公证、目标硬件报告。

### 0.7 依赖与下一闭合节点 checklist

- [x] 上游只依赖 plan/00 技术栈锚文档与架构文档。
- [x] 下游由 plan/02–20 消费工程骨架；当前产品结构新增项由 plan/00、plan/19、plan/20 保持权威。
- [!] 下一闭合节点不是继续扩展历史 bootstrap，而是 plan/13 / plan/20 / plan/15 / plan/16 / plan/17 的产品与证据债收口。

## 1. 目标与范围

本文档对应路线图里程碑 M0 的「骨架」半边（架构 §18）。历史目标是产出一个能 `dotnet build` / `dotnet test` / `dotnet run` 通过的工程底座与完整的本地 + CI 构建管线；该 bootstrap 口径只保留为已完成工程地基证据，不再代表当前产品完成目标。当前职责是确保后续每个子系统计划都建立在既定项目、依赖方向、编译属性、包版本、native 构建入口与 CI 矩阵之上，并与 plan/00 当前产品结构保持一致。

范围之内（历史 M0 bootstrap 证据）：创建 `PixelEngine.sln` 与当时 plan/00 §5 列出的 18 个项目（`src/` 下 12 个、`demo/` 1 个、`tests/` 4 个、`bench/` 1 个）的 `.csproj` 骨架，并用 `ProjectReference` 强制当时 plan/00 §5 的依赖方向（无任何反向依赖）；建立 `Directory.Build.props`（统一目标框架、语言、Nullable、ImplicitUsings、Deterministic、分析器、热路径 `AllowUnsafeBlocks`）与 `Directory.Packages.props`（中央包版本管理 CPM，锁定 plan/00 §4 全部 NuGet 包版本）；在 `native/box2d/` 下 vendoring Box2D v3.1.1 C 源并搭起 CMake **dual-build（静态 + 动态）× 6 RID** 的脚本骨架与 MSBuild 集成入口（详细打包在 plan/15 落实，本文档只建脚手架）；补齐 `.vscode/`、`tools/` 与 GitHub Actions CI 工作流（6-RID 矩阵 build + test，同时验证 CoreCLR/R2R 动态路径与 NativeAOT 静态路径，针对架构 R5「debug 正常 publish 崩」）；最后做历史 bootstrap 冒烟验证：空骨架项目能 build、空测试能 test、Demo 能 run 起一个空窗口或打印 banner。当前 `PixelEngine.Gui`、`PixelEngine.UI`、`apps/PixelEngine.Editor.Shell`、Hosting 解耦与 Demo player-only 结构以 plan/00 / plan/19 / plan/20 为准。

范围之外（交由邻居文档）：任何 cell / chunk / 渲染 / 物理 / 脚本 / 编辑器 / 序列化 / 音频的**业务逻辑**与公开 API 设计，均在各自子系统计划落地；`EngineConstants` 的**具体常量值**在 plan/02 定义（本文档仅在 `PixelEngine.Core` 建一个无值的 `EngineConstants` 占位壳，见 §3）；Box2D 绑定的实际 `[LibraryImport]` 签名与 task 桥在 plan/06；native 的 codesign / notarization / 完整发行打包在 plan/15；GC 模式实测定档（架构 §12.4）、基准曲线在对应子系统计划。

不变式守护：本文档建立的依赖方向（plan/00 §5、§8）与编译属性（plan/00 §1、§6）是 AGENTS.md §1 全部不变式得以成立的工程前提，尤其是「native 面收敛到 Box2D 一个依赖」（不变式 10、架构 §14.4）。本文档不引入任何会与不变式冲突的设计。

## 2. 技术栈与依赖

全部选型继承 plan/00，不另立。运行时与语言：.NET 10 LTS、C# 14（`LangVersion=14`）、`Nullable=enable`、`ImplicitUsings=enable`、file-scoped namespace、`Deterministic=true`；CI 开 `TreatWarningsAsErrors`（plan/00 §1）。本机已验证 SDK 为 10.0.108。

本文档需在 `Directory.Packages.props` 中以 CPM 锁定 plan/00 §4 的全部包。下表给出建议的 2026 年可用版本号，**所有版本号均标注「需核对」**——落地时以 NuGet 上 .NET 10 兼容的实际最新稳定版为准，核对后回填并提交。

| 领域 | 包 ID | 建议版本（需核对） | 备注 |
|---|---|---|---|
| 窗口 / 输入 / GL / AL | `Silk.NET.Windowing` / `Silk.NET.Input` / `Silk.NET.OpenGL` / `Silk.NET.OpenAL` | `2.22.0`（架构 §9.1 提及 2.23.x，取最新 2.x） | MIT、.NET Foundation；GL 3.3 Core 基线 |
| 数学（可选） | `Silk.NET.Maths` | `2.22.0` | 仅与 GL 交互便利处可选；主数学走 BCL |
| Editor ImGui 面板层 | `Hexa.NET.ImGui` / `Hexa.NET.ImGui.Backends` | `2.2.7` | Unity-like Editor 的 ImGui 面板层技术，非玩家侧 Web-first UI Runtime |
| 编辑器 UI 扩展 | `Hexa.NET.ImGuizmo` / `Hexa.NET.ImPlot` / `Hexa.NET.ImNodes` | `2.2.7` | gizmo / 曲线 / 节点（plan/00 §4） |
| 脚本编译 | `Microsoft.CodeAnalysis.CSharp` | `4.14.0` | Roslyn；与 .NET 10 SDK 内置 Roslyn 对齐 |
| 脚本隔离 | （BCL `System.Runtime.Loader`） | 随框架 | 可回收 ALC，无需包 |
| 内容序列化 | （BCL `System.Text.Json` + 源生成器） | 随框架（10.0.x） | net10 内置，无需显式 PackageReference |
| 存档压缩 | `K4os.Compression.LZ4` | `1.3.8` | chunk RLE + LZ4（架构 §11.3） |
| 测试框架 | `xunit` | `2.9.3` | 性质 / 边界 / oracle 测试 |
| 测试运行 | `xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk` | `3.1.0` / `17.13.0` | VS / `dotnet test` |
| 覆盖率（可选） | `coverlet.collector` | `6.0.4` | CI 覆盖率收集 |
| 基准 | `BenchmarkDotNet` | `0.14.0` | 含 `[DisassemblyDiagnoser]`（架构 §17.3） |

数学 / SIMD 走 `System.Numerics` + `System.Runtime.Intrinsics`（BCL，无包）。互操作走 `[LibraryImport]` source-gen（BCL，无包），禁新 `DllImport`（plan/00 §4、架构 §14.3）。物理为 vendored Box2D v3.1.1 C 源 + 自建 `[LibraryImport]` 薄绑定，**不**引第三方托管绑定包（避开架构 R10 的双 "Box2D.NET" 陷阱）。分析器以 .NET 内置 `Microsoft.CodeAnalysis.NetAnalyzers`（`EnableNETAnalyzers`）为基线；零分配 / SIMD 相关规则的「提升为 error」通过 `.editorconfig` 的 severity 配置实现（plan/00 §6），如需第三方堆分配分析器（如 `Meziantou.Analyzer`）由后续计划评审引入，本文档不强加。

唯一 native 依赖：Box2D（不变式 10）。OpenAL（OpenAL Soft）、可选 ANGLE 等走 Silk.NET 自带 runtime 包 / 系统分发，不进入本文档的 dual-build fan-out。

## 3. 详细设计

### 3.1 目录与解决方案布局

最终仓库顶层结构如下（在已存在文件基础上补齐）。已存在并纳入版本控制的文件：`.git/`（已 init，分支 `main`，尚无 commit）、`.gitignore`、`.editorconfig`、`LICENSE`（MIT）、`AGENTS.md`、`docs/`、`plan/`。本文档新增标 `(新增)`。

```
PixelEngine/
├─ .git/                         已存在（main，无 commit）
├─ .gitignore                    已存在（需核对覆盖 bin/ obj/ native 产物 等，见 3.7）
├─ .editorconfig                 已存在（需补 §3.3 的分析器 severity，见 3.7）
├─ .gitmodules                   (新增) Box2D v3.1.1 子模块声明
├─ .gitattributes               (新增) 行尾与 native 二进制属性
├─ LICENSE                       已存在（MIT）
├─ AGENTS.md                     已存在
├─ Directory.Build.props         (新增) 全局 MSBuild 属性
├─ Directory.Packages.props      (新增) CPM 中央包版本
├─ Directory.Build.targets       (新增) 全局 targets（导入 native targets 等）
├─ PixelEngine.sln               (新增)
├─ global.json                   (新增) 锁 SDK 频段（10.0.x，rollForward=latestFeature）
├─ nuget.config                  (新增) 锁定 nuget.org 源
├─ .vscode/
│  ├─ extensions.json            (新增) 推荐 C# Dev Kit、CMake Tools
│  ├─ settings.json              (新增) 格式化 / 分析器开关
│  └─ launch.json                (新增) 调试 Demo / Benchmarks
├─ .github/workflows/
│  └─ ci.yml                     (新增) 6-RID build+test + R2R/AOT 双路径验证
├─ tools/                        (新增) 构建脚本目录
│  ├─ build-native.ps1           (新增) Windows 侧 CMake dual-build 驱动
│  ├─ build-native.sh            (新增) Linux/macOS 侧 CMake dual-build 驱动
│  └─ verify-publish.ps1         (新增) 本地复现 CI 的 R2R/AOT publish 冒烟
├─ native/
│  ├─ box2d/                     (新增) Box2D v3.1.1 C 源（git submodule）
│  ├─ CMakeLists.txt             (新增) 顶层：dual-build（STATIC+SHARED）
│  ├─ CMakePresets.json          (新增) 6 RID 预设 + 交叉编译 toolchain
│  ├─ PixelEngine.Native.targets (新增) MSBuild 集成入口（动态拷贝 + AOT 静态链）
│  └─ out/                       构建产物（git 忽略）
├─ content/                      (新增，空占位) materials.json/reactions.json/纹理/音效（plan/02+ 填充）
├─ src/   （14 个当前项目，见 3.2）
├─ apps/  （1 个当前项目，见 3.2）
├─ demo/  （1 个项目）
├─ tests/ （13 个当前项目，见 3.2）
├─ bench/ （1 个项目）
└─ tools/ （脚本 + 2 个工具项目，见 3.2）
```

`PixelEngine.sln` 用 solution folder 归类 `src` / `apps` / `demo` / `tests` / `bench` / `tools` 与 `Build`（放 `Directory.*.props`、`global.json` 等），并把 `native/`、`.github/` 以 solution items 形式挂入 `Build` 便于 IDE 浏览。当前 `tools/PixelEngine.Tools.DeterministicPackage` 由 `tools/package.ps1` / `tools/package.sh` 直调，尚未登记进 solution；这是当前事实记录，不视为本文 M0 阻塞。

### 3.2 当前工程清单与历史 M0 依赖方向

> 早期 M0 表格的「18 项目（12 src + 1 demo + 4 tests + 1 bench）」只保留为 bootstrap 历史证据；当前权威结构已由 plan/00、plan/15、plan/19、plan/20 与实际源码扩展。2026-07-09 本地 `dotnet sln PixelEngine.sln list` 显示 solution 登记 31 个项目；`rg --files -g "*.csproj"` 显示仓库共有 32 个 `.csproj`，其中 `tools/PixelEngine.Tools.DeterministicPackage` 由打包脚本直调，不在 solution 中。

当前 `src/` 引擎与运行时项目（14 个）：`PixelEngine.Core`、`PixelEngine.Interop`、`PixelEngine.Simulation`、`PixelEngine.Content`、`PixelEngine.Serialization`、`PixelEngine.World`、`PixelEngine.Physics`、`PixelEngine.Rendering`、`PixelEngine.Audio`、`PixelEngine.Scripting`、`PixelEngine.Gui`、`PixelEngine.UI`、`PixelEngine.Editor`、`PixelEngine.Hosting`。其中 `PixelEngine.Gui` 是中性 ImGui / GUI bridge 层，`PixelEngine.UI` 是 Web-first UI Runtime，`PixelEngine.Editor` 只承载编辑器 ImGui 面板层；Hosting 解耦后不再硬引用 Editor，Demo 仍只消费 Hosting 公开 API。

当前产品入口项目（2 个）：`apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj` 是 Unity-like Editor 独立应用；`demo/PixelEngine.Demo/PixelEngine.Demo.csproj` 是 player/demo 入口，保持 player-only 依赖纪律。

当前测试项目（13 个）：`PixelEngine.Core.Tests`、`PixelEngine.Content.Tests`、`PixelEngine.Simulation.Tests`、`PixelEngine.Serialization.Tests`、`PixelEngine.World.Tests`、`PixelEngine.Physics.Tests`、`PixelEngine.Rendering.Tests`、`PixelEngine.Audio.Tests`、`PixelEngine.Scripting.Tests`、`PixelEngine.UI.Tests`、`PixelEngine.Editor.Tests`、`PixelEngine.Hosting.Tests`、`PixelEngine.Demo.Tests`。这些测试项目的具体覆盖面归对应 leaf plan 登记，本文只记录结构事实。

当前工具 / 基准项目（2 个 solution 内 + 1 个 solution 外工具）：`bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj`、`tools/PixelEngine.Tools.ManagedNativeLeakDetector/PixelEngine.Tools.ManagedNativeLeakDetector.csproj` 已登记进 solution；`tools/PixelEngine.Tools.DeterministicPackage/PixelEngine.Tools.DeterministicPackage.csproj` 已被 `tools/package.ps1` / `tools/package.sh` 使用但未登记进 solution。若后续决定把 deterministic package tool 纳入 solution，应在 plan/15 与本文同步更新。

依赖方向仍遵守 plan/00 §5、§8 与 AGENTS.md 不变式：Demo 不直接引用 Editor 或内部实现，Simulation 不引用 Rendering / Physics，Interop 隔离 unsafe / native surface，权威 sim 热路径 native 依赖仍收敛到 Box2D。当前完整 ProjectReference 事实以各 `.csproj`、`dotnet list <proj> reference`、plan/00 与 leaf plan 的依赖纪律测试为准；本文不再维护一张会随产品演进漂移的逐项 reference 表。

### 3.3 Directory.Build.props / Directory.Build.targets（全局 MSBuild）

`Directory.Build.props` 被所有项目自动继承（plan/00 §6、§8），统一设置：`TargetFramework=net10.0`、`LangVersion=14`、`Nullable=enable`、`ImplicitUsings=enable`、`Deterministic=true`、`ContinuousIntegrationBuild=$(CI)`（CI 上为 true，确定性构建路径映射）、`InvariantGlobalization=true`、`EnableNETAnalyzers=true`、`AnalysisLevel=latest`、`EnforceCodeStyleInBuild=true`、`GenerateDocumentationFile=true`（脚本 IntelliSense 依赖公开 API 的 XML 注释，AGENTS.md §2/§4）、`DebugType=portable`、`Features=strict`。`TreatWarningsAsErrors` 仅在 CI 开启：`<TreatWarningsAsErrors Condition="'$(CI)'=='true'">true</TreatWarningsAsErrors>`（plan/00 §1）。

热路径开关用项目自带的 `<IsHotPath>true</IsHotPath>` 属性驱动，`Directory.Build.props` 据此条件设置 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` 与 `<Optimize Condition="'$(Configuration)'=='Release'">true</Optimize>`，并对热路径项目把指定的零分配 / SIMD 分析器规则提升为 error（具体规则 ID 在 `.editorconfig` 配 severity，见 §3.7）。`ServerGarbageCollection`/`ConcurrentGarbageCollection` **不在此预设**——按 plan/00 §1 与架构 §12.4 默认 Workstation+Concurrent，基准实测后定档，留待对应计划。

`Directory.Build.targets` 用于导入 `native/PixelEngine.Native.targets`（仅 `Interop` 通过条件导入，见 §3.6），以及统一的符号 / 包属性收尾，避免在每个 `.csproj` 重复。

### 3.4 Directory.Packages.props（CPM）

启用 `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`（plan/00 §6），把 §2 表中全部包以 `<PackageVersion Include="..." Version="..." />` 集中锁定；各 `.csproj` 只写 `<PackageReference Include="..." />`（无版本）。包按项目分配：`Rendering` 引 Silk.NET.{Windowing,Input,OpenGL}；`Audio` 引 Silk.NET.OpenAL；`Editor` 引 Hexa.NET.ImGui(.Backends/.ImGuizmo/.ImPlot/.ImNodes)；`Scripting` 引 Microsoft.CodeAnalysis.CSharp；`Serialization` 引 K4os.Compression.LZ4；四个测试项目引 xunit + xunit.runner.visualstudio + Microsoft.NET.Test.Sdk(+coverlet.collector)；`Benchmarks` 引 BenchmarkDotNet。`System.Text.Json` 与 `System.Runtime.Loader` 随 net10 框架，不写 PackageReference。所有版本号注明「需核对」，落地时核对 NuGet 实际可用稳定版后回填。

### 3.5 native/box2d — Box2D v3.1.1 vendoring 与 CMake dual-build 脚手架

vendoring 方式：以 git submodule 引入 `erincatto/box2d`，固定到 v3.1.1 对应的 tag/commit `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`（写入 `.gitmodules` 与 `native/box2d`）。这是唯一 native 依赖（不变式 10、架构 §14.4）。

`native/CMakeLists.txt`（顶层）用 `ExternalProject_Add` 启动两套隔离的 Box2D 子构建，分别产出 `STATIC`（`.lib`/`.a`，供 NativeAOT 静态链）与 `SHARED`（`.dll`/`.so`/`.dylib`，供 CoreCLR 开发与 R2R 发行），即架构 §14.4 的「dual-build」。统一关闭 Box2D 的 sample/test 子目标，保留默认 SIMD（不为泛用 RID 固定 AVX2）。Linux 侧动态链 glibc、**不**静态链 libc（架构 §14.4、R5）。

`native/CMakePresets.json` 提供 6 个 RID 预设：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`（plan/00 §3、架构 §15）。每个预设指定 generator、架构 / toolchain（arm64 交叉编译 toolchain 文件，macOS 用 `CMAKE_OSX_ARCHITECTURES`），并把产物输出到 `native/out/<rid>/{shared,static}/`。

`tools/build-native.ps1` 与 `tools/build-native.sh` 是跨平台驱动脚本：参数化 RID 与 build 类型，依次 `cmake --preset <rid>` + `cmake --build`，把动态库收集到约定的 `runtimes/<rid>/native/` 暂存、静态库收集到 `native/out/<rid>/static/`，供 MSBuild 拾取。本文档只建脚手架与产物约定；**完整打包 / codesign / notarization 在 plan/15**，逐 RID 的绑定与 task 桥在 plan/06。

### 3.6 native 的 MSBuild 集成入口

`native/PixelEngine.Native.targets` 由 `PixelEngine.Interop` 通过 `Directory.Build.targets` 条件导入（仅 Interop 需要 native），按发行路径分流：CoreCLR 开发与 R2R 发行（动态）下，把 `runtimes/<rid>/native/` 的 `.dll`/`.so`/`.dylib` 作为 `<Content>` / `<None CopyToOutputDirectory>` 随 RID 拷贝（host 自动从 `runtimes/<rid>/native/` 解析，架构 §15）；NativeAOT 发行（静态）下，用 `<NativeLibrary Include="native/out/$(RuntimeIdentifier)/static/...">`（RID-gated）静态链入（架构 §14.4）。本文档只搭入口与条件分流骨架，真实库名 / 入口点在 plan/06 接入；CI 的 R2R/AOT 双路径验证（§3.8）即为防 R5「debug 正常 publish 崩」而设。

### 3.7 已存在文件的纳入与补充

已存在文件（`.gitignore`、`.editorconfig`、`LICENSE`、`AGENTS.md`、`docs/`、`plan/`）保留，按需补充：核对并补全 `.gitignore` 覆盖 `bin/`、`obj/`、`native/out/`、`runtimes/`、`*.user`、`BenchmarkDotNet.Artifacts/`、存档 / 构建产物（AGENTS.md §6）；在 `.editorconfig` 追加分析器 severity 配置——全局把 code-style 规则设为 warning，热路径项目（用 `[*.cs]` 配合路径 glob，如 `[{src/PixelEngine.Core,src/PixelEngine.Simulation,...}/**.cs]`）把零分配 / SIMD 相关诊断（例如 `CA1849`、`CA2014`、堆分配类规则）提升为 `error`（plan/00 §6）。新增 `.gitattributes`（规范行尾、把 native 二进制标 binary）。`.vscode/extensions.json` 推荐 `ms-dotnettools.csdevkit`（C# Dev Kit）、`ms-dotnettools.csharp`、`ms-vscode.cmake-tools`；`settings.json` 统一格式化与 `dotnet.server.useOmnisharp=false`；`launch.json` 配置启动 Demo 与 Benchmarks。`global.json` 锁 SDK 至 `10.0.x`（`rollForward: latestFeature`）。`nuget.config` 固定 `nuget.org` 单一源（配合 R10 锁源）。

### 3.8 CI（GitHub Actions）

`.github/workflows/ci.yml` 两段式。第一段 `build-test`：矩阵跨 6 RID 与对应 runner——`win-x64`(windows-latest)、`win-arm64`(windows-11-arm 或交叉 build-only)、`linux-x64`(ubuntu-latest)、`linux-arm64`(ubuntu-24.04-arm)、`osx-x64`(macos-13)、`osx-arm64`(macos-14)；每格先 `tools/build-native` 出 Box2D dual-build，再 `dotnet build PixelEngine.sln -c Release`，在原生架构可执行的 runner 上跑 `dotnet test`（交叉 build-only 的 arm 格仅编译不测，注明）。第二段 `verify-publish`（针对架构 R5）：对 Demo 同时执行 **CoreCLR 自包含 + R2R（动态 native）** `dotnet publish` 与 **NativeAOT（静态 native）** publish，各自 headless 冒烟运行一次确认不崩——覆盖「CoreCLR/R2R 动态路径」与「NativeAOT 静态路径」两条，防止「debug 正常 publish 崩」。CI 设 `CI=true` 触发 `TreatWarningsAsErrors` 与确定性构建。

### 3.9 EngineConstants 占位

按 plan/00 §7，编译期常量（`ChunkSize=64`、`MoveCap=32`、`PhysicsPixelsPerMeter=16`、`TempFieldDownscale=4` 等）集中在 `PixelEngine.Core` 的 `EngineConstants`。本文档**只**在 `src/PixelEngine.Core/EngineConstants.cs` 建一个带 XML 文档头、`public static class EngineConstants`（partial）的**空壳**，不写任何常量值；**具体常量在 plan/02 定义**。此举仅固定类型归属与命名空间，不构成业务逻辑或占位实现。

### 3.10 历史 bootstrap 冒烟内容（最小可运行壳）

为满足冒烟验证而创建的最小内容，均为真实可运行壳而非假实现：`src/*` 全部 12 个库项目**不含任何 `.cs` 业务文件**（除 Core 的 `EngineConstants` 空壳），空 assembly 正常编译；`demo/PixelEngine.Demo/Program.cs` 用 top-level statements 打印引擎 banner（名称 + 版本 + 目标 RID）并退出 0（真实窗口循环依赖 Rendering，留待渲染计划；本文档满足「打印」一档，预留切换到 Silk.NET 空窗口的入口注释）；四个测试项目各含一个 `SmokeTests.cs`，写一个验证「被测 assembly 可加载」的 `[Fact]`（断言其程序集类型可枚举 / `Assembly.Load` 成功），作为装配冒烟，后续被各子系统真实测试替换 / 扩充；`bench/PixelEngine.Benchmarks/Program.cs` 用 `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args)`，空基准集合可正常启动（真实基准随各子系统加入）。

## 4. 实现清单

仓库与全局配置：

- [x] 新增 `global.json`，锁 SDK 至 `10.0.x`（`rollForward: latestFeature`），与本机 10.0.108 一致
- [x] 新增 `nuget.config`，固定单一源 `nuget.org`（配合架构 R10 锁源）
- [x] 新增 `Directory.Build.props`：`TargetFramework=net10.0`、`LangVersion=14`、`Nullable=enable`、`ImplicitUsings=enable`、`Deterministic=true`、`ContinuousIntegrationBuild=$(CI)`、`InvariantGlobalization=true`、`GenerateDocumentationFile=true`、`DebugType=portable`、`Features=strict`、`EnableNETAnalyzers=true`、`AnalysisLevel=latest`、`EnforceCodeStyleInBuild=true`
- [x] 在 `Directory.Build.props` 加 `TreatWarningsAsErrors` 仅 `'$(CI)'=='true'` 时为 true（plan/00 §1）
- [x] 在 `Directory.Build.props` 用 `<IsHotPath>` 条件设 `AllowUnsafeBlocks=true` 与 Release `Optimize=true`（plan/00 §6）
- [x] 新增 `Directory.Build.targets`：条件导入 `native/PixelEngine.Native.targets`（仅 Interop）与统一收尾属性
- [x] 新增 `Directory.Packages.props`：`ManagePackageVersionsCentrally=true`，按 §2 表锁定全部 `PackageVersion`（每条注「需核对」）
- [x] 补全 `.gitignore`：`bin/`、`obj/`、`native/out/`、`runtimes/`、`BenchmarkDotNet.Artifacts/`、`*.user`、存档 / 产物（AGENTS.md §6）
- [x] 在 `.editorconfig` 追加分析器 severity：全局 code-style=warning；热路径项目把零分配 / SIMD 诊断提升为 `error`（plan/00 §6）
- [x] 新增 `.gitattributes`：规范行尾、native 二进制标 binary
- [x] 新增 `.vscode/extensions.json`（推荐 `ms-dotnettools.csdevkit`、`ms-dotnettools.csharp`、`ms-vscode.cmake-tools`）、`settings.json`、`launch.json`

解决方案与 src 项目（12 个）：

- [x] 新增 `PixelEngine.sln`，建 `src`/`demo`/`tests`/`bench`/`Build` solution folders，并把 `native/`、`tools/`、`.github/`、`Directory.*.props`、`global.json` 挂为 solution items
- [x] 创建 `src/PixelEngine.Core/PixelEngine.Core.csproj`（Library，`IsHotPath=true`），无 ProjectReference
- [x] 在 `src/PixelEngine.Core/EngineConstants.cs` 建 `public static partial class EngineConstants` **空壳**（带 XML 文档头，无常量值；常量在 plan/02）
- [x] 创建 `src/PixelEngine.Interop/PixelEngine.Interop.csproj`（Library，`IsHotPath=true`），引用 Core；条件导入 native targets
- [x] 创建 `src/PixelEngine.Simulation/PixelEngine.Simulation.csproj`（Library，`IsHotPath=true`），引用 Core
- [x] 创建 `src/PixelEngine.Content/PixelEngine.Content.csproj`（Library），引用 Core、Simulation
- [x] 创建 `src/PixelEngine.Serialization/PixelEngine.Serialization.csproj`（Library），引用 Core、Simulation、Content；引 `K4os.Compression.LZ4`
- [x] 创建 `src/PixelEngine.World/PixelEngine.World.csproj`（Library），引用 Core、Simulation、Serialization
- [x] 创建 `src/PixelEngine.Physics/PixelEngine.Physics.csproj`（Library，`IsHotPath=true`），引用 Core、Interop、Simulation
- [x] 创建 `src/PixelEngine.Rendering/PixelEngine.Rendering.csproj`（Library，`IsHotPath=true`），引用 Core、Simulation、World；引 `Silk.NET.Windowing/Input/OpenGL`
- [x] 创建 `src/PixelEngine.Audio/PixelEngine.Audio.csproj`（Library），引用 Core、Content；引 `Silk.NET.OpenAL`
- [x] 创建 `src/PixelEngine.Scripting/PixelEngine.Scripting.csproj`（Library），引用 Core、Simulation、Physics、World、Content；引 `Microsoft.CodeAnalysis.CSharp`
- [x] 创建 `src/PixelEngine.Editor/PixelEngine.Editor.csproj`（Library），引用全部 9 个子系统（Core/Simulation/Physics/World/Serialization/Content/Rendering/Audio/Scripting）；引 `Hexa.NET.ImGui(.Backends/.ImGuizmo/.ImPlot/.ImNodes)`
- [x] 创建 `src/PixelEngine.Hosting/PixelEngine.Hosting.csproj`（Library），引用全部 11 个其它引擎项目
- [x] `dotnet build` 后用 `dotnet list <proj> reference` 逐项核对依赖方向与 §3.2 表完全一致、无反向、无环

demo / tests / bench：

- [x] 创建 `demo/PixelEngine.Demo/PixelEngine.Demo.csproj`（Exe），**仅**引用 Hosting
- [x] 写 `demo/PixelEngine.Demo/Program.cs`：打印 banner（名称 + 版本 + 目标 RID），退出 0；注释预留 Silk.NET 空窗口切换入口
- [x] 创建 `tests/PixelEngine.Simulation.Tests/`（Test 项目 + `SmokeTests.cs` 装配冒烟 `[Fact]`），引用 Simulation
- [x] 创建 `tests/PixelEngine.Physics.Tests/`（同上），引用 Physics
- [x] 创建 `tests/PixelEngine.Serialization.Tests/`（同上），引用 Serialization
- [x] 创建 `tests/PixelEngine.Scripting.Tests/`（同上），引用 Scripting
- [x] 创建 `bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj`（Exe，`IsHotPath=true`），引用 Core、Simulation、Physics、Serialization；引 `BenchmarkDotNet`
- [x] 写 `bench/PixelEngine.Benchmarks/Program.cs`：`BenchmarkSwitcher.FromAssembly(...).Run(args)`（空基准集合）

native（Box2D dual-build 脚手架）：

- [x] 以 git submodule 引入 `erincatto/box2d` 到 `native/box2d`，固定 v3.1.1 tag/commit，写 `.gitmodules`
- [x] 新增 `native/CMakeLists.txt`：以隔离 ExternalProject 子构建定义 STATIC + SHARED 双目标，关闭 sample/test，保留默认 SIMD，Linux 动态链 glibc
- [x] 新增 `native/CMakePresets.json`：6 RID 预设（win/linux/osx × x64/arm64）+ arm64 交叉 toolchain + `osx` 多架构，产物输出 `native/out/<rid>/{shared,static}/`
- [x] 新增 `tools/build-native.ps1` 与 `tools/build-native.sh`：参数化 RID 驱动 `cmake --preset` + `--build`，收集动态库到 `runtimes/<rid>/native/`、静态库到 `native/out/<rid>/static/`
- [x] 新增 `native/PixelEngine.Native.targets`：R2R/CoreCLR 动态路径拷 `.dll/.so/.dylib`；NativeAOT 静态路径 `<NativeLibrary Include>`（RID-gated）——仅入口骨架，真实库名 plan/06 接
- [x] 新增 `tools/verify-publish.ps1`：本地复现 R2R 与 NativeAOT publish 冒烟，便于 CI 前自检

CI：

- [x] 新增 `.github/workflows/ci.yml` `build-test` job：6-RID 矩阵 + 对应 runner，先 `tools/build-native` 再 `dotnet build -c Release`，原生架构 runner 跑 `dotnet test`，交叉 arm 格 build-only（注明）
- [x] 在 `ci.yml` 加 `verify-publish` job：Demo 同时做 CoreCLR+R2R（动态）与 NativeAOT（静态）publish + headless 冒烟运行，覆盖架构 R5 两条路径
- [x] `ci.yml` 设 `env: CI=true`，使 `TreatWarningsAsErrors` 与确定性构建在 CI 生效

冒烟验证（本机）：

- [x] `dotnet build PixelEngine.sln -c Release` 全 18 项目通过、零警告
- [x] `dotnet test` 四个测试项目全绿（装配冒烟 `[Fact]` 通过）
- [x] `dotnet run --project demo/PixelEngine.Demo -c Release` 打印 banner 并退出 0
- [x] `dotnet run --project bench/PixelEngine.Benchmarks -c Release` 正常启动 BenchmarkSwitcher（空集合）
- [x] 本机执行 `tools/build-native`（当前 RID），产出 Box2D 静态 + 动态库于约定目录

## 5. 验收标准

- [x] `PixelEngine.sln` 含且仅含 plan/00 §5 的 18 个项目（12 src + 1 demo + 4 tests + 1 bench），路径与命名与 §3.2 表一致
- [x] 每个项目的 `ProjectReference` 与 §3.2 表逐字一致，依赖方向满足 `Demo → Hosting → {子系统} → Interop → Core`，`dotnet list reference` 核验无任何反向依赖、无循环
- [x] `Simulation` 不引用 `Rendering`/`Physics`；`Demo` 仅引用 `Hosting`；`Interop` 为唯一持有 native 集成的项目（不变式 10）
- [x] `Directory.Build.props` 生效且被全部项目继承：`net10.0`、C# 14、Nullable、ImplicitUsings、Deterministic、`GenerateDocumentationFile`、分析器开启
- [x] 热路径项目 {Core, Interop, Simulation, Physics, Rendering, Benchmarks} 启用 `AllowUnsafeBlocks`；非热路径项目未启用
- [x] `TreatWarningsAsErrors` 仅在 CI（`CI=true`）生效，本机开发态不因 warning 阻断
- [x] `Directory.Packages.props` 启用 CPM，§2 表全部包集中锁定版本；各 `.csproj` 的 `PackageReference` 均不带内联版本；每个版本号附「需核对」标注
- [x] `native/box2d` 为固定到 v3.1.1 的 submodule；`native/CMakeLists.txt` 能产出**静态 + 动态**两类目标；`CMakePresets.json` 覆盖全部 6 RID
- [x] `tools/build-native.{ps1,sh}` 在当前平台可跑通，产物落在 `runtimes/<rid>/native/`（动态）与 `native/out/<rid>/static/`（静态）
- [x] `native/PixelEngine.Native.targets` 仅被 `Interop` 导入，R2R 动态拷贝与 AOT 静态链两条分支均有入口（真实库名待 plan/06，骨架完整）
- [x] 已存在文件全部纳入：`.gitignore` 覆盖产物与 `native/out/`、`runtimes/`；`.editorconfig` 含热路径分析器 `error` 升级；`LICENSE`(MIT)/`AGENTS.md` 未被改动
- [x] `.vscode/extensions.json` 推荐 C# Dev Kit 与 CMake Tools；`tools/` 构建脚本目录存在
- [x] `.github/workflows/ci.yml` 存在并定义 6-RID 矩阵 build+test 与 R2R/AOT 双路径 publish 冒烟两段（覆盖架构 R5）
- [x] `dotnet build PixelEngine.sln -c Release` 在本机零警告通过（历史 M0 空骨架项目）
- [x] `dotnet test` 四个测试项目全部通过
- [x] `dotnet run --project demo/PixelEngine.Demo -c Release` 起一个空窗口或打印 banner 并以 0 退出
- [x] `dotnet run --project bench/PixelEngine.Benchmarks -c Release` 正常启动且不崩
- [x] `src/` 下除 `Core/EngineConstants.cs` 空壳外无任何业务 `.cs`；`EngineConstants` 仅声明类型、无常量值（常量归 plan/02）
- [x] 本文档技术栈与依赖与 plan/00 无冲突，未引入 plan/00 §4 之外的选型，未违背 AGENTS.md §1 任一不变式

## 6. 依赖关系

前置：本文档是 `plan/` 的最底层工程地基，仅依赖 plan/00（技术栈定稿）与架构文档（§14.4 dual-build、§15 兼容性、§16 工程结构、§18 M0、§19 R5/R10）已就绪，以及本机 .NET 10 SDK（已确认 10.0.108）与 CMake / C 工具链可用。无其它 plan 前置。

被依赖：所有后续子系统计划（plan/02 起）都在本文档建立的项目、依赖方向、`Directory.*.props`、CPM 版本、native 集成入口与 CI 之上落地。具体衔接——plan/02 在 `PixelEngine.Core/EngineConstants.cs` 空壳中填入 `ChunkSize`/`MoveCap`/`PhysicsPixelsPerMeter`/`TempFieldDownscale` 等常量值；plan/06（物理）接入 `native/PixelEngine.Native.targets` 的真实 Box2D 库名 / 入口点与 task 桥，并把 dual-build 接入 Interop；plan/15（发行）在本文档的 CMake / RID 脚手架上落实完整 6-RID 打包、codesign、notarization 与 R2R/NativeAOT 发行产物。

文档边界：本文档严格只建脚手架，不写任何子系统业务逻辑，不替邻居文档做设计决策；如落地中发现与架构不变式 / plan/00 冲突，按 AGENTS.md §5 先改计划再改代码并上报。

## 7. 提交节点

按 AGENTS.md §6，完成本文档定义的提交节点即用中文提交（在 `main` 上小步提交）。建议拆为四个节点，便于回溯：

- [x] 节点 1：仓库工程化骨架。提交 `global.json`、`nuget.config`、`Directory.Build.props/.targets`、`Directory.Packages.props`、`.gitattributes`、`.vscode/`、补全的 `.gitignore`/`.editorconfig`。提交信息：`build(core): 建立全局 MSBuild 属性、CPM 与工程化配置`
- [x] 历史节点 2：解决方案与 M0 空骨架项目。提交 `PixelEngine.sln` 与 18 个 `.csproj` 骨架、`EngineConstants` 空壳、Demo/tests/bench 历史最小可运行壳；本机 `dotnet build`/`test`/`run` 冒烟通过。提交信息：`build(core): 建立解决方案与全部项目骨架及依赖方向`
- [x] 节点 3：native Box2D dual-build 脚手架。提交 Box2D v3.1.1 submodule、`native/CMakeLists.txt`、`CMakePresets.json`、`tools/build-native.*`、`PixelEngine.Native.targets`。提交信息：`build(physics): 搭建 Box2D v3.1.1 vendoring 与 CMake dual-build 脚手架`
- [x] 节点 4：CI 管线。提交 `.github/workflows/ci.yml`（6-RID 矩阵 + R2R/AOT 双路径验证）与 `tools/verify-publish.ps1`。提交信息：`build(core): 接入 6-RID CI 与 R2R/NativeAOT 双路径发布验证`

每个节点完成并自测后再勾选；遇阻塞标 `- [!] 阻塞：原因` 并上报，不前进（AGENTS.md §2/§5）。
