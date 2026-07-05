# Plan 15 — 构建、打包与分发（Build / Packaging / Distribution）

> 本文件定义 PixelEngine 的发行管线：Windows 优先激活 + 跨平台 6-RID 矩阵保留（非激活）、CoreCLR+R2R 主发行、NativeAOT 次发行、Box2D dual-build × 6 RID、native 资产打包、trim 配置、发布 CI、codesign/notarization、版本与产物命名、内容资产打包、编辑器触发的 `build-player` 一键出包入口、玩家包/编辑器工具包分流。
> 权威依据：架构文档 `../docs/PixelEngine-架构与需求设计.md`（下称「架构 §x.y」）的 §12.3、§13、§14.4、§15、R3/R5/R14；技术栈锚文档 `00-conventions-and-techstack.md`；开发宪法 `../AGENTS.md`。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

本文件交付一条可重复、可审计、**当前激活集收敛到 Windows、但构建/发布/打包/审计代码路径与配置对 6 RID 全量保持可扩展**的发行管线，把 `src/` 引擎与 `demo/PixelEngine.Demo` 打成最终用户可运行的产物，并把 `content/` 内容资产一并打包。管线必须同时调和「极致性能」与「广兼容」两个硬需求（架构 §2 挑战五、§12.3、§15）：主发行走 CoreCLR 自包含 + ReadyToRun（R2R composite），靠运行时 CPU 检测 + Tier-1 重 JIT + Dynamic PGO 让 sim 热方法在用户机上 light-up AVX2/AVX-512/AVX10.2，**不固定 ISA**；次发行走 NativeAOT、每 RID 单独产物、必须显式 `IlcInstructionSet`、仅对已知硬件分发（架构 R3）。

**Windows 优先激活与跨平台矩阵保留（本轮修订，见 §2.1）**：当前激活集为 `win-x64`（主发行、本机可全链路验证）与 `win-arm64`（条件激活，构建必过、smoke 走 load-only 手动硬件门禁）；`linux-x64`/`linux-arm64`/`osx-x64`/`osx-arm64` 作为**保留矩阵位（reserved, dormant）**完整保留其 toolchain、构建/发布/打包脚本、`IlcInstructionSet` 分组、codesign 管线与 CI 矩阵形态，仅从「实际调度构建的激活集」中 gate 掉，翻一个布尔即恢复。跨平台不是被删除，而是被「非激活」；双通道（R2R 主 / AOT 次）、不固定 ISA、Box2D dual-build、trim、codesign 的设计全部不变，仅其执行范围随激活集缩放。发行激活门控与 `ci.yml`/`16-performance-hardening.md` 的 6-RID 构建/测试矩阵是**两回事**：后者刻意保持 6 RID（cross 用 build-only）不随发行门控收敛，作为 dormant RID 的编译保证后盾（§2.1）。

**发行分两类（见 §3.7 分流）**：**玩家包**（`demo/PixelEngine.Demo` 或后续 Player app，以 `-p:PixelEnginePlayerBuild=true` 剥离编辑器，**绝不含 `PixelEngine.Editor.dll` 及编辑器专属 `ImGuizmo/ImPlot` 闭包，允许玩家 HUD 所需 `Hexa.NET.ImGui` 核心**）受本文件玩家矩阵约束（win-first 激活子集）；**编辑器工具包**（`apps/PixelEngine.Editor.Shell`，开发/内测分发）与玩家矩阵解耦、不受 6-RID 约束。除 CI 五阶段外，本文件还提供**编辑器触发的一键出包入口** `tools/build-player.*`（§3.11），把 native→publish→verify→package→audit 串成单进程编排，供 `plan/19` 的 BuildSettings 面板子进程消费。

明确的范围边界（严格只写本范围）：

本文件**拥有**发行/打包/分发这一层——即 `dotnet publish` 的发行配置、产物布局、native 资产落位、trim 配置、版本与命名、发布 CI 工作流（`release.yml`）、codesign/notarization、以及发行级的「Box2D dual-build × 6 RID 构建矩阵」编排与产物消费。本文件**不拥有**：解决方案/项目骨架与持续集成基线（`build`/`test` 工作流）由 `01-project-setup.md` 落实，本文件的 `release.yml` 复用其 `ci.yml` 的 action 片段；Box2D 的 `[LibraryImport]` 绑定、`[UnmanagedCallersOnly]` task 桥、CMake 源码编译脚本本身由 `01`/`06-physics-collision-rigidbody.md` 落实，本文件只定义跨 6 RID 的「构建矩阵编排 + 静/动产物落位 + RID-gated 链接」这一发行契约；smoke/性质/基准测试本体由 `14-testing-benchmarking.md` 落实，本文件只定义把 smoke 接入 publish 双路径验证（架构 R5）。

不变式遵守（`AGENTS.md §1`）：本管线把**权威 sim 热路径的静态 vendored native / dual-build 静态链**收敛到 Box2D 一项（不变式 #10 修订口径），仅 Box2D 需要 dual-build（静/动）。OpenAL/ANGLE 与 `plan/20` 的 RmlUi/Ultralight UI native 均归门控类可选 native：dynamic-only 或系统分发、可禁用并回退纯托管/系统基线，绝不进入 Box2D 的 static+dynamic dual-build fan-out（架构 §14.4、§15）。本层不触碰 sim/physics 任何运行时行为，仅决定编译/链接/打包形态，不与四大基石、checkerboard、单缓冲、32px 上限等任何不变式相关。

---

## 2. 技术栈与依赖

与 `00-conventions-and-techstack.md` 完全一致，不另立选型：

- 运行时/语言：.NET 10 LTS / C# 14（架构 §13）。
- 主发行编译模式：**CoreCLR 自包含 + ReadyToRun（R2R composite）**——`PublishReadyToRun=true` + `PublishReadyToRunComposite=true` + `SelfContained=true`，保留 Tiered Compilation/Tier-1 重 JIT/Dynamic PGO（架构 §12.3、§13）。
- 次发行编译模式：**NativeAOT**，每 RID 单独产物，`PublishAot=true` + 显式 `IlcInstructionSet`（架构 §12.3、R3）。
- 开发态：纯 JIT（不在本文件范围，仅作对照）。
- native 依赖：**Box2D v3.1（vendored C 源）**，唯一 sim-native / dual-build 静态承载依赖（不变式 #10）；动态 `.dll/.so/.dylib` 供 CoreCLR/R2R、静态 `.lib/.a` 供 NativeAOT；其余 native（OpenAL Soft、可选 ANGLE、`PixelEngine.UI` 的 RmlUi/Ultralight）始终 dynamic-only 或系统分发并可门控回退。
- native 构建工具：**CMake**（≥3.21，支持 toolchain-file 交叉编译与 multi-config），编译器 MSVC（win）、clang（linux/mac，mac 用 Apple clang）。
- 托管侧打包友好库（均 reflection-free / 源生成 / AOT-trim 友好，架构 §9.1、§13）：`Silk.NET.*`、`Hexa.NET.ImGui`（+ Backends）、`System.Text.Json` 源生成、`K4os.Compression.LZ4`、`Microsoft.CodeAnalysis.CSharp`（脚本编译器，发行时随产物分发但本身不参与 trim 根，见 §3.6）。
- CI：GitHub Actions（与 `01` 的 `ci.yml` 同栈），新增 `release.yml`；runner：`windows-latest`、`ubuntu-latest`、`macos-latest`（含 Apple silicon）。
- 签名：macOS `codesign` + `notarytool` + `stapler`（Developer ID Application 证书）；Windows Authenticode `signtool`（可选）；Linux 仅产 `SHA256SUMS`（无强制签名）。

