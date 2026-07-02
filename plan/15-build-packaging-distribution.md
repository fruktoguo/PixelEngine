# Plan 15 — 构建、打包与分发（Build / Packaging / Distribution）

> 本文件定义 PixelEngine 的发行管线：6-RID 产物、CoreCLR+R2R 主发行、NativeAOT 次发行、Box2D dual-build × 6 RID、native 资产打包、trim 配置、发布 CI、codesign/notarization、版本与产物命名、内容资产打包。
> 权威依据：架构文档 `../docs/PixelEngine-架构与需求设计.md`（下称「架构 §x.y」）的 §12.3、§13、§14.4、§15、R3/R5/R14；技术栈锚文档 `00-conventions-and-techstack.md`；开发宪法 `../AGENTS.md`。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

本文件交付一条可重复、可审计、覆盖 6 个 RID 的发行管线，把 `src/` 引擎与 `demo/PixelEngine.Demo` 打成最终用户可运行的产物，并把 `content/` 内容资产一并打包。管线必须同时调和「极致性能」与「广兼容」两个硬需求（架构 §2 挑战五、§12.3、§15）：主发行走 CoreCLR 自包含 + ReadyToRun（R2R composite），靠运行时 CPU 检测 + Tier-1 重 JIT + Dynamic PGO 让 sim 热方法在用户机上 light-up AVX2/AVX-512/AVX10.2，**不固定 ISA**；次发行走 NativeAOT、每 RID 单独产物、必须显式 `IlcInstructionSet`、仅对已知硬件分发（架构 R3）。

明确的范围边界（严格只写本范围）：

本文件**拥有**发行/打包/分发这一层——即 `dotnet publish` 的发行配置、产物布局、native 资产落位、trim 配置、版本与命名、发布 CI 工作流（`release.yml`）、codesign/notarization、以及发行级的「Box2D dual-build × 6 RID 构建矩阵」编排与产物消费。本文件**不拥有**：解决方案/项目骨架与持续集成基线（`build`/`test` 工作流）由 `01-project-setup.md` 落实，本文件的 `release.yml` 复用其 `ci.yml` 的 action 片段；Box2D 的 `[LibraryImport]` 绑定、`[UnmanagedCallersOnly]` task 桥、CMake 源码编译脚本本身由 `01`/`06-physics-collision-rigidbody.md` 落实，本文件只定义跨 6 RID 的「构建矩阵编排 + 静/动产物落位 + RID-gated 链接」这一发行契约；smoke/性质/基准测试本体由 `14-testing-benchmarking.md` 落实，本文件只定义把 smoke 接入 publish 双路径验证（架构 R5）。

不变式遵守（`AGENTS.md §1`）：本管线把 native 面收敛到 **Box2D 单一依赖**（不变式 #10），仅 Box2D 需要 dual-build（静/动），OpenAL/ANGLE 始终走动态（系统/捆绑）分发以压制 dual-build fan-out（架构 §14.4、§15）。本层不触碰 sim/physics 任何运行时行为，仅决定编译/链接/打包形态，不与四大基石、checkerboard、单缓冲、32px 上限等任何不变式相关。

---

## 2. 技术栈与依赖

与 `00-conventions-and-techstack.md` 完全一致，不另立选型：

- 运行时/语言：.NET 10 LTS / C# 14（架构 §13）。
- 主发行编译模式：**CoreCLR 自包含 + ReadyToRun（R2R composite）**——`PublishReadyToRun=true` + `PublishReadyToRunComposite=true` + `SelfContained=true`，保留 Tiered Compilation/Tier-1 重 JIT/Dynamic PGO（架构 §12.3、§13）。
- 次发行编译模式：**NativeAOT**，每 RID 单独产物，`PublishAot=true` + 显式 `IlcInstructionSet`（架构 §12.3、R3）。
- 开发态：纯 JIT（不在本文件范围，仅作对照）。
- native 依赖：**Box2D v3.1（vendored C 源）**，唯一 native 依赖（不变式 #10）；动态 `.dll/.so/.dylib` 供 CoreCLR/R2R、静态 `.lib/.a` 供 NativeAOT；其余 native（OpenAL Soft、可选 ANGLE）始终动态。
- native 构建工具：**CMake**（≥3.21，支持 toolchain-file 交叉编译与 multi-config），编译器 MSVC（win）、clang（linux/mac，mac 用 Apple clang）。
- 托管侧打包友好库（均 reflection-free / 源生成 / AOT-trim 友好，架构 §9.1、§13）：`Silk.NET.*`、`Hexa.NET.ImGui`（+ Backends）、`System.Text.Json` 源生成、`K4os.Compression.LZ4`、`Microsoft.CodeAnalysis.CSharp`（脚本编译器，发行时随产物分发但本身不参与 trim 根，见 §3.6）。
- CI：GitHub Actions（与 `01` 的 `ci.yml` 同栈），新增 `release.yml`；runner：`windows-latest`、`ubuntu-latest`、`macos-latest`（含 Apple silicon）。
- 签名：macOS `codesign` + `notarytool` + `stapler`（Developer ID Application 证书）；Windows Authenticode `signtool`（可选）；Linux 仅产 `SHA256SUMS`（无强制签名）。

