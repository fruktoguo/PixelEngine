# Plan 15 — M15 发行、打包与分发证据账本

> 本文件是 M15 的发行证据账本，承载玩家包、Windows-first active set、长期 6-RID 保留矩阵、R2R/AOT、native 打包、签名、公证、GitHub Release 与 build-player 的可追溯状态。
> 产品北极星：Engine Core + Unity-like Editor + Web-first UI Runtime + Showcase Demo Game。技术依据：`../docs/PixelEngine-架构与需求设计.md`、`00-conventions-and-techstack.md`、`../AGENTS.md`。
> 状态标记：只使用 `- [x]`、`- [ ]`、`- [!]`。进行中状态必须拆成已完成子项与未完成或阻塞子项。

---

## 1. 当前产品职责

- [x] 本文件只负责发行、打包、分发和发行证据，不定义 Engine Core、Unity-like Editor、Web-first UI Runtime 或 Showcase Demo Game 的功能实现。
- [x] 本文件负责玩家包发行链路：`demo/PixelEngine.Demo` 或后续 Player app 经 `build-player`、publish、verify、package、audit 产出不含编辑器闭包的玩家包。
- [x] 本文件负责把发行当前激活集与长期 RID 目标分离：Windows-first active set 是当前发布门禁，长期 6-RID 是一等支持全集和可恢复矩阵。
- [x] 本文件负责 R2R 主发行与 NativeAOT 次发行的包形态、ISA 策略、Box2D dual-build 落位和 native 依赖动态分发边界。
- [x] 本文件负责给 Unity-like Editor 的 Build Settings 面板提供唯一一键出包入口 `tools/build-player.ps1` 与 `tools/build-player.sh`，面板只消费 NDJSON 和 `build-result.json`，不重实现管线。
- [x] 本文件负责声明哪些状态不能算验收完成：`pending_review`、`local_probe_only`、`scripted_probe_only`、`process_smoke_only`、`load-only`、`ready`、`counters_present`、非 tag 的 `workflow_dispatch` 上传报告都不是发行完成证据。

---

## 2. 状态总览 checklist