目标 6 RID（架构 §15、§12.3；`00` §3）：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`——这是**长期一等支持目标 RID 全集**（`plan/00 §3` 不变）。本文件在其上叠加一层「当前发行激活子集」gate（见 §2.1），不修改 RID 全集定义。

### 2.1 RID 激活矩阵与门控机制

RID 分两类，二者**共用同一套构建/发布/打包/审计代码路径**，差异只在「是否进入激活集」：激活集（Active）= `win-x64`（主）+ `win-arm64`（条件激活）；保留集（Reserved / Dormant）= `linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`（矩阵位、runner 映射、toolchain、ISA 分组、codesign 步骤全部保留且保持可编译，仅不进入当前发布调度）。目标是把「哪些 RID 现在真发」收敛到一处声明，让 CI 矩阵、产物审计、发行证据预检全部从它派生；重新激活任一 RID 只需改这一个文件的一个布尔，无需改工作流 YAML、审计脚本或预检脚本的任何逻辑。

**单一真相源 `tools/release-rids.json`**：

```json
{
  "channels": ["r2r", "aot"],
  "rids": [
    { "rid": "win-x64",    "active": true,  "runner": "windows-latest", "shell": "pwsh", "smoke": "native",    "codesign": false },
    { "rid": "win-arm64",  "active": true,  "runner": "windows-latest", "shell": "pwsh", "smoke": "load-only", "codesign": false },
    { "rid": "linux-x64",  "active": false, "runner": "ubuntu-latest",  "shell": "bash", "smoke": "native",    "codesign": false },
    { "rid": "linux-arm64","active": false, "runner": "ubuntu-latest",  "shell": "bash", "smoke": "qemu",      "codesign": false },
    { "rid": "osx-x64",    "active": false, "runner": "macos-15-intel", "shell": "bash", "smoke": "native",    "codesign": true  },
    { "rid": "osx-arm64",  "active": false, "runner": "macos-14",       "shell": "bash", "smoke": "native",    "codesign": true  }
  ]
}
```

字段语义：`active` 是唯一 gate 开关；`runner`/`shell`/`smoke`/`codesign` 是每 RID 的固有元数据（对 dormant RID 也完整保留，保证「翻 true 即可用」）。`smoke` 取值 `native`（本机跑 Demo `--smoke`）/ `load-only`（缺仿真时降级为加载校验 + release notes 人工门禁）/ `qemu`（QEMU user-mode 仿真），取代原 §3.2 内散落的跨架构处理约定。

**win-arm64「条件激活」**：默认 `active:true` 但 `smoke:load-only`——Windows x64 runner 上做加载校验、构建产物必出，真机 smoke 留人工门禁（与现状一致，不伪造 arm64 运行证据）。`release.yml` 增 `workflow_dispatch` 输入 `include_win_arm64`（默认 `true`）；运维只想发纯 x64 时置 `false`，setup job 生成矩阵时把 win-arm64 当 dormant 过滤掉。tag 触发的命令行发布默认按 json 的 `active` 走。

**CI 矩阵参数化（动态矩阵）**：`release.yml` 新增前置 `setup` job（阶段 0），用可复用 pwsh `tools/release-matrix.ps1`（新增）读 `release-rids.json` + `include_win_arm64`，按 `active && (rid≠win-arm64 || include_win_arm64)` 过滤后与 `channels` 笛卡尔积，`ConvertTo-Json -Compress` 写 `$GITHUB_OUTPUT` 三份 output：`native-matrix`（按 runner 分组的激活 RID，`rids` 为空格分隔串沿用现有 build-native 循环入参格式）、`build-matrix`（激活 `rid × channel × 元数据`，供 publish/verify/sign-package 三 job 的 `strategy.matrix` 用 `fromJSON(needs.setup.outputs['build-matrix'])` 消费）、`expected`（`{ activeRids:[...], packageCount:N, assetCount:N+1, channels:[...] }`，其中 `packageCount = activeRids.length × channels.length`、`assetCount = packageCount + 1`（+1 为 `SHA256SUMS`），供 release job 审计/预检/证据 manifest 用）。四个 job 的 `runs-on`/`shell` 从矩阵条目取（`${{ matrix.runner }}` / `${{ matrix.shell }}`），YAML 里不再出现静态 6-RID 列表；dormant RID 不在 `build-matrix` 中故其 job 不被调度，但 YAML 结构、runner 映射逻辑、codesign step 一字不删。当前激活集（win-x64 + win-arm64 × {r2r, aot}）→ `packageCount=4`、`assetCount=5`；`include_win_arm64=false` → `2`/`3`；全 6 RID 恢复 → `12`/`13`。

**审计与预检参数化**：`tools/audit-release-artifacts.ps1|.sh` 把硬编码 `$rids=@(6个)` 改为接收 `-ActiveRids`（逗号分隔，默认读 `release-rids.json` 的 active 集），`--require-all` 语义改为「对激活集 require-all」，`packages.Count` 期望从常量 `12` 改为 `activeRids.Count × channels.Count`，「missing package」「同名展开目录」等逐 RID 断言只遍历激活集，dormant RID 产物缺失**不**报错；原有所有布局纪律（根目录禁运行时依赖、`app/content` 禁重复、AOT 禁动态 Box2D、SHA256SUMS 全覆盖等）逐 RID 不变。`tools/release-evidence-preflight.ps1|.sh` 的 `$rids`/`package_count`/`expanded_package_count`/`required_rids`/`uploaded_asset_count`/`Test-DeterministicHashRows` 必需行集/SHA256SUMS 覆盖集全部改由 `-ActiveRids`/`-ExpectedPackageCount` 派生（默认读同一 json）；AOT `simdProbeKind`（`x64_ymm_zmm` / `arm64_neon`）、tag 一致性、run identity 等既有锁定逻辑不动，只是遍历范围随激活集缩放。**审计/预检的发行包命名正则保持宽松**，本轮仅改 `$rids` 枚举与 count/expected 派生，不收紧命名断言。

**边界声明（务必）**：`release-rids.json` 只 gate**发行调度**；`ci.yml` 与 `plan/16` 的 6-RID 构建/测试矩阵、以及 `tools/ci-matrix-evidence-preflight.ps1` 保持 6 RID（cross 用 build-only）**不随发行门控收敛**——这是 dormant RID 的编译保证后盾，避免其退化为无编译保证的 TODO-later（守 `AGENTS §2` 无占位红线）。

**保留兼容的落地做法（不删跨平台设计）**：`native/toolchains/{linux,osx}-*.cmake`、`tools/build-native.*`、`tools/publish-*.**`、`tools/package.*`、`tools/codesign-macos.sh`、`Directory.Build.props` 的 6 组 `IlcInstructionSet` 全部保留、保持可编译/可运行、不加任何 `#if`；dormant 化只发生在「激活清单 + 矩阵生成」一层（`active:false` + setup job 过滤）。重新激活流程：把目标 RID 的 `active` 翻 `true` → 无需改任何 YAML/脚本逻辑 → 下次 tag 发布该 RID 自动进 native→publish→verify→sign-package→release 全链路，审计/预检期望数量自动 +1 组；osx-* 翻 true 时 codesign step 因 `codesign:true` 元数据自动生效（凭据缺失仍按 §3 标 `- [!] 阻塞：缺 macOS 签名凭据`，不静默出未签名产物）。

---

## 3. 详细设计

### 3.1 双发行通道与产物矩阵

每个 RID 产出两条独立通道，共 12 个基础产物（架构 §12.3、§13、R5）：

主通道 **R2R**：CoreCLR 自包含 + R2R composite。可移植 codegen + 启动期 native image，热方法运行时按真实 CPU light-up SIMD。这是面向最终用户的默认下载。

次通道 **AOT**：NativeAOT 单文件原生可执行，启动最快、footprint 最小，但 ISA 在编译期固定，**只对已知硬件分发**（架构 R3）。每 RID 的 `IlcInstructionSet` 见 §3.4。

两条通道在 CI 中**都必须构建并冒烟验证**，这是「debug 正常 publish 崩」（架构 R5）的核心防线：R2R 走动态 Box2D、AOT 走静态 Box2D，二者链接路径不同，必须各自验证（§3.7、§5）。

### 3.2 RID → runner 映射与交叉编译约束

NativeAOT 不能跨 OS 编译（必须在目标 OS 上构建）；R2R/crossgen2 可跨 RID 目标但运行需对应平台；Box2D native 走 CMake toolchain-file 在同 OS 内交叉到另一架构。据此固定映射：

| RID | runner | R2R 构建 | AOT 构建 | 本机可运行 smoke |
|---|---|---|---|---|
| `win-x64` | `windows-latest` | 原生 | 原生 | 是 |
| `win-arm64` | `windows-latest` | crossgen2 跨架构 | AOT 跨架构（arm64 工具链） | 否→见下 |
| `linux-x64` | `ubuntu-latest` | 原生 | 原生（clang） | 是 |
| `linux-arm64` | `ubuntu-latest` | crossgen2 跨架构 | AOT 交叉（`aarch64` clang sysroot） | 否→QEMU |
| `osx-x64` | `macos-latest`(arm) | crossgen2 跨架构 | AOT 交叉（`-arch x86_64`） | 否→见下 |
| `osx-arm64` | `macos-latest`(arm) | 原生 | 原生 | 是 |

跨架构无法本机运行的 smoke 处理：`linux-arm64` 用 QEMU user-mode 仿真运行；`win-arm64`/`osx-x64` 在 CI 无仿真时标 `- [!]`-able 的「手动门禁」——构建必过、smoke 在缺仿真时降级为「加载校验（产物结构 + 依赖完整性静态检查）」并在 release notes 标注需目标机人工验证，不得静默跳过（架构 R5）。

### 3.3 R2R 发行配置（主通道）

在 `demo/PixelEngine.Demo/PixelEngine.Demo.csproj`（发行入口为 Hosting 装配后的 Demo）与 `Directory.Build.props` 的发行属性组中定档：

```xml
<!-- 由 tools/publish-r2r.* 以 -p:Channel=R2R 激活 -->
<PropertyGroup Condition="'$(Channel)'=='R2R'">
  <SelfContained>true</SelfContained>
  <PublishReadyToRun>true</PublishReadyToRun>
  <PublishReadyToRunComposite>true</PublishReadyToRunComposite>
  <PublishSingleFile>false</PublishSingleFile>   <!-- 保留 runtimes/ 布局，便于 native 解析与排错 -->
  <TieredCompilation>true</TieredCompilation>
  <TieredPGO>true</TieredPGO>                      <!-- Dynamic PGO，架构 §12.3 -->
  <InvariantGlobalization>true</InvariantGlobalization>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>partial</TrimMode>                     <!-- R2R 通道用 partial，安全且仍瘦身；见 §3.6 -->
</PropertyGroup>
```

不固定 ISA：R2R 通道**绝不**设置任何 baseline ISA property，依赖 CoreCLR 运行时检测 + Tier-1 重 JIT 完成 light-up（架构 §12.3、§15）。

### 3.4 NativeAOT 发行配置（次通道，显式 ISA）

```xml
<!-- 由 tools/publish-aot.* 以 -p:Channel=AOT 激活 -->
<PropertyGroup Condition="'$(Channel)'=='AOT'">
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <TrimMode>full</TrimMode>
  <IlcOptimizationPreference>Speed</IlcOptimizationPreference>
  <IlcFoldIdenticalMethodBodies>true</IlcFoldIdenticalMethodBodies>
</PropertyGroup>
<!-- 每 RID 显式 ISA（架构 R3：AOT 默认 SSE2 baseline 会静默砍 SIMD） -->
<PropertyGroup Condition="'$(Channel)'=='AOT' and '$(RuntimeIdentifier)'=='win-x64'">
  <IlcInstructionSet>x86-x64-v3</IlcInstructionSet>  <!-- avx2,bmi1,bmi2,fma,lzcnt,popcnt,movbe -->
</PropertyGroup>
<!-- linux-x64/osx-x64 同上 x86-x64-v3；arm64 三者用 base+lse+rcpc（NEON 为基线） -->
<PropertyGroup Condition="'$(Channel)'=='AOT' and ($(RuntimeIdentifier.EndsWith('arm64')))">
  <IlcInstructionSet>apple-m1</IlcInstructionSet>    <!-- arm64：NEON 基线 + lse/rcpc；osx 用 apple-m1，linux/win-arm64 用 armv8.2-a+lse -->
</PropertyGroup>
```