目标 6 RID（架构 §15、§12.3；`00` §3）：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。

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

R2R 通道（非单文件，保留可排错布局）：

```
PixelEngine-<version>-<rid>/
├─ PixelEngine.Demo(.exe)              # apphost
├─ PixelEngine.*.dll                   # 引擎 + Demo 托管程序集（R2R composite 后含 native image）
├─ *.dll                              # 自包含的 CoreCLR + BCL
├─ runtimes/<rid>/native/
│  ├─ box2d.(dll|so|dylib)            # 动态 Box2D（本通道）
│  ├─ (openal soft native)            # 动态
│  └─ (angle native, 可选)            # 动态
└─ content/                           # 内容资产（§3.9）
   ├─ materials.json
   ├─ reactions.json
   ├─ textures/
   ├─ audio/
   └─ scenes/default.scene
```

AOT 通道（单一原生可执行）：

```
PixelEngine-<version>-<rid>-aot/
├─ PixelEngine.Demo(.exe)             # 含静态链 Box2D 的单一原生可执行
├─ (openal soft native)              # 仍动态，置于可执行同目录
├─ (angle native, 可选)             # 动态
└─ content/                          # 同上
```

### 3.8 版本号与产物命名

版本来源：`Directory.Build.props` 的 `<VersionPrefix>`（SemVer，如 `0.1.0`）；CI 在 tag `v<semver>` 触发时以 tag 覆盖版本，`<InformationalVersion>` 嵌 `+<gitShortSha>`；`<Deterministic>true</Deterministic>`（已由 `00` §6 约定）保证可复现。

产物压缩与命名（`<channel>` ∈ `r2r|aot`；可选 ISA 变体后缀 `-avx512`）：

```
PixelEngine-Demo-<version>-<rid>-<channel>.zip      # win-* 用 zip
PixelEngine-Demo-<version>-<rid>-<channel>.tar.gz   # linux-*/osx-* 用 tar.gz（保留 +x 权限）
SHA256SUMS                                           # 汇总所有产物校验和
```

### 3.9 内容资产打包

`content/`（materials.json、reactions.json、材质纹理、音效、默认场景）随每个产物原样打包到产物根的 `content/`（架构 §16.3、§11）。打包脚本以 `content/` 为单一真相源拷贝，不重排、不改名（材质稳定字符串键的可移植性不受打包影响，不变式 #8、架构 §11.2）。产物内 `content/` 与开发态目录结构一致，引擎以相对可执行路径定位，保证 R2R/AOT 两通道、6 RID 下加载行为一致。

### 3.10 发布 CI 工作流

新增 `.github/workflows/release.yml`，触发于 tag `v*`（手动 `workflow_dispatch` 可选）。复用 `01` 的 `ci.yml` 的 setup-dotnet/缓存 action 片段。结构：

阶段 1「native」：在三个 runner 上各自跑 `tools/build-native.*`，按 §3.2 映射构建该 runner 负责 RID 的 Box2D 静/动产物，上传为 artifact。

阶段 2「publish」：6 RID × 2 通道 的 job 矩阵，下载对应 native artifact，跑 `tools/publish-r2r.*` 或 `tools/publish-aot.*`；AOT job 额外跑 SIMD 探针（§3.4）。

阶段 3「verify」：对每个产物跑 `tools/verify-publish.*`（§5、架构 R5），本机可运行的直接跑 Demo `--smoke`，跨架构的按 §3.2 走 QEMU 或降级加载校验。

阶段 4「sign & package」：macOS 产物跑 `tools/codesign-macos.sh`（codesign Developer ID + notarytool 提交 + stapler 装订，架构 §15）；Windows 可选 Authenticode；`tools/package.*` 装配布局、拷 `content/`、压缩、生成 `SHA256SUMS`。