- [!] 文档总状态：M15 发行证据未完成。发行工具链和本机 Windows 路径已有大量工程证据，但最终 release artifact、签名、公证、目标 runner、GitHub Release 与人工复核仍未闭合。
- [x] Windows-first active set 已建模：`tools/release-rids.json` 将 `win-x64`、`win-arm64` 作为当前 active RID，linux 与 macOS 四 RID 作为 dormant 保留矩阵位。
- [x] 长期 6-RID 目标未删除：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64` 的 toolchain、ISA 分组、脚本和矩阵形态保留。
- [!] `win-x64` 本机探针只证明本地路径可运行，不等于 tag release、GitHub Release 上传、外部机器复核或目标硬件闭合。
- [!] `win-arm64` 的 `load-only` 只证明结构与加载校验，不等于 Windows ARM64 真机运行 smoke。
- [!] linux 和 macOS dormant RID 缺发行产物不是当前 Windows-first 发布失败，但它们仍是长期 6-RID 一等支持的未闭合外部证据。
- [!] macOS `codesign`、`notarization`、`stapler`、`spctl` 和 Developer ID 凭据仍是发行阻塞。
- [!] GitHub Release 证据必须来自 tag 触发的 Release workflow；非 tag 的 `workflow_dispatch` 只能用于演练，不能写成发布成功。
- [x] build-player 单 RID 一键出包入口、NDJSON 协议、`build-result.json`、dev-audit 分流和 player-only 审计已落地。
- [x] 玩家包与编辑器工具包分流已落地：玩家包拒绝 `PixelEngine.Editor.dll` 与 ImGuizmo/ImPlot 编辑器闭包，允许玩家 HUD 所需 `Hexa.NET.ImGui`。

---

## 3. 已实现证据 checklist

- [x] 发行模式已锁定：`Directory.Build.props` 定义 R2R 主通道与 NativeAOT 次通道，R2R 不固定 ISA，AOT 通过 RID 显式 `IlcInstructionSet` 避免 SSE2 静默退化。
- [x] 发布脚本已落地：`tools/publish-r2r.ps1`、`tools/publish-r2r.sh`、`tools/publish-aot.ps1`、`tools/publish-aot.sh` 产出 `artifacts/publish/<rid>-<channel>/` 中间产物，并用 README 标明它不是玩家包。
- [x] 玩家友好布局已落地：`tools/package.ps1` 与 `tools/package.sh` 输出包根启动入口、`content/`、`app/`、README、LICENSE、SHA256SUMS，运行时依赖收进 `app/`。
- [x] Unity 式 `app/` 布局已锁定：R2R 保持 `PublishSingleFile=false`，明确否决 (b) 单文件下载即应用根形态；Windows R2R 根启动器经 apphost 相对程序集路径指向 `app/PixelEngine.Demo.dll`，AOT 根启动器直接启动并加载 `app/` native。保留方案 (b) 单文件为已否决备选口径。
- [x] Box2D dual-build 工具链已落地：`tools/build-native.ps1`、`tools/build-native.sh` 与 `native/toolchains/*` 支持 6 RID 的动态和静态产物路径，`native/PixelEngine.Box2D.targets` 区分 R2R 动态库与 AOT 静态链。
- [x] native fan-out 边界已落地：Box2D 是唯一 sim-native / dual-build 静态承载依赖；OpenAL、ANGLE、RmlUi、Ultralight 归 dynamic-only 或系统分发并可门控回退。
- [x] Trim/AOT 纪律已落地：引擎库启用 trim/AOT 分析器，脚本编译子系统对 Roslyn/ALC 做局部豁免，NativeAOT 通道显式禁用热重载实现而不是伪造 AOT 热重载。
- [x] Release workflow 已实现动态矩阵：`.github/workflows/release.yml` 通过 setup job 消费 `tools/release-matrix.ps1` 输出 native/build/expected 矩阵。
- [x] 发行激活真相源已集中：`tools/release-rids.json` 声明 channels、active RID、runner、shell、smoke、codesign；任一 dormant RID 翻 active 后由矩阵脚本扩展。
- [x] 审计脚本已参数化：`tools/audit-release-artifacts.ps1` 与 `.sh` 接收 active RID，`--require-all` 只要求当前激活集，dormant RID 缺失不误判当前 Windows-first 发布失败。
- [x] 发行证据预检已参数化：`tools/release-evidence-preflight.ps1` 与 `.sh` 从 active RID 和 expected package count 派生包数、上传资产数、SHA256SUMS 覆盖和 deterministic hash 行集。
- [x] GitHub Release 上传报告约束已写入预检：必须覆盖 package asset 与唯一 `SHA256SUMS`，只写 success 不能冒充完成。
- [x] 确定性打包工具已落地：`tools/PixelEngine.Tools.DeterministicPackage` 固定 entry 顺序、时间戳、权限与 owner，release job 二次 package 生成 deterministic hash report。
- [x] build-player 编排器已落地：`tools/build-player.ps1` 与 `.sh` 串 `build-native`、publish、verify、package、audit，输出 `schema=pixelengine.build/v1` NDJSON 与 `build-result.json`。
- [x] build-player 产品名契约已落地：`-ProductName` 只影响玩家可见启动器和包名，内部 `AssemblyName` 默认保持 `PixelEngine.Demo`，避免带空格 assembly 破坏 restore 或 apphost 载荷。
- [x] build-player dev-audit 分流已落地：`-DevLayout` 允许保留 pdb/xml，但仍检查结构存在性和 player-only 断言；Release 无符号构建走完整发行审计。
- [x] 内容资产打包已落地：`content/` 是单一真相源，`materials.json`、`reactions.json`、`weapons.json`、场景、纹理、音频进入包根 `content/`，不复制到 `app/content/`。
- [x] UI native 打包边界已登记：Web-first UI Runtime 的 RmlUi 与 Ultralight 若启用，只能 dynamic-only 落 `runtimes/<rid>/native/`，纳入许可和 SHA256SUMS，不进入 Box2D dual-build；当前 Ultralight optional profile inactive，发行审计拒绝未激活的 `Ultralight` / `WebCore` / `AppCore` native 混入。
- [x] 纪律测试已记录：`HostingProjectDisciplineTests.ReleaseRidGateDeclaresWindowsActiveSetAndMatrixOutputs`、`ReleaseRidGateDryRunActivatesDormantRidFromConfigOnly`、`EditorShellBuildTests.PlayerPackageAuditRejectsEditorClosureAllowsImGuiAndSupportsDevLayout`、`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightRejectsNonTagUploadReportAsReleaseSuccess` 等锁定关键边界。

---

## 4. 未完成目标 checklist

- [ ] 生成 tag 触发的 Windows-first GitHub Release，包含当前 active RID × channels 的全部 package 和唯一 `SHA256SUMS`。
- [ ] 为 `win-x64` active set 提供 release workflow 产物、verify、package、artifact audit、upload report、deterministic hash report 与 manifest 同源证据。
- [ ] 为 `win-arm64` active set 提供真实工具链构建和目标硬件运行 smoke；`load-only` 只能作为临时阻塞状态，不能作为最终完成。
- [ ] 为 Linux dormant RID 在重新激活时提供 glibc 动态链检查、native smoke 或 QEMU 策略、R2R/AOT 双通道产物和 SHA256 覆盖。
- [ ] 为 macOS dormant RID 在重新激活时提供 codesign、notarization、staple、`codesign --verify`、`spctl` 与 GitHub Release 产物证据。
- [ ] 为 6-RID 长期目标提供 Box2D dynamic/static 全矩阵证据，证明 R2R 含动态 Box2D、AOT 不携带动态 Box2D。
- [ ] 为所有 active 产物提供 AOT SIMD 探针和 R2R runtime light-up 证据，证明 AOT 未退化、R2R 热方法仍能 Tier-1 重 JIT。
- [ ] 为 Web-first UI Runtime native 后端补许可、体积、AOT 绑定、fallback 和发行 gate 证据，尤其 Ultralight 可选高保真后端；Ultralight optional profile inactive 时必须自动回退 ManagedFallback，且不得把 Ultralight native 混入包当成发行闭合。
- [ ] 如果产品决定将 linux 或 macOS 从 dormant 恢复为 active，需要只改 `tools/release-rids.json`，并跑通全链路而非新写分支脚本。

---

## 5. 证据债 / 阻塞 checklist

- [!] 阻塞：`release_evidence_attached_pending_review` 只代表证据包已附上并待人工复核，不代表发行验收完成。
- [!] 阻塞：`workflow_dispatch` 的 GitHub Release 上传报告不是正式 tag release 证据，非 tag 上传必须保持阻塞。
- [!] 阻塞：本机 `win-x64` publish、smoke、package、audit 只能算本地工程探针，不能替代 release runner、GitHub Release、目标机器和人工复核。
- [!] 阻塞：`load-only` 不能证明目标架构可运行，`win-arm64` 仍需要真机或可信 runner smoke。
- [!] 阻塞：macOS 签名和公证需要 Developer ID 与 notary 凭据，缺凭据时不得出未签名产物冒充完成。
- [!] 阻塞：Linux 动态链 glibc 需要目标 runner 的 `ldd` 或等价检查，Windows 本机不能替代。
- [!] 阻塞：完整 6-RID dual-build 产物需要对应 runner 或交叉编译证据，本机 `win-x64` 不能替代其它 RID。
- [!] 阻塞：SHA256SUMS 必须覆盖全部 active package 且与 GitHub Release 上传 asset hash 一致，局部 checksum 文件不能替代。
- [!] 阻塞：deterministic hash report 必须逐 active RID × channel 给出 match 行，只写 `conclusion=success` 不能通过。
- [!] 阻塞：发行证据 manifest、workflow run 报告、artifact audit、SIMD 探针、signing 报告和 GitHub upload 报告必须同 run_id、sha、workflow、attempt，不允许拼接不同运行。
- [!] 阻塞：Ultralight optional profile inactive 前不得携带 `Ultralight` / `WebCore` / `AppCore` native，也不得把 native 文件、NOTICE 文案或 startup 请求冒充 SDK provenance、commercial redistribution license、codesign/notarize 或 release artifact evidence。

---

## 6. 验证命令与证据路径 checklist

- [x] 本机 Windows-first 探针证据路径：`docs/release-reports/2026-07-02-win-x64-publish.md` 记录 `win-x64` R2R/AOT publish、smoke、package、audit；该路径只作为本地证据，不升级为最终 release 完成。
- [x] 一键出包入口命令：`pwsh tools/build-player.ps1 -Rid win-x64 -Channel r2r -Configuration Release -StartScene scenes/lava-mine.scene`，输出 `build-result.json` 与玩家包归档。
- [x] 玩家包审计命令：`pwsh tools/audit-release-artifacts.ps1 -PublishRoot artifacts/publish/win-x64/r2r -PackageRoot artifacts/package -ActiveRids win-x64 -RequireAll` 或 Bash 等价入口，用于同时审计 publish 与 package 结构、player-only 断言和 inactive Ultralight native 混入门禁。
- [x] 矩阵 dry-run 命令：`pwsh tools/release-matrix.ps1 -Config tools/release-rids.json -IncludeWinArm64 true`，用于验证 active RID 派生 package count 和 asset count。
- [!] 最终 release 预检命令：`pwsh tools/release-evidence-preflight.ps1 -Manifest <release-evidence.json> -ActiveRids <active-rids> -ExpectedPackageCount <n>`，必须在 tag release 证据齐全后才能解除阻塞。
- [!] 最终签名证据路径：macOS `codesign`、`notarytool`、`stapler`、`spctl` 报告仍缺，不得勾选 macOS 完成。
- [!] 最终上传证据路径：GitHub Release upload markdown 必须列出 `uploaded_asset_count=packageCount+1`、全部 package asset hash 和唯一 `SHA256SUMS` hash。
- [!] 最终 SIMD 证据路径：AOT 探针报告必须按 x64/arm64 区分 `simdProbeKind`，非 x64 skip 不能冒充 arm64 NEON 证明。

---

## 7. 依赖与下一闭合节点 checklist

- [x] 上游依赖：`00-conventions-and-techstack.md` 定义 .NET、RID、R2R/AOT、Box2D 和 UI native 门控；本文件不另立技术栈。
- [x] 上游依赖：`01-project-setup.md` 提供 solution、Directory.Build、CPM、CI 基线和 native 构建底座。
- [x] 上游依赖：`06-physics-collision-rigidbody.md` 提供 Box2D 绑定和 task bridge，本文件只打包 native 产物。
- [x] 协同依赖：`14-testing-benchmarking.md` 提供 release evidence preflight、artifact audit 纪律测试、smoke 和 SIMD 探针语义。
- [x] 协同依赖：`16-performance-hardening.md` 提供 R2R light-up、AOT SIMD、目标硬件、硬件计数器和帧预算证据门禁。
- [x] 下游消费：`19-standalone-editor-app.md` 的 Build Settings 面板只消费 `build-player`，不复制打包逻辑。
- [x] 下游消费：`20-interactive-html-ui.md` 的 RmlUi/Ultralight native 由本文件登记为 dynamic-only 发行资产。
- [ ] 下一闭合节点：先跑 tag release dry-run 与 Windows-first active set 产物审计，再补 `win-arm64` 真机 smoke。
- [ ] 下一闭合节点：补 macOS signing/notarization 凭据和 Linux/macOS dormant RID 重新激活演练证据。
- [!] M15 出口阻塞：发行、性能、UI native、目标硬件和人工复核证据未闭合前，plan/15 不能从阻塞改为完成。

---

## 8. 保留设计细节 checklist

- [x] R2R 主通道：CoreCLR 自包含 + ReadyToRun composite + Tiered Compilation + Dynamic PGO，保留运行时 CPU 检测和热方法重 JIT。
- [x] AOT 次通道：NativeAOT per RID，显式 ISA，限已知硬件，必须有 SIMD 探针，不作为广兼容默认下载。
- [x] 发行目录：包根是玩家入口和内容，`app/` 是运行时依赖，publish 目录是中间产物而不是用户下载物。
- [x] 内容策略：默认整包 `content/` 原样打包；build-player 可选场景过滤只裁剪 staging 内的 scenes，并生成 `startup.json`。
- [x] Native 策略：Box2D dynamic/static 双产物随 R2R/AOT 分路；OpenAL、ANGLE、RmlUi、Ultralight 不进静态 dual-build fan-out。
- [x] 审计策略：发行审计以 active RID 派生数量，避免把 dormant RID 缺产物误判为当前 Windows-first 发布失败，也避免把 dormant RID 当已完成。

---

## 9. 提交节点 checklist

- [x] 已完成历史节点：R2R/AOT 配置、Box2D dual-build、trim/AOT、版本与内容打包、macOS 签名脚本、release workflow、RID 激活门控、Unity 式 `app/` 布局、build-player、player-only 审计、HTML UI native dynamic-only 登记。
- [ ] 待完成 M15 节点：`build(release): 闭合 Windows-first release artifact 与 GitHub Release 证据`。
- [ ] 待完成 M15 节点：`build(release): 闭合目标 RID 签名、公证、native leak 与发行审计证据`。
- [!] 阻塞提交不得提前写：任何只基于 pending review、本机探针、load-only 或 workflow dispatch 的提交，都不能写成发行验收完成。