x64 三 RID 统一以 **x86-64-v3（AVX2 家族）** 为已知硬件基线；可选再产一个 `-avx512` 变体（`x86-x64-v4`）作单独命名产物（§3.8），但默认不发，以「限已知硬件」为准（架构 R3、§12.3）。每个 AOT 产物在 CI 中必须经反汇编/SIMD 探针验证 ymm（或 v4 的 zmm）真实出现，否则视为退化失败（架构 R3、§5）。arm64 三 RID 以 NEON 为基线，附 `lse`（原子）与 `rcpc`。

### 3.5 Box2D dual-build 矩阵与 native 资产落位

Box2D 是唯一需 dual-build 的 native 依赖（不变式 #10、架构 §14.4）。CMake 构建矩阵：6 RID × {SHARED, STATIC} = 12 个 native 产物，由 `tools/build-native.*` 调用 `native/box2d/CMakeLists.txt`（其 CMake 源编译细节归 `01`/`06`，本文件定义其发行矩阵契约）。

每 RID 用 toolchain-file `native/toolchains/<rid>.cmake` 指定目标三元组、架构、sysroot、`CMAKE_OSX_ARCHITECTURES`。两次配置/构建：

- `-DBUILD_SHARED_LIBS=ON` → `box2d.dll`（win）/ `libbox2d.so`（linux）/ `libbox2d.dylib`（osx），供 R2R/CoreCLR 运行时 `dlopen`/`LoadLibrary`。
- `-DBUILD_SHARED_LIBS=OFF` → `box2d.lib`（win）/ `libbox2d.a`（linux/osx），供 NativeAOT 静态链。

native 资产落位（架构 §14.4、§15）：

R2R/framework-dependent/自包含布局——动态库放 `runtimes/<rid>/native/`，由一个发行 `.targets`（`native/PixelEngine.Box2D.targets`，被 `PixelEngine.Interop` 引入）按 `$(RuntimeIdentifier)` 把对应动态库以 `<Content Include>` + `Link=runtimes\$(RuntimeIdentifier)\native\<lib>` + `CopyToOutputDirectory=PreserveNewest` 注入；SDK 在自包含 publish 时把目标 RID 的 native 解析到产物（host 自动定位）。

AOT 布局——同一 `.targets` 在 `Condition="'$(PublishAot)'=='true'"` 下改用 `<NativeLibrary Include="...box2d.(lib|a)" />`（RID-gated），由 ILC 静态链进单一可执行；此时**不**产出动态 Box2D（架构 §14.4）。

Linux 链接纪律：**动态链 glibc，绝不静态链 libc**（架构 §14.4、§15）——CMake toolchain 与 AOT `LinkerArg` 均不得加 `-static`/`-static-libc`；只静态链 Box2D 自身目标。

OpenAL/ANGLE：两条通道均动态——`Silk.NET.OpenAL` 的 native 经其 NuGet `runtimes/<rid>/native/` 随产物分发；ANGLE（可选回退）同样动态置于 `runtimes/<rid>/native/`。AOT 通道下二者仍以运行时 P/Invoke 动态加载，不进静态链（保持 fan-out 收敛，不变式 #10、架构 §14.4）。

HTML UI native（`plan/20` 的 `PixelEngine.UI` 后端 RmlUi 及可选 Ultralight）：作 **OpenAL/ANGLE 同级的 dynamic-only 依赖**处理——native 落 `runtimes/<rid>/native/`、随玩家包分发、纳入 `SHA256SUMS`、在包内 `LICENSE`/许可声明中登记（RmlUi=MIT、Ultralight=商业许可），AOT 通道下经运行时 P/Invoke 动态加载或按 `plan/20 §7` 的 `#10` 处置门控排除，**绝不进 Box2D dual-build**（`plan/20` 若启用 UI 时也不新增第二个 dual-build native，守不变式 #10、架构 §14.4）。UI native 是否随激活 RID 一并出包，遵循 §2.1 激活集（当前 Windows-first 仅 win-x64 + 纯托管基线变体，见 §3.11）。

### 3.6 Trim 配置（reflection-free）

引擎全程 reflection-free 是 AOT/trim 可行的前提（架构 §9.1、§13）。在 `Directory.Build.props` 对所有 `src/` 引擎库统一：

```xml
<PropertyGroup>
  <IsTrimmable>true</IsTrimmable>
  <IsAotCompatible>true</IsAotCompatible>          <!-- 开启 IL2xxx/IL3xxx 分析器 -->
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>
```

CI 把 `IL2026/IL2046/IL2070/IL2075/IL3050/IL3056` 等 trim/AOT 警告**提升为 error**（与 `AGENTS.md §4` 的 `TreatWarningsAsErrors` 一致），任何反射/动态代码在编译期即暴露，杜绝运行期 trim 崩溃。

序列化：`System.Text.Json` 全程走源生成 `JsonSerializerContext`（materials.json/reactions.json/world manifest 的上下文由 `04`/`07` 定义，命名 `PixelEngineJsonContext`），本文件只要求发行配置不引入 `JsonSerializerOptions` 反射回退，保证零 `TrimmerRootDescriptor`。Silk.NET、Hexa.NET.ImGui 均无反射根，无需 root 文件。

脚本系统例外（架构 §13、`11-scripting-system.md`）：Roslyn（`Microsoft.CodeAnalysis.CSharp`）与可回收 ALC 在运行时生成/加载用户脚本程序集，**它本身就是运行时代码加载，不可被 trim 视为死代码**。处理：脚本编译子系统所在程序集标 `<IsTrimmable>false</IsTrimmable>` 局部豁免，并随产物完整分发 Roslyn 依赖；用户脚本编译产物是运行时 JIT（即便宿主是 R2R/AOT，ALC 内仍是 JIT），不参与本管线的 trim。这一豁免严格限定在脚本子系统，不污染 sim/physics/render 热路径程序集。

### 3.7 自包含发行布局

最终用户下载包采用「玩家友好启动布局」：压缩包根目录保留清晰的启动入口（Windows 为 `PixelEngine Demo.exe`，Linux/macOS 为 `PixelEngine Demo.sh`）、README / 许可证 / 校验说明、`content/` 内容目录与 `app/` 依赖目录；`.dll`、`.deps.json`、`.runtimeconfig.json`、runtime/native 等运行必须依赖全部放入 `app/`。`content/` 只能位于包根，禁止在 `app/content/` 再复制一份内容资产。`.pdb`、XML 文档、多语言 `*.resources.dll` 卫星程序集与 `createdump(.exe)` 诊断辅助程序不进入玩家包。Windows R2R 包根 `PixelEngine Demo.exe` 是重写 apphost 载荷的真实启动 exe，指向 `app/PixelEngine.Demo.dll`；Windows AOT 包根 `PixelEngine Demo.exe` 是 NativeAOT 主程序，启动时把 `app/` 加入 native search path。这样既保留玩家可直接双击的根目录 exe，又避免根目录被运行时依赖淹没。发行审计必须验证根目录不直接堆放托管程序集、不存在 `app/content/` 重复内容、不在 `app/` 下重复保留 Windows 原始 `PixelEngine.Demo.exe`，且玩家包整体不携带调试符号、XML 文档、诊断辅助程序和本地化卫星资源文件。

R2R 通道（非单文件，保留可排错布局）：

```
PixelEngine-<version>-<rid>/
├─ PixelEngine Demo.exe / PixelEngine Demo.sh   # 玩家启动入口
├─ README.txt / LICENSE / SHA256SUMS            # 少量说明文件
├─ content/                                     # 内容资产（§3.9）
│  ├─ materials.json
│  ├─ reactions.json
│  ├─ textures/
│  ├─ audio/
│  └─ scenes/default.scene
└─ app/
   ├─ PixelEngine.Demo.dll                      # R2R 托管入口
   ├─ PixelEngine.*.dll                         # 引擎 + Demo 托管程序集（R2R composite 后含 native image）
   ├─ *.dll                                     # 自包含的 CoreCLR + BCL
   ├─ runtimes/<rid>/native/
   │  ├─ box2d.(dll|so|dylib)                   # 动态 Box2D（本通道）
   │  ├─ (openal soft native)                   # 动态
   │  └─ (angle native, 可选)                   # 动态
   └─ PixelEngine.Demo.runtimeconfig.json / .deps.json
```

AOT 通道（单一原生可执行）：

```
PixelEngine-<version>-<rid>-aot/
├─ PixelEngine Demo.exe / PixelEngine Demo.sh
├─ README.txt / LICENSE / SHA256SUMS
├─ content/                        # 同上
└─ app/
   ├─ (openal soft native)            # 仍动态，置于可执行同目录
   └─ (angle native, 可选)            # 动态
```

**玩家包 vs 编辑器工具包分流**：以上布局是**玩家包**（`demo/PixelEngine.Demo` 或后续 Player app，以 `-p:PixelEnginePlayerBuild=true` 发布）的最终形态。玩家包**绝不含** `PixelEngine.Editor.dll` 与编辑器专属 `ImGuizmo/ImPlot` 闭包——该剥离由需求 1 的 GUI 宿主中性化重构落地（`Hosting` 删除对 `PixelEngine.Editor` 的硬 `ProjectReference`、玩家 HUD 所需 ImGui host 下沉到中性程序集 `PixelEngine.Gui`、`DemoProgram.cs` 改用 `PixelEngine.Gui` 中性 host），玩家包审计据此新增断言：`app/` 内出现 `PixelEngine.Editor.dll` 或任意 `ImGuizmo*/ImPlot*` 即 **fail**。审计**允许**玩家 HUD 所需的 `Hexa.NET.ImGui`（撤销早期「拒绝 ImGui」的不可满足表述）——被拒的是 `PixelEngine.Editor.dll` 与编辑器专属面板闭包，不是 ImGui 本体。**编辑器工具包**（`apps/PixelEngine.Editor.Shell`，见 `plan/19`）是开发/内测分发物，含完整编辑器闭包，与玩家 6-RID 矩阵解耦、不受 §2.1 激活集约束，不走本节玩家包审计的 player-only 断言。