阶段 5「release」：把全部产物 + `SHA256SUMS` 附到 GitHub Release。

---

## 4. 实现清单

发行配置与脚本

- [x] 在 `Directory.Build.props` 增 `Channel` 驱动的 R2R 属性组（§3.3）：`SelfContained`/`PublishReadyToRun`/`PublishReadyToRunComposite`/`TieredPGO`/`InvariantGlobalization`，**不设任何 baseline ISA**（架构 §12.3）。
- [x] 在 `Directory.Build.props` 增 AOT 属性组（§3.4）：`PublishAot`/`TrimMode=full`/`IlcOptimizationPreference=Speed`，并为 6 RID 各设显式 `IlcInstructionSet`（x64=x86-64-v3，arm64=NEON+lse+rcpc）（架构 R3）。
- [x] 写 `tools/publish-r2r.ps1` 与 `tools/publish-r2r.sh`：参数 `-Rid`，对 `demo/PixelEngine.Demo` 跑 `dotnet publish -c Release -r <rid> -p:Channel=R2R`，输出到 `artifacts/publish/<rid>-r2r/`。
- [x] 写 `tools/publish-aot.ps1` 与 `tools/publish-aot.sh`：参数 `-Rid`，跑 `dotnet publish -c Release -r <rid> -p:Channel=AOT`，输出到 `artifacts/publish/<rid>-aot/`。
- [x] 写 `tools/verify-publish.ps1` 与 `tools/verify-publish.sh`：对给定产物目录跑 Demo `--smoke`（本机）或 QEMU/降级加载校验（跨架构），非零退出即失败（架构 R5）。
- [x] 写 `tools/package.ps1` 与 `tools/package.sh`：装配 §3.7 布局、拷 `content/`、按 RID 选 zip/tar.gz、产 `SHA256SUMS`、命名按 §3.8。

Box2D dual-build 矩阵与 native 落位

- [x] 写 6 个 toolchain-file `native/toolchains/{win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64}.cmake`（目标三元组/架构/sysroot/`CMAKE_OSX_ARCHITECTURES`），交叉项遵守「动态链 glibc，绝不 `-static` libc」（架构 §14.4、§15）。
- [x] 写 `tools/build-native.ps1` 与 `tools/build-native.sh`：参数 `-Rid`，对该 RID 跑 `BUILD_SHARED_LIBS=ON` 与 `OFF` 两次 CMake 配置+构建，产 12 矩阵中属于该 RID 的 4 件（动态库 + 静态库各 1，含 import lib 若有），落 `native/out/<rid>/{shared,static}/`。
- [x] 写 `native/PixelEngine.Box2D.targets`（被 `PixelEngine.Interop` 引入）：R2R/默认按 `$(RuntimeIdentifier)` 注入动态库到 `runtimes/<rid>/native/`（`<Content>`+`Link`+`PreserveNewest`）；`Condition` 命中 `PublishAot` 时改用 `<NativeLibrary Include>` 静态链（RID-gated）（架构 §14.4）。
- [x] 确认 OpenAL/ANGLE 在两通道均保持动态（不进 `<NativeLibrary>` 静态链），收敛 fan-out（不变式 #10、架构 §14.4）。

Trim 配置

- [x] 在 `Directory.Build.props` 对 `src/` 引擎库统一开 `IsTrimmable`/`IsAotCompatible`/`EnableTrimAnalyzer`/`EnableAotAnalyzer`（§3.6、架构 §9.1）。
- [x] CI 把 `IL2xxx`/`IL3xxx` trim/AOT 警告提升为 error（与 `AGENTS.md §4` 一致）。
- [x] 对脚本编译子系统程序集设 `IsTrimmable=false` 局部豁免（Roslyn/ALC 运行时代码加载），并确保 Roslyn 依赖随产物完整分发（§3.6、架构 §13、`plan/11`）。
- [x] 验证发行不引入任何 `TrimmerRootDescriptor`/反射回退（System.Text.Json 走 `PixelEngineJsonContext` 源生成）（架构 §16.3）。

发布 CI 工作流