发行布局的两方案权衡与选型锁定见 §3.7.1；编辑器触发的一键出包入口见 §3.11。

### 3.7.1 发行布局方案权衡与选型（锁定需求 4）

**问题**：默认 `dotnet publish`（`PublishSingleFile=false`）把托管 dll、`*.deps.json`、`*.runtimeconfig.json`、自包含 CoreCLR+BCL、`runtimes/<rid>/native/` 全部摊在输出根，玩家在几十个 dll 里找不到 exe。§3.7 已给出玩家包布局，此处把「为什么这样」的两方案权衡与选型显式锁进设计。

**方案 (a) Unity 式 `app/` 子目录（本项目采用，推荐）**：包根只保留玩家可见的少量入口（`PixelEngine Demo.exe`/`PixelEngine Demo.sh`、`README.txt`、`SHA256SUMS`、`content/`、`app/`），全部运行时依赖落 `app/`。.NET 探测路径处理的**关键洞察是「只搬 apphost、不搬程序集」**：R2R 通道下 `app/` 内保留完整原生 publish 布局（`PixelEngine.Demo.dll` + `.runtimeconfig.json` + `.deps.json` + BCL + `runtimes/<rid>/native/` 原样共处），只把 apphost 可执行体搬到包根，并用 `tools/package.ps1` 的 `Set-AppHostRelativeAssemblyPath` 把 apphost 内嵌的相对程序集路径从 `PixelEngine.Demo.dll` 改写为 `app\PixelEngine.Demo.dll`；host 以**被加载的 .dll 所在目录**计算 `APP_BASE`（不是 exe 所在目录），因此 `deps.json`/`runtimeconfig.json` 解析、BCL 探测、`runtimes/<rid>/native/` native 解析全部相对 `app/` 自然成立——**无需** `additionalProbingPaths`、自定义 `AppContext` 探测或 `DllImportResolver`（搬的是壳，探测模型原封不动，这是 (a) 低风险的根本原因）。AOT 通道下包根 exe 是 NativeAOT 原生主程序（无 apphost/dll 分离），`app/` 只放动态 native（OpenAL/ANGLE/HTML UI，Box2D 已静态链入 exe），Demo 入口启动早期把 `app/` 加入 native 搜索路径（Windows `AddDllDirectory`+`SetDefaultDllDirectories`，POSIX 由 `.sh` 先 `cd app` 再 `--content ../content` 反指 exec，等价 `RPATH=$ORIGIN`），同样无需自定义解析器。各维度：R2R composite 完全相容（只是 `app/` 里多几个文件）、native 依赖解析简单、Box2D dual-build 无影响、启动无额外开销（无自解压/临时缓存）、排错难度最低（所有文件磁盘明面，可 `dotnet PixelEngine.Demo.dll` 手动复现、可 attach 调试、可逐 dll 校验）。代价仅是「根 exe + app/ 两级」的心智，而这正是 Unity 玩家熟悉的形态。

**方案 (b) 单文件 `PublishSingleFile=true`（评估后否决）**：包根仅一个 exe（托管 dll 全打进单 exe）。R2R composite 单文件会让整个托管闭包成为一个大 blob，首次启动触发一次性自解压（native 库/必要时 crossgen 镜像）到 `DOTNET_BUNDLE_EXTRACT_BASE_DIR`（默认 `%TEMP%`/`$TMPDIR`）缓存目录，引入首启延迟、临时磁盘占用与「陈旧缓存」难排查失败模式；Box2D 动态/OpenAL/ANGLE 要么内嵌走自解压缓存（放大成本）要么留 exe 旁（则非真单文件，收益打折）；单文件倾向 full-trim 而 R2R 主通道用 `TrimMode=partial`（§3.6），组合产出既大又更难调试的 exe；崩溃现场只有一个 exe 加一个临时解压缓存，比 `app/` 明面布局显著难定位。

**选型结论（锁定，不可回退）**：采用 **(a) Unity 式 `app/`**，`Directory.Build.props` R2R 属性组保持 `PublishSingleFile=false` 不变（注释指向本节），AOT 通道天然单原生可执行。(a) 已用「搬 apphost 壳、探测模型不动」零成本拿到同样干净的根目录，故否决 (b)。可行性锚点：`tools/package.ps1` 的 `Set-AppHostRelativeAssemblyPath` 已落地、`tools/audit-release-artifacts.*` 已强制该布局（根目录禁托管 dll/deps/runtimeconfig、禁 `app/content/` 重复、禁 `app/` 下第二启动 exe）、`docs/release-reports/2026-07-02-win-x64-publish.md` 的 `win-x64/r2r` smoke 通过。

**中间产物 ≠ 玩家包（消除「找不到 exe」根因）**：用户看到的「根目录一堆 dll」是**原始 publish 中间产物** `artifacts/publish/<rid>-<channel>/`（`dotnet publish` 扁平输出，**非玩家包**），仅供 CI/package 装配消费，已有 `_PUBLISH_INTERMEDIATE_README.txt` 指向真正玩家包，且 `tools/package.*` 另产固定入口 `artifacts/PixelEngine Demo/` 供本机直接双击。玩家包（`artifacts/package/PixelEngine-Demo-<version>-<rid>-<channel>/` 及其归档）才是 (a) 布局的最终形态。

### 3.8 版本号与产物命名

版本来源：`Directory.Build.props` 的 `<VersionPrefix>`（SemVer，如 `0.1.0`）；CI 在 tag `v<semver>` 触发时以 tag 覆盖版本，`<InformationalVersion>` 嵌 `+<gitShortSha>`；`<Deterministic>true</Deterministic>`（已由 `00` §6 约定）保证可复现。

产物压缩与命名（`<channel>` ∈ `r2r|aot`；可选 ISA 变体后缀 `-avx512`）：

```
PixelEngine-Demo-<version>-<rid>-<channel>.zip      # win-* 用 zip
PixelEngine-Demo-<version>-<rid>-<channel>.tar.gz   # linux-*/osx-* 用 tar.gz（保留 +x 权限）
SHA256SUMS                                           # 汇总所有产物校验和
```

### 3.9 内容资产打包

`content/`（materials.json、reactions.json、材质纹理、音效、默认场景）随每个产物原样打包到包根 `content/`（架构 §16.3、§11）。打包脚本以开发态 `content/` 为单一真相源拷贝，不重排、不改名（材质稳定字符串键的可移植性不受打包影响，不变式 #8、架构 §11.2）。产物内 `content/` 与开发态目录结构一致；R2R 真实程序集位于 `app/` 时，Demo 默认 content 解析会优先使用包根 `content/`，保证 R2R/AOT 两通道、激活集各 RID 下加载行为一致。

**demo-playability 新内容文件**：`plan/13` 引入的 `content/weapons.json`（武器/道具库数据）与 `plan/04` 新增材质（`gravel`/`crystal` 等）的纹理资产，均随开发态 `content/` 一并纳入单一真相源与内容包核对（新增 `materials.json` 字段亦随其原文件走，不另立打包分支）；`materials.json`/`reactions.json` 在任何打包模式下**恒含**，守稳定字符串键可移植性（不变式 #8）。

**编辑器触发布局的可选内容裁剪（仅 build-player，默认整包不变）**：默认 CI 五阶段与 `tools/package.*` 手工出包保持「整包 `content/` 原样打包」不变。仅当经 §3.11 的 `build-player` 编排、且面板启用「按入包场景过滤」选项时，才在 **staging `content/`** 内生成 `startup.json`（`{ "startScene": "scenes/<name>.scene" }`，把面板选定启动场景烘焙进包，player 的 `DemoStartupOptions` 优先读它、缺省回落既有默认场景，从而不靠 CLI `--scene` 即可用选定启动场景）并按 `-IncludeScene` 清单只拷 `content/scenes/` 被选场景；此模式下 `materials.json`/`reactions.json`/纹理/音效仍恒拷（守 #8），audit 的「必含场景」断言相应放宽为「必含被声明的启动场景文件」。默认（未启用过滤）整包 content 单一真相源、原样打包语义不变。

### 3.10 发布 CI 工作流

新增 `.github/workflows/release.yml`，触发于 tag `v*`（手动 `workflow_dispatch` 可选，含 `include_win_arm64` 输入）。复用 `01` 的 `ci.yml` 的 setup-dotnet/缓存 action 片段。结构（本轮修订为动态矩阵，见 §2.1）：

阶段 0「setup」：跑 `tools/release-matrix.ps1` 读 `release-rids.json` + `include_win_arm64`，输出 `native-matrix`/`build-matrix`/`expected` 三份 JSON output。后续 native/publish/verify/sign-package 四 job 的 `strategy.matrix` 改为 `fromJSON(needs.setup.outputs.*)`，`runs-on`/`shell`/smoke 分支/`codesign` 分支从矩阵条目元数据取，删除四处静态 6-RID 列表（等价逻辑迁入 json）；release job 的 `package_count`/`expanded_package_count`/`rids`/deterministic-hash 循环/`uploaded_asset_count` 期望改为消费 `needs.setup.outputs.expected`，`audit-release-artifacts.*`/`release-evidence-preflight.*` 传 `-ActiveRids`。dormant RID 不在矩阵内故不调度，codesign step（osx-* 元数据 `codesign:true` 驱动）保留、凭据缺失仍按 `- [!] 阻塞` 语义。

阶段 1「native」：在三个 runner 上各自跑 `tools/build-native.*`，按 §3.2 映射构建该 runner 负责 RID 的 Box2D 静/动产物，上传为 artifact。

阶段 2「publish」：6 RID × 2 通道 的 job 矩阵，下载对应 native artifact，跑 `tools/publish-r2r.*` 或 `tools/publish-aot.*`；AOT job 额外跑 SIMD 探针（§3.4）。

阶段 3「verify」：对每个产物跑 `tools/verify-publish.*`（§5、架构 R5），本机可运行的直接跑 Demo `--smoke`，跨架构的按 §3.2 走 QEMU 或降级加载校验。

阶段 4「sign & package」：macOS 产物跑 `tools/codesign-macos.sh`（codesign Developer ID + notarytool 提交 + stapler 装订，架构 §15）；Windows 可选 Authenticode；`tools/package.*` 装配布局、拷 `content/`、压缩、生成 `SHA256SUMS`。

阶段 5「release」：把全部产物 + `SHA256SUMS` 附到 GitHub Release。

### 3.11 编辑器触发的一键出包入口 `build-player`

CI 五阶段是「多 RID × 双通道」的批量矩阵；编辑器内的 BuildSettings 面板（`plan/19`）需要的是「**单 RID、单通道、一键、带实时进度**」的出包入口。为此新增 `tools/build-player.ps1` 与 `tools/build-player.sh`，作为**唯一的一键玩家包编排器**：把既有 `build-native → publish-r2r|publish-aot → verify-publish → package → audit-release-artifacts`（单 RID、非 `--require-all`）串成单进程顺序编排。编辑器**绝不重复实现**任何 publish/apphost 重写/布局装配/校验逻辑，只经 `System.Diagnostics.Process` 起 `build-player` 子进程、喂参数、收结构化输出（`plan/19 §5.4`/`§5.5` 的消费方）。

**参数通道**：`-Rid`（`win-x64`/`win-arm64`）、`-Channel`（`r2r`|`aot`）、`-Configuration`（`Debug`|`Release`）、`-Output`、`-Version`/`-InformationalVersion`（后者默认嵌 `git rev-parse --short HEAD`）、`-ProductName`、`-p:ApplicationIcon`（`.ico` 路径）、`-IncludeSymbols`、`-StartScene`、`-IncludeScene`（可重复，驱动 §3.9 场景过滤）、`-DevLayout`（开发布局开关，见下）。**`-ProductName` 落地**：`ProductName` 是玩家可见启动器/包内产品名，投射为 MSBuild `-p:Product` 与 `tools/package.*` 的根启动器命名参数；内部 .NET 入口 `AssemblyName` 默认保持稳定的 `PixelEngine.Demo`（带空格 `AssemblyName` 会触发 .NET/NuGet restore 的 ambiguous project name），因此 R2R 根 exe 仍命名为 `<ProductName>.exe`，但 apphost 载荷指向 `app/PixelEngine.Demo.dll`。如后续需要无空格内部入口名，可显式传 publish 脚本的 `-AssemblyName/--assembly-name`。`build-player` 保证玩家可见名与 `Set-AppHostRelativeAssemblyPath`、`audit-release-artifacts` 启动器命名断言不冲突（audit 从同一 `ProductName` 派生期望启动器，不写死 `PixelEngine Demo`）。

**RID/通道当前边界（Windows-first）**：AOT 通道**仅宿主 RID**（不能跨 OS 编译，架构 R3）→ 编辑器内当前只出 `win-x64/aot`；R2R 通道可经 crossgen2 跨架构，当前 Windows runner 上出 `win-x64`（及条件 `win-arm64`）。跨架构/非 Windows RID 在面板灰显注「由 CI/CLI 出」（`plan/19 §5.3`）。

**NDJSON 进度协议**：`build-player` 的 stdout 为逐行 NDJSON，`schema=pixelengine.build/v1`，每行形如 `{ "schema":"pixelengine.build/v1", "kind":"phase|progress|log|result", "phase":"native|publish|verify|package|audit|done", "percent":<0..100>, "level":"info|warn|error", "message":"...", "ts":"<ISO8601>" }`；面板逐行解析为 `BuildProgressEvent` 刷新进度/日志（`plan/19 §5.4`）。stderr 归 error 级。**结束契约**：子进程在 `-Output` 目录写 `build-result.json`（`{ ok, rid, channel, configuration, version, informationalVersion, packageArchive, packageDir, playerDir, launcherExe, sha256, sizeBytes, phaseTimingsMs{}, warnings[], error, exitCode }`），退出码 0=成功、非 0=失败；面板以 `build-result.json` + exit code 合成 `BuildResult`，非 0 且无结果清单时回退末尾 stderr/stdout + exit code 报错。取消由父进程 `Process.Kill(entireProcessTree:true)` 杀 dotnet/publish 子树；`build-player` 内各 publish 脚本每次先清理本 RID/配置的 publish 输出与 `src/**/bin·obj`，故被取消的半成品在下次运行时被清理，保证可重复性。

**dev-audit 模式（`-DevLayout`）**：「含调试符号」的开发向构建落**开发布局**（保留 pdb/xml），走 `audit-release-artifacts -DevLayout` 宽松模式——只放宽符号/文档噪音，仍查结构存在性与 player-only 断言（`app/` 无 `PixelEngine.Editor.dll`/`ImGuizmo*`/`ImPlot*`，允许 `Hexa.NET.ImGui` 核心）。`Release` + 无符号构建走完整 `audit-release-artifacts`，保发行不变式不被削弱。`build-player` 依 `-DevLayout`/`-IncludeSymbols` 分流到宽松或严格 audit，面板在结果区明示当前布局类型（`plan/19 §5.8`）。

**产物一致性锚点**：`build-player` 与 `tools/*` 手工出包复用同一管线与确定性打包（`tools/PixelEngine.Tools.DeterministicPackage`），故**同等参数下二者字节级一致**（同 entry 顺序/时间戳/权限/owner）。

---

## 4. 实现清单

发行配置与脚本

- [x] 在 `Directory.Build.props` 增 `Channel` 驱动的 R2R 属性组（§3.3）：`SelfContained`/`PublishReadyToRun`/`PublishReadyToRunComposite`/`TieredPGO`/`InvariantGlobalization`，**不设任何 baseline ISA**（架构 §12.3）。
- [x] 在 `Directory.Build.props` 增 AOT 属性组（§3.4）：`PublishAot`/`TrimMode=full`/`IlcOptimizationPreference=Speed`，并为 6 RID 各设显式 `IlcInstructionSet`（x64=x86-64-v3，arm64=NEON+lse+rcpc）（架构 R3）。
- [x] 写 `tools/publish-r2r.ps1` 与 `tools/publish-r2r.sh`：参数 `-Rid`，先清理当前 RID/架构的 Demo `bin/obj`、`src/PixelEngine.*` 架构输出与目标 publish 目录，再对 `demo/PixelEngine.Demo` 跑 `dotnet publish -c Release -r <rid> -p:Channel=R2R`，输出到 `artifacts/publish/<rid>-r2r/`，避免旧依赖残留污染发行包；该目录是 CI/package assembly 中间产物，脚本会写 `_PUBLISH_INTERMEDIATE_README.txt` 明确提示玩家入口在 `artifacts/package/PixelEngine-Demo-<version>-<rid>-r2r/`。
- [x] 写 `tools/publish-aot.ps1` 与 `tools/publish-aot.sh`：参数 `-Rid`，先清理当前 RID/架构的 Demo `bin/obj`、`src/PixelEngine.*` 架构输出与目标 publish 目录，再跑 `dotnet publish -c Release -r <rid> -p:Channel=AOT`，输出到 `artifacts/publish/<rid>-aot/`，避免旧 Roslyn/linked 闭包残留污染 NativeAOT；该目录同样只作为中间产物，脚本会写 `_PUBLISH_INTERMEDIATE_README.txt` 指向 `artifacts/package/PixelEngine-Demo-<version>-<rid>-aot/` 玩家包。
- [x] 写 `tools/verify-publish.ps1` 与 `tools/verify-publish.sh`：对给定产物目录跑 Demo `--smoke`（本机）或 QEMU/降级加载校验（跨架构），非零退出即失败（架构 R5）。
- [x] 写 `tools/package.ps1` 与 `tools/package.sh`：装配 §3.7 布局、拷 `content/`、按 RID 选 zip/tar.gz、产 `SHA256SUMS`、命名按 §3.8。
- [x] 调整 `tools/package.ps1` / `tools/package.sh` 输出玩家友好启动布局：包根目录保留 `PixelEngine Demo.exe`（Windows）或 `PixelEngine Demo.sh`（Linux/macOS）、README、包内 `SHA256SUMS`、`content/` 与 `app/`；`.dll`、`.deps.json`、`.runtimeconfig.json`、runtime/native 等运行必须依赖进入 `app/`，`.pdb`、XML 文档、多语言 `*.resources.dll` 卫星程序集与 `createdump(.exe)` 诊断辅助程序会在打包阶段剔除，`content/` 只能保留在包根且不能在 `app/content/` 重复出现，Windows 原始 `PixelEngine.Demo.exe` 不能留在 `app/` 形成第二个启动入口，publish 中间目录的 `_PUBLISH_INTERMEDIATE_README.txt` 也不会进入包根或 `app/`。Windows R2R 根 exe 通过 apphost 相对程序集路径指向 `app/PixelEngine.Demo.dll`，Windows AOT 根 exe 直接启动并由 Demo 入口把 `app/` 加入 native search path。脚本现在同时产出 `artifacts/package/<包名>/` 同名展开目录和默认 `artifacts/PixelEngine Demo/` 固定玩家入口目录供本机直接打开；固定入口目录每次先清空再同步，避免旧 zip、raw publish 文件、截图、调试符号或诊断辅助入口干扰玩家找 exe。脚本也会在打包前清理同 RID/channel 的旧展开目录与旧归档；`tools/audit-release-artifacts.*` 同时审计归档包与同名展开目录，拒绝缺失对应归档、缺失同名展开目录、根层运行时依赖、`app/content/` 重复内容、`app/PixelEngine.Demo.exe` 重复启动入口、玩家包内调试/文档/诊断辅助/本地化资源噪音和未覆盖文件的包内 SHA256SUMS，避免玩家入口层被历史残留淹没。

Box2D dual-build 矩阵与 native 落位