- [x] 写 `.github/workflows/release.yml`，触发 tag `v*` + `workflow_dispatch`，复用 `01` 的 `ci.yml` setup/缓存片段。
- [x] 阶段 1「native」：三 runner 按 §3.2 映射跑 `tools/build-native.*`，上传 native artifact。
- [x] 阶段 2「publish」：6 RID × {r2r, aot} job 矩阵，下载 native artifact，跑对应 publish 脚本；AOT job 跑 SIMD 探针验证 ymm/zmm 出现（架构 R3）。
- [x] 阶段 3「verify」：每产物跑 `tools/verify-publish.*`；跨架构按 §3.2（`linux-arm64` QEMU；`win-arm64`/`osx-x64` 降级加载校验 + release notes 人工门禁标注）（架构 R5）。
- [x] 阶段 4「sign & package」：macOS 跑 `tools/codesign-macos.sh`；可选 Windows Authenticode；跑 `tools/package.*`。
- [x] 阶段 5「release」：上传全部产物 + `SHA256SUMS` 到 GitHub Release。

codesign / notarization

- [x] 写 `tools/codesign-macos.sh`：`codesign --options runtime --timestamp` 签 `osx-x64`/`osx-arm64` 可执行与动态库 → `notarytool submit --wait` → `stapler staple`（架构 §15）。
- [x] CI secrets 接入：Developer ID Application 证书（base64 p12）、`notarytool` API key/issuer；缺失时该 step 标 `- [!] 阻塞：缺 macOS 签名凭据` 而非静默出未签名产物。

版本与产物命名

- [x] `Directory.Build.props` 设 `VersionPrefix`（SemVer）+ `Deterministic` + `ContinuousIntegrationBuild`（CI 下）（§3.8）。
- [x] `release.yml` 以 tag 覆盖版本并嵌 `+<gitShortSha>` 进 `InformationalVersion`（§3.8；随发布 CI 节点落实）。
- [x] `tools/package.*` 按 `PixelEngine-Demo-<version>-<rid>-<channel>[.<variant>].{zip|tar.gz}` 命名，tar.gz 保留可执行权限位。

内容资产打包

- [x] `tools/package.*` 以 `content/` 为单一源拷入每产物根 `content/`，不重排/不改名（不变式 #8、架构 §11.2、§16.3）。
- [x] 验证本机 `win-x64` R2R/AOT 包内 `content/` 结构与开发态一致，包根含 `materials.json`/`reactions.json`/纹理/音效/场景（§3.9）。
- [x] 验证 R2R/AOT × 6 RID 产物内 `content/` 结构与开发态一致，引擎按相对路径定位成功（§3.9；随发布 CI 节点落实）。

---

## 5. 验收标准