- [x] 写 6 个 toolchain-file `native/toolchains/{win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64}.cmake`（目标三元组/架构/sysroot/`CMAKE_OSX_ARCHITECTURES`），交叉项遵守「动态链 glibc，绝不 `-static` libc」（架构 §14.4、§15）。
- [x] 写 `tools/build-native.ps1` 与 `tools/build-native.sh`：参数 `-Rid`，对该 RID 跑 `BUILD_SHARED_LIBS=ON` 与 `OFF` 两次 CMake 配置+构建，产 12 矩阵中属于该 RID 的 4 件（动态库 + 静态库各 1，含 import lib 若有），落 `native/out/<rid>/{shared,static}/`。
- [x] 写 `native/PixelEngine.Box2D.targets`（被 `PixelEngine.Interop` 引入）：R2R/默认按 `$(RuntimeIdentifier)` 注入动态库到 `runtimes/<rid>/native/`（`<Content>`+`Link`+`PreserveNewest`）；`Condition` 命中 `PublishAot` 时改用 `<NativeLibrary Include>` 静态链（RID-gated）（架构 §14.4）。
- [x] 确认 OpenAL/ANGLE 在两通道均保持动态（不进 `<NativeLibrary>` 静态链），收敛 fan-out（不变式 #10、架构 §14.4）。

Trim 配置

- [x] 在 `Directory.Build.props` 对 `src/` 引擎库统一开 `IsTrimmable`/`IsAotCompatible`/`EnableTrimAnalyzer`/`EnableAotAnalyzer`（§3.6、架构 §9.1）。
- [x] CI 把 `IL2xxx`/`IL3xxx` trim/AOT 警告提升为 error（与 `AGENTS.md §4` 一致）。
- [x] 对脚本编译子系统程序集设 `IsTrimmable=false` 局部豁免（Roslyn/ALC 运行时代码加载）；R2R/开发通道保留 Roslyn 热重载依赖，NativeAOT 通道通过 `PIXELENGINE_NATIVEAOT` 排除 Roslyn/ALC 实现并编译显式禁用热重载的 stub，避免 AOT 产物携带脚本编译器闭包（§3.6、架构 §13、`plan/11`）。
- [x] 验证发行不引入任何 `TrimmerRootDescriptor`/反射回退（System.Text.Json 走 `PixelEngineJsonContext` 源生成）（架构 §16.3）。

发布 CI 工作流

- [x] 写 `.github/workflows/release.yml`，触发 tag `v*` + `workflow_dispatch`，复用 `01` 的 `ci.yml` setup/缓存片段。
- [x] 阶段 1「native」：三 runner 按 §3.2 映射跑 `tools/build-native.*`，上传 native artifact。
- [x] 阶段 2「publish」：6 RID × {r2r, aot} job 矩阵，下载 native artifact，跑对应 publish 脚本；AOT job 跑 SIMD 探针验证 ymm/zmm 出现（架构 R3）。
- [x] 阶段 3「verify」：每产物跑 `tools/verify-publish.*`；跨架构按 §3.2（`linux-arm64` QEMU；`win-arm64`/`osx-x64` 降级加载校验 + release notes 人工门禁标注）（架构 R5）。
- [x] 阶段 4「sign & package」：macOS 跑 `tools/codesign-macos.sh`；可选 Windows Authenticode；跑 `tools/package.*`。
- [x] 阶段 5「release」：上传激活集全部产物 + `SHA256SUMS` 到 GitHub Release；`github-release-upload.md` 必须声明 `uploaded_asset_count=packageCount+1`，并列出激活集全部 package 与 `SHA256SUMS` 的 `asset/<name>` → SHA256，`tools/release-evidence-preflight.ps1` 会对照 manifest/package hash 拒绝只写 `conclusion=success` 但没有 asset 覆盖清单的报告。

codesign / notarization

- [x] 写 `tools/codesign-macos.sh`：`codesign --options runtime --timestamp` 签 `osx-x64`/`osx-arm64` 可执行与动态库 → `notarytool submit --wait` → `stapler staple`（架构 §15）。
- [x] CI secrets 接入：Developer ID Application 证书（base64 p12）、`notarytool` API key/issuer；缺失时该 step 标 `- [!] 阻塞：缺 macOS 签名凭据` 而非静默出未签名产物。

版本与产物命名

- [x] `Directory.Build.props` 设 `VersionPrefix`（SemVer）+ `Deterministic` + `ContinuousIntegrationBuild`（CI 下）（§3.8）。
- [x] `release.yml` 以 tag 覆盖版本并嵌 `+<gitShortSha>` 进 `InformationalVersion`（§3.8；随发布 CI 节点落实）。
- [x] `tools/package.*` 按 `PixelEngine-Demo-<version>-<rid>-<channel>[.<variant>].{zip|tar.gz}` 命名，tar.gz 保留可执行权限位。

内容资产打包

- [x] `tools/package.*` 以开发态 `content/` 为单一源拷入每产物包根 `content/`，不重排/不改名（不变式 #8、架构 §11.2、§16.3）。
- [x] 验证本机 `win-x64` R2R/AOT 包内 `content/` 结构与开发态一致，含 `materials.json`/`reactions.json`/纹理/音效/场景（§3.9）。
- [x] 验证 R2R/AOT × 6 RID 产物内 `content/` 结构与开发态一致，引擎按相对路径定位成功（§3.9；随发布 CI 节点落实）。

RID 激活门控（win-first，§2.1）

- [x] 新增 `tools/release-rids.json`：6 RID 元数据（`rid`/`active`/`runner`/`shell`/`smoke`/`codesign`）+ `channels`，`win-x64`/`win-arm64` `active:true`、linux/osx 四个 `active:false`（§2.1）。
- [x] 新增 `tools/release-matrix.ps1`：解析 `release-rids.json` + `include_win_arm64`，输出 `native-matrix`/`build-matrix`/`expected` 三份压缩 JSON 到 `$GITHUB_OUTPUT`（§2.1）。
- [x] `release.yml` 增 `setup` job（阶段 0）+ `workflow_dispatch` 输入 `include_win_arm64`（默认 true）；native/publish/verify/sign-package 四 job 改 `fromJSON` 消费激活矩阵、`runs-on`/`shell` 取自矩阵条目；release job 期望数量改由 `expected` 派生；删除四处静态 6-RID 列表（等价逻辑迁入 json）（§2.1、§3.10）。
- [x] `tools/audit-release-artifacts.ps1|.sh` 增 `-ActiveRids`/`--active-rids`（默认读 json），`--require-all` 语义改为「激活集 require-all」，包数期望 `activeRids×channels`，逐 RID 断言只遍历激活集、dormant RID 缺失不报错；发行包命名正则保持宽松（仅改 `$rids` 枚举与 count/expected 派生）（§2.1）。
- [x] `tools/release-evidence-preflight.ps1|.sh` 的 `$rids`/`package_count`/`expanded_package_count`/`required_rids`/`uploaded_asset_count`/deterministic 行集/SHA256SUMS 覆盖集改由 `-ActiveRids`/`-ExpectedPackageCount` 派生（默认读同一 json）；PowerShell 版已完成并接入 `release.yml`，Bash 入口转发到同一实现并映射 `--active-rids`/`--expected-package-count`，tag/identity/`simdProbeKind` 锁定逻辑不动（§2.1）。
- [x] 显式边界回归：`ci.yml`/`plan/16` 的 6-RID 构建/测试矩阵与 `tools/ci-matrix-evidence-preflight.ps1` 保持 6 RID（cross 用 build-only）不随发行门控收敛，作为 dormant RID 编译保证后盾；`HostingProjectDisciplineTests.ReleaseRidGateDeclaresWindowsActiveSetAndMatrixOutputs` 已锁定 CI/preflight 不读取 `release-rids.json`（§2.1）。
- [x] dry-run 回归：把任一 dormant RID 翻 `active:true` 后（`workflow_dispatch` 演练），无需改任何 YAML/脚本逻辑即自动进全链路、审计/预检期望数量自动 +1 组；`HostingProjectDisciplineTests.ReleaseRidGateDryRunActivatesDormantRidFromConfigOnly` 用临时配置翻开 `linux-x64` 并验证矩阵扩展到 3 RID / 6 package（§2.1）。

发行布局选型（需求 4）

- [x] §3.7 增「中间产物 ≠ 玩家包」明确段落；§3.7.1 落地 (a)/(b) 权衡与 (a) 选型锁定正文（§3.7.1）。
- [x] （已实现）`tools/package.*` 输出 Unity 式 `app/` 布局、apphost 相对路径改写、noise 剔除、固定玩家入口目录。
- [x] （已实现）`tools/audit-release-artifacts.*` 强制 `app/` 布局、拒绝根目录运行时依赖 / `app/content` 重复 / `app/` 下第二启动 exe。
- [x] 确认 `Directory.Build.props` R2R 属性组 `PublishSingleFile=false` 作为 (a) 选型的锁定项，注释指向 §3.7.1；`HostingProjectDisciplineTests.ReleaseLayoutLocksPublishIntermediateAndSingleFileDecision` 已覆盖（§3.7.1）。

编辑器触发的 build-player 入口（§3.11）

- [x] 写 `tools/build-player.ps1` 与 `tools/build-player.sh`：唯一一键入口，串 `build-native → publish-r2r|publish-aot → verify-publish → package → audit`（单 RID、非 RequireAll），参数通道 `-Rid/-Channel/-Configuration/-Output/-Version/-InformationalVersion/-ProductName/-p:ApplicationIcon/-IncludeSymbols/-StartScene/-IncludeScene/-DevLayout`（§3.11）。
- [x] `build-player` stdout NDJSON 协议 `schema=pixelengine.build/v1`（`phase|progress|log|result` 行）+ 在 `-Output` 写 `build-result.json`，退出码 0/非 0 语义、无结果清单时回退末尾输出 + exit code（§3.11）。
- [x] `-ProductName` 作为玩家可见产品名落地为 `-p:Product` + 根启动器名 + `package` 命名；内部 `AssemblyName` 默认稳定为 `PixelEngine.Demo`，流经 `Set-AppHostRelativeAssemblyPath`，audit 启动器命名断言从同一 `ProductName` 派生期望名（不写死 `PixelEngine Demo`），已验证不与带空格产品名冲突（§3.11）。
- [x] AOT 仅宿主 RID（当前 `win-x64/aot`）、R2R crossgen2 当前 Windows 出 `win-x64`（+条件 `win-arm64`），跨架构/非 Windows RID 由 CI/CLI 出（§3.11）。
- [x] `-DevLayout` 分流：开发（含符号）布局走宽松 dev-audit（保 pdb、只查结构存在性 + player-only 断言）；`Release`+无符号走完整 `audit-release-artifacts`（§3.11、§3.7）。

玩家包/编辑器工具包分流与 player-only 审计

- [x] `tools/audit-release-artifacts.*` 新增玩家包断言：`app/` 内出现 `PixelEngine.Editor.dll` 或任意 `ImGuizmo*/ImPlot*` 即 fail；审计**允许**玩家 HUD 所需 `Hexa.NET.ImGui`，撤销早期「拒绝 ImGui」表述（§3.7）。
- [x] 编辑器工具包（`apps/PixelEngine.Editor.Shell`）不走玩家 player-only 审计、与玩家 6-RID 矩阵解耦（§3.7、`plan/19`）。

HTML UI native 与 demo-playability 内容打包

- [x] `plan/20` 的 UI native（RmlUi 主 / Ultralight 可选）作 dynamic-only 落 `runtimes/<rid>/native/`、纳入 `SHA256SUMS` 与包内许可声明、AOT 通道门控排除或动态加载、**不进 Box2D dual-build**；当前 Windows-first 仅 `win-x64` + 纯托管 `ManagedFallbackBackend` 基线变体出包（§3.5、§3.11）。
- [x] `content/weapons.json` 与 `gravel`/`crystal` 等新材质纹理纳入内容包核对，`materials.json`/`reactions.json` 恒含；当前 `weapons.json`、`textureId` 17/18 对应纹理已落盘，`audit-release-artifacts.ps1|.sh` 与 `HostingProjectDisciplineTests.DemoContentDeclaresWeaponsAndResolvableMaterialTextures` 已锁定（守 #8）（§3.9）。
- [x] `build-player` 场景过滤模式（可选）：staging `content/` 生成 `startup.json` + 按 `-IncludeScene` 只拷 `content/scenes/` 被选场景，audit「必含场景」放宽为「必含被声明启动场景文件」；默认整包 content 原样打包不变（§3.9）。

---

## 5. 验收标准

> 本轮 win-first 修订的作用域说明（不改写下方既有 `- [!]`/`- [x]` 条目）：下方原「6 个 RID 全绿」类阻塞项，对**激活集**（win-x64 主 + win-arm64 条件）保持既有校验语义与证据要求；对**保留集**（linux/osx 四 RID）转为「随该 RID 激活时才校验」——即 dormant 期间其阻塞不计入本轮发布门禁，翻 `active:true` 后自动恢复为发布门禁项（linux 动态链 glibc、macOS codesign/notarization、R2R light-up 等阻塞项均按此语义随激活生效）。新增激活集/布局/build-player/player-only 验收见本节末尾追加条目。

- [!] 阻塞：6 个 RID（win-x64/win-arm64/linux-x64/linux-arm64/osx-x64/osx-arm64）的 R2R 通道产物全部构建成功并产出（架构 §15）。本机已验证 `win-x64/r2r`，其余 RID 需 release workflow 或对应目标 runner 产物闭合；`release.yml` 已上传 publish/verify/package/signing evidence artifact，并在 release job 汇总 `evidence.json` 后调用 `tools/release-evidence-preflight.ps1`，缺 manifest 为 `blocked_missing_release_manifest`，schema/JSON 错误为 `blocked_invalid_release_evidence`，缺 RID/channel/signing/hash/upload/artifact-audit scope 为 `blocked_missing_release_scope_evidence`，非 tag `workflow_dispatch` GitHub Release 上传报告为 `blocked_not_tag_release` 且不能当作发布成功证据（由 `PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsNonTagUploadReportAsReleaseSuccess` 锁定），`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsSuccessfulUploadFromNonTagRef` 已锁定即使 upload 写 success，也必须来自 `refs/tags/v<semver>` 且 `release_tag=true`，`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsMismatchedRunIdentity` 已锁定所有 markdown 报告必须与 `workflow_run` 的 `run_id` / `sha` 同源，`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsWrongWorkflowMetadata` 已锁定 workflow-run 报告必须来自 `Release` workflow、`push`/`workflow_dispatch` 事件与有效 `run_attempt`，`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsMissingArtifactAuditReport` 已锁定 require-all artifact audit 必须进入 manifest，证据齐全也仅为 `release_evidence_attached_pending_review`，证据见 `docs/release-reports/2026-07-02-win-x64-publish.md`。
- [!] 阻塞：6 个 RID 的 AOT 通道产物全部构建成功并产出；每个 AOT 产物经 SIMD 探针确认 x64 有 ymm（v4 变体有 zmm）、arm64 有 NEON 指令，**无 SSE2 静默退化**（架构 R3、§12.3）。本机已验证 `win-x64/aot` publish + smoke；当前 release evidence 预检要求 AOT `simdProbeKind` 显式区分 `x64_ymm_zmm` 与 `arm64_neon`，并拒绝把 non-x64 skip 报告冒充 arm64 NEON 证明（由 `PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsSkippedArm64SimdProbe` 锁定）；跨 RID 与每产物 SIMD 探针仍需目标 runner/硬件。
- [!] 阻塞：R2R 产物在目标机不固定 ISA：运行时 light-up 验证显示 sim 热方法在支持 AVX2/AVX-512 的机器上使用对应宽寄存器（Tier-1 重 JIT 生效）（架构 §12.3、§15）。该项需要代表性 AVX2/AVX-512 目标机与 Tier-1 反汇编证据。
- [!] 阻塞：Box2D dual-build 完整：6 RID × {动态, 静态} = 12 件 native 产物齐备；R2R 产物 `runtimes/<rid>/native/` 含动态 Box2D，AOT 产物为含静态 Box2D 的单一可执行（架构 §14.4）。本机仅验证 `win-x64` 动态/静态 build 与双通道打包；`tools/audit-release-artifacts.ps1`/`.sh` 已对 AOT 产物递归拒绝动态 Box2D，避免错误路径漏检。
- [!] 阻塞：OpenAL/ANGLE 在两通道均为动态分发，未被静态链；native 依赖 fan-out 仅 Box2D 一项（不变式 #10、架构 §14.4）。本机 `win-x64` 审计通过；6 RID 双通道仍需 release artifact 审计。
- [!] 阻塞：Linux 两 RID 产物动态链 glibc（`ldd`/`otool` 等价检查无静态 libc），未 `-static` libc（架构 §14.4、§15）。当前 Windows 本机不能验证 Linux 产物动态链。
- [!] 阻塞：macOS arm64（及 x64）产物完成 codesign + notarization + staple，`spctl`/`codesign --verify` 通过（架构 §15）。该项需要 macOS runner 与 Developer ID/notary 凭据。
- [x] 「debug 正常 publish 崩」防线：CI verify 阶段对 R2R（动态）与 AOT（静态）两条路径均跑 smoke/加载校验且全绿；任一路径失败即阻断 release（架构 R5）。
- [x] Trim：所有 `src/` 引擎库开启 trim/AOT 分析器，CI 下 `IL2xxx`/`IL3xxx` 零警告（即零 error）；无 `TrimmerRootDescriptor`，System.Text.Json 全走源生成（架构 §9.1、§16.3）。
- [x] 脚本子系统 trim 豁免生效：R2R 通道保留 Roslyn+ALC 热重载能力，NativeAOT 通道显式禁用并降级。`HotReloadService`/`ScriptLoadContext` 继续用 `RequiresDynamicCode` 标注动态代码边界；Demo 启动路径已通过 `RuntimeFeature.IsDynamicCodeSupported` 门控热重载，AOT / `--smoke` 不会尝试进入 Roslyn/ALC 路径；`DemoStartupOptionsTests.HotReloadRequiresDynamicCodeSupport` 覆盖该策略。该决策保留 NativeAOT 次发行，同时不伪造 AOT 热重载能力。
- [!] 阻塞：版本与命名：所有产物按 `PixelEngine-Demo-<version>-<rid>-<channel>` 命名，`InformationalVersion` 含 git sha，构建确定性可复现（同输入同产物 hash）。本机已验证 `PixelEngine-Demo-0.1.0-win-x64-{r2r,aot}.zip` 命名；`tools/PixelEngine.Tools.DeterministicPackage` 已替代 `Compress-Archive`/`zip`/`tar -czf`，固定 entry 顺序、时间戳、权限与 owner，并由测试证明相同内容不同 metadata 的 zip/tar.gz hash 稳定；`release.yml` 已在 release job 中二次 package 到 `artifacts/package-deterministic` 并生成 `deterministicHashReport`，`tools/release-evidence-preflight.ps1` 已要求该报告除 `conclusion=success` 外还必须列出激活 RID × channel 的 `match` 明细行，`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsDeterministicHashRowMismatch` 锁定 mismatch 不能冒充成功。该项仍需真实 release runner 产物、tag 覆盖与 evidence manifest 人工复核后才能勾选。
- [!] 阻塞：内容打包：每个产物的包根 `content/` 含 materials.json/reactions.json/纹理/音效/默认场景，结构与开发态一致，引擎加载成功（架构 §16.3、§11）。本机 `win-x64` 双通道已由 smoke 与 artifact audit 验证；“每个产物”需 6 RID 全矩阵。
- [x] 玩家友好启动布局：解压后的包根目录保留清晰启动入口、`content/`、`app/` 与少量说明/校验文件，`.dll`、`.deps.json`、`.runtimeconfig.json` 等运行必须依赖位于 `app/` 子目录；`.pdb`、XML 文档、多语言 `*.resources.dll` 卫星程序集与 `createdump(.exe)` 诊断辅助程序不进入玩家包；Windows 根目录 `PixelEngine Demo.exe` 可直接启动，发行审计会拒绝运行时依赖堆在包根目录、`app/content/` 重复内容、`app/PixelEngine.Demo.exe` 重复启动入口，也会拒绝玩家包内调试/文档/诊断辅助/本地化资源噪音。
- [!] 阻塞：`SHA256SUMS` 覆盖全部产物并随 GitHub Release 一并发布。本机已生成覆盖 2 个 `win-x64` 包的 `SHA256SUMS`；PowerShell/Bash 审计已拒绝非发行包名、重复 RID/channel、路径型 checksum、重复/多余/缺失 checksum 条目；`tools/release-evidence-preflight.ps1` 也会解析 manifest 指向的 `SHA256SUMS`，要求激活集全部 package 覆盖且每行 hash 与 `packageSha256` 一致，并要求 `github_release_upload` 报告以 `uploaded_asset_count=packageCount+1` 和逐个 `asset/<name>` hash 证明 GitHub Release 上传了激活集 package 与唯一 `SHA256SUMS`；`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsInvalidSha256SumsContent` 已锁定占位 checksum 文件不能冒充成功证据，`ReleaseEvidencePreflightRejectsUploadReportWithoutAssetCoverage` 已锁定只写 upload success 但缺 asset 清单不能冒充发布完成；完整覆盖仍需 GitHub Release 全产物上传后复核。