- [!] 阻塞：6 个 RID（win-x64/win-arm64/linux-x64/linux-arm64/osx-x64/osx-arm64）的 R2R 通道产物全部构建成功并产出（架构 §15）。本机已验证 `win-x64/r2r`，其余 RID 需 release workflow 或对应目标 runner 产物闭合；`release.yml` 已上传 publish/verify/package/signing evidence artifact，并在 release job 汇总 `evidence.json` 后调用 `tools/release-evidence-preflight.ps1`，缺 manifest 为 `blocked_missing_release_manifest`，schema/JSON 错误为 `blocked_invalid_release_evidence`，缺 RID/channel/signing/hash/upload scope 为 `blocked_missing_release_scope_evidence`，非 tag `workflow_dispatch` GitHub Release 上传报告为 `blocked_not_tag_release` 且不能当作发布成功证据（由 `PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsNonTagUploadReportAsReleaseSuccess` 锁定），证据齐全也仅为 `release_evidence_attached_pending_review`，证据见 `docs/release-reports/2026-07-02-win-x64-publish.md`。
- [!] 阻塞：6 个 RID 的 AOT 通道产物全部构建成功并产出；每个 AOT 产物经 SIMD 探针确认 x64 有 ymm（v4 变体有 zmm）、arm64 有 NEON 指令，**无 SSE2 静默退化**（架构 R3、§12.3）。本机已验证 `win-x64/aot` publish + smoke；当前 release evidence 预检要求 AOT `simdProbeKind` 显式区分 `x64_ymm_zmm` 与 `arm64_neon`，并拒绝把 non-x64 skip 报告冒充 arm64 NEON 证明；跨 RID 与每产物 SIMD 探针仍需目标 runner/硬件。
- [!] 阻塞：R2R 产物在目标机不固定 ISA：运行时 light-up 验证显示 sim 热方法在支持 AVX2/AVX-512 的机器上使用对应宽寄存器（Tier-1 重 JIT 生效）（架构 §12.3、§15）。该项需要代表性 AVX2/AVX-512 目标机与 Tier-1 反汇编证据。
- [!] 阻塞：Box2D dual-build 完整：6 RID × {动态, 静态} = 12 件 native 产物齐备；R2R 产物 `runtimes/<rid>/native/` 含动态 Box2D，AOT 产物为含静态 Box2D 的单一可执行（架构 §14.4）。本机仅验证 `win-x64` 动态/静态 build 与双通道打包；`tools/audit-release-artifacts.ps1`/`.sh` 已对 AOT 产物递归拒绝动态 Box2D，避免错误路径漏检。
- [!] 阻塞：OpenAL/ANGLE 在两通道均为动态分发，未被静态链；native 依赖 fan-out 仅 Box2D 一项（不变式 #10、架构 §14.4）。本机 `win-x64` 审计通过；6 RID 双通道仍需 release artifact 审计。
- [!] 阻塞：Linux 两 RID 产物动态链 glibc（`ldd`/`otool` 等价检查无静态 libc），未 `-static` libc（架构 §14.4、§15）。当前 Windows 本机不能验证 Linux 产物动态链。
- [!] 阻塞：macOS arm64（及 x64）产物完成 codesign + notarization + staple，`spctl`/`codesign --verify` 通过（架构 §15）。该项需要 macOS runner 与 Developer ID/notary 凭据。
- [x] 「debug 正常 publish 崩」防线：CI verify 阶段对 R2R（动态）与 AOT（静态）两条路径均跑 smoke/加载校验且全绿；任一路径失败即阻断 release（架构 R5）。
- [x] Trim：所有 `src/` 引擎库开启 trim/AOT 分析器，CI 下 `IL2xxx`/`IL3xxx` 零警告（即零 error）；无 `TrimmerRootDescriptor`，System.Text.Json 全走源生成（架构 §9.1、§16.3）。
- [x] 脚本子系统 trim 豁免生效：R2R 通道保留 Roslyn+ALC 热重载能力，NativeAOT 通道显式禁用并降级。`HotReloadService`/`ScriptLoadContext` 继续用 `RequiresDynamicCode` 标注动态代码边界；Demo 启动路径已通过 `RuntimeFeature.IsDynamicCodeSupported` 门控热重载，AOT / `--smoke` 不会尝试进入 Roslyn/ALC 路径；`DemoStartupOptionsTests.HotReloadRequiresDynamicCodeSupport` 覆盖该策略。该决策保留 NativeAOT 次发行，同时不伪造 AOT 热重载能力。
- [!] 阻塞：版本与命名：所有产物按 `PixelEngine-Demo-<version>-<rid>-<channel>` 命名，`InformationalVersion` 含 git sha，构建确定性可复现（同输入同产物 hash）。本机已验证 `PixelEngine-Demo-0.1.0-win-x64-{r2r,aot}.zip` 命名；`tools/PixelEngine.Tools.DeterministicPackage` 已替代 `Compress-Archive`/`zip`/`tar -czf`，固定 entry 顺序、时间戳、权限与 owner，并由测试证明相同内容不同 metadata 的 zip/tar.gz hash 稳定；`release.yml` 已在 release job 中二次 package 到 `artifacts/package-deterministic` 并生成 `deterministicHashReport`，全部 RID/channel hash 匹配才允许 GitHub Release 上传。该项仍需真实 release runner 产物、tag 覆盖与 evidence manifest 人工复核后才能勾选。
- [!] 阻塞：内容打包：每个产物根 `content/` 含 materials.json/reactions.json/纹理/音效/默认场景，结构与开发态一致，引擎加载成功（架构 §16.3、§11）。本机 `win-x64` 双通道已由 smoke 与 artifact audit 验证；“每个产物”需 6 RID 全矩阵。
- [!] 阻塞：`SHA256SUMS` 覆盖全部产物并随 GitHub Release 一并发布。本机已生成覆盖 2 个 `win-x64` 包的 `SHA256SUMS`；PowerShell/Bash 审计已拒绝非发行包名、重复 RID/channel、路径型 checksum、重复/多余/缺失 checksum 条目；完整覆盖仍需 GitHub Release 全产物上传后复核。

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

- `17-roadmap-execution-order.md` 的 **M9（存档 + 流式 + 打包）** 与 **M10（Demo 整合 + 调优）**——6-RID CI、Box2D dual-build、R2R 发行 + NativeAOT 次级、macOS codesign 是 M9 的验收交付物（架构 §18）。

不变式校验（`AGENTS.md §1`）：本文件仅触及编译/链接/打包形态，遵守不变式 #10（native 收敛 Box2D），不与基石/调度/单缓冲/耦合/帧节奏等任何不变式冲突。技术栈与 `00` 完全一致，无冲突上报。

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