win-first 激活门控与布局选型（本轮新增）

- [~] 激活集全绿：`win-x64` r2r/aot 本机全链路（publish + smoke + package + audit）通过；`win-arm64` r2r/aot 构建必过、load-only 校验通过、release notes 标人工硬件门禁（架构 R5、§15、§2.1）。`tools/build-player.*` 已按 `release-rids.json` 的 `smoke=load-only` 将跨架构 verify 显式降级为 load-only，AOT 仅禁止跨 OS 不再误拦同 OS 跨架构；本机 `pwsh tools/build-player.ps1 -Rid win-arm64 -Channel aot ...` 已确认进入真实 native 阶段并因缺 MSVC v143 C++ 工具链失败，需安装工具链/runner 后继续闭合真实产物证据。
- [x] 保留集 dormant 且可一键激活：linux/osx 四 RID 在 `release-rids.json` 为 `active:false`，其 toolchain/脚本/ISA 组/codesign step 保留可编译；把任一 RID 翻 `active:true` 后无需改任何 YAML/脚本逻辑即自动进 native→publish→verify→sign-package→release 全链路、审计/预检期望数量自动 +1 组；本地 dry-run 已验证 `linux-x64` 翻 active 后矩阵扩展生效（§2.1）。
- [x] 数量与激活集挂钩：`SHA256SUMS` 覆盖激活集全部产物（当前 4）；`uploaded_asset_count = packageCount + 1`（当前 5）；audit `--require-all` 与 preflight 期望包数 = `activeRids × channels`，dormant RID 缺失不误判为失败（§2.1、§3.8）。
- [x] 边界不误伤：`ci.yml`/`plan/16` 的 6-RID 构建/测试矩阵与 `ci-matrix-evidence-preflight.ps1` 未随发行门控收敛，dormant RID 仍有编译保证（§2.1）。
- [x] 布局选型锁定：`PublishSingleFile=false` 保持；§3.7.1 记录 (a)/(b) 权衡与否决 (b) 的理由；`artifacts/publish/<rid>-<channel>/` 中间产物不被当作发行物（有 `_PUBLISH_INTERMEDIATE_README.txt` + 固定玩家入口目录）（§3.7.1）。

build-player 入口与玩家包解耦（本轮新增）

- [x] `tools/build-player.*` 单 RID 一键出包：在 Windows 本机对 `win-x64/r2r/Release` 跑通 native→publish→verify→package→audit，逐行 NDJSON（`schema=pixelengine.build/v1`）+ `build-result.json` 产出正确，产物与 `tools/*` 手工出包**同等参数下字节级一致**（§3.11）。
- [x] `-DevLayout` 开发（含符号）构建走宽松 dev-audit（保 pdb、结构存在性 + player-only 断言）；`Release`+无符号走完整 `audit-release-artifacts`，二者布局符合各自规则（§3.11、§3.7）。
- [x] player-only 审计：玩家包 `app/` 内不含 `PixelEngine.Editor.dll` 与任意 `ImGuizmo*/ImPlot*`，审计通过；`Hexa.NET.ImGui` 玩家 HUD 依赖被允许（§3.7）。
- [x] HTML UI native（`plan/20`）dynamic-only 落 `runtimes/<rid>/native/`、纳入 `SHA256SUMS` 与许可声明、不进 Box2D dual-build；当前 Windows-first 仅 `win-x64` + 纯托管基线变体出包（§3.5、§3.11）。

---

## 6. 依赖关系

前置依赖：

- `00-conventions-and-techstack.md`——技术栈/RID/编译模式定档（本文件不另立选型）。
- `01-project-setup.md`——解决方案/项目骨架、`Directory.Build.props`/CPM、Box2D vendored 源与 CMake 编译脚本、`ci.yml` 基线（本文件 `release.yml` 复用其片段、扩展其属性组）。
- `06-physics-collision-rigidbody.md`——`PixelEngine.Interop` 的 Box2D `[LibraryImport]` 绑定与 task 桥（本文件打包其 native 产物，且 AOT 静态链路径依赖其绑定 AOT 友好，架构 §14.2、R14）。

协同依赖：

- `14-testing-benchmarking.md`——提供 Demo `--smoke` 入口与 SIMD 探针/基准，本文件 verify 阶段调用之（架构 R5、§17.3）。
- `11-scripting-system.md`——脚本 trim 豁免与 Roslyn 分发契约（§3.6），打包后脚本热重载验收联动。

下游消费：

- `17-roadmap-execution-order.md` 的 **M9（存档 + 流式 + 打包）** 与 **M10（Demo 整合 + 调优）**——6-RID CI（矩阵保留、当前 Windows 激活子集）、Box2D dual-build、R2R 发行 + NativeAOT 次级、macOS codesign 是 M9 的验收交付物（架构 §18）；本文件 win-first 门控与 build-player 入口挂 **M13「编辑器独立化与发行解耦」**。
- `19-standalone-editor-app.md`——其 BuildSettings 面板经子进程消费本文件 §3.11 的 `build-player` 编排器 + NDJSON/`build-result.json` 契约；**顺序约束**：`build-player`（§3.11）必须先于 `plan/19` BuildSettingsPanel 落地（面板只编排、不重实现打包）。
- `20-interactive-html-ui.md`——其 `PixelEngine.UI` 后端 native 由本文件 §3.5/§3.9 作 dynamic-only 打包、纳入 SHA256SUMS 与许可声明、不进 Box2D dual-build。

前置顺序约束（写入本文件与 `plan/17`）：需求 1 的 **GUI 宿主中性化重构**（新增中性程序集 `PixelEngine.Gui`、`Hosting` 删除对 `PixelEngine.Editor` 的硬 `ProjectReference`、`DemoProgram.cs` 改用 `PixelEngine.Gui` 中性 host）必须**先于**本文件 §3.7 玩家包审计新规则（拒绝 `app/` 含 `PixelEngine.Editor.dll` 与 `ImGuizmo*`/`ImPlot*`，允许 `Hexa.NET.ImGui` 核心）；当前前置已落地，player-only 断言已转绿。

不变式校验（`AGENTS.md §1`）：本文件仅触及编译/链接/打包形态，遵守不变式 #10 修订口径（Box2D 是唯一 sim-native / dual-build 静态承载依赖；UI/音频/渲染 native 为 dynamic-only 门控依赖），不与基石/调度/单缓冲/耦合/帧节奏等任何不变式冲突。技术栈与 `00` 完全一致，无冲突上报。

---

## 7. 提交节点

按 `AGENTS.md §6`，每完成一个节点即用中文 git 提交（`type(scope): 简述`，`scope=build`）：

- [x] `build(build): 落地 R2R 主发行配置与 publish 脚本（6 RID 自包含 + R2R composite，不固定 ISA）` — 对应 §3.3、实现清单「发行配置与脚本」R2R 项。
- [x] `build(build): 落地 NativeAOT 次发行配置（每 RID 显式 IlcInstructionSet，限已知硬件）` — 对应 §3.4、AOT 项。
- [x] `build(build): Box2D dual-build × 6 RID 构建矩阵与 native 资产落位 targets` — 对应 §3.5、Box2D 项。
- [x] `build(build): 引擎全程 trim/AOT-friendly 配置与脚本子系统豁免` — 对应 §3.6、Trim 项。
- [x] `build(build): 版本号、产物命名与内容资产打包脚本` — 对应 §3.8、§3.9、版本/内容项。
- [x] `build(build): macOS codesign + notarization 脚本` — 对应 codesign 项、架构 §15。
- [x] `build(build): 发布 CI release.yml（native→publish→verify→sign→release 五阶段，双路径冒烟）` — 对应 §3.10、CI 项，闭合架构 R5 防线。
- [x] `build(build): RID 激活门控（release-rids.json 单一真相源 + 动态矩阵 + 审计/预检参数化，Windows 优先、跨平台保留非激活）` — 对应 §2.1、§3.10、实现清单「RID 激活门控」项。
- [x] `build(build): 锁定 Unity 式 app/ 发行布局选型（§3.7.1 方案权衡，否决单文件）` — 对应 §3.7.1、实现清单「发行布局选型」项。
- [x] `build(build): tools/build-player 编排器 + NDJSON(pixelengine.build/v1)/build-result.json 契约 + dev-audit 分流` — 对应 §3.11、实现清单「build-player 入口」项，供 plan/19 BuildSettings 面板消费。
- [x] `build(build): 玩家包 player-only 审计（拒 PixelEngine.Editor.dll 与 ImGuizmo/ImPlot、允许玩家 HUD 的 Hexa.NET.ImGui）+ 编辑器工具包分流` — 对应 §3.7、实现清单「玩家包/编辑器工具包分流」项。
- [x] `build(build): HTML UI native dynamic-only 打包与 demo-playability 内容核对（weapons.json/新材质纹理，不进 Box2D dual-build）` — 对应 §3.5、§3.9、实现清单「HTML UI native 与 demo-playability 内容」项。
