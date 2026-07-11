# PixelEngine Windows-first 1.0 目标硬件与 Runner 清单

机器可读真相是 schema v2 的 [`tools/target-hardware-matrix.json`](../tools/target-hardware-matrix.json)，一致性校验命令是 `pwsh tools/validate-target-hardware-matrix.ps1`。该清单消费 [`tools/release-rids.json`](../tools/release-rids.json)，不复制或改写 RID 激活真相。v2 明确区分普通 build/test runner 与需要真实交互桌面、WGL 和 ANGLE 的 native GPU smoke runner，二者证据不得互相冒充。

## 快照身份

| 项目 | 值 |
|---|---|
| 清单 commit | `796b57817b9136340b70ee0594fe743f1f094d97` |
| Windows-first required RID | `win-x64` |
| Windows-first conditional RID | `win-arm64` |
| 长期兼容矩阵 | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` |
| 配置 / SDK | `Release` / `.NET 10.0.x` |
| 本机观测 | Windows 11 专业版 build 26100; AMD Ryzen 7 5800X; AMD Radeon RX 7900 XT; driver `32.0.31021.5001`; .NET SDK `10.0.108` |

## RID 矩阵

| RID | 产品状态 | Build/test runner | Native GPU runner | CPU/GPU/OS/driver | 权限与外部条件 | Smoke / benchmark |
|---|---|---|---|---|---|---|
| `win-x64` | required / active | `windows-latest` / `pwsh`；仅 build、test、benchmark、verify-publish | `external_required`；`[self-hosted, Windows, X64, pixelengine-wgl-angle, pixelengine-native-smoke]`；当前未注册 | Ryzen/Radeon 是本机 `observed_local`，不是 `windows-latest` 身份；真实 job 必须重新捕获 CPU/GPU/driver/OS | ETW counters 需管理员；native GPU workflow 只允许受信任 exact SHA 的 `workflow_dispatch`；当前 codesign=false | hosted native build/solution test/R2R verify；专用 runner 执行 Desktop GL 3.3 + ANGLE/GLES 3.0 native smoke；BenchmarkDotNet MediumRun |
| `win-arm64` | conditional / active build-only | `windows-latest` / `pwsh`；x64 host → arm64 target | 不在当前 win-x64 GPU smoke scope | CPU/GPU/OS/driver 标为 `EXTERNAL_REQUIRED`，需 ARM64 设备或可信 runner 回填 | ETW 需目标设备权限；codesign=false；load-only 不能解除条件 | cross/native build + load-only 仅为入口；最终需 ARM64 包真实启动、进入 `lava-mine`、退出 |
| `linux-x64` | dormant / long-term | `ubuntu-latest` / `bash` | 不在当前 Windows-first GPU smoke scope | runner `lscpu`/`lspci`/`uname`/glibc 在 run 中记录 | dormant 不阻塞 1.0；激活时补 glibc/native smoke | native build、solution test、R2R verify；BenchmarkDotNet 在匹配 runner 运行 |
| `linux-arm64` | dormant / long-term | `ubuntu-24.04-arm` / `bash` | 不在当前 Windows-first GPU smoke scope | ARM64 runner 身份在 run 中记录；QEMU 只用于 smoke | CI smoke 可 `sudo` 安装 qemu；QEMU 不能冒充目标性能硬件 | native/QEMU smoke、solution test、R2R verify；native ARM64 benchmark 需同架构硬件 |
| `osx-x64` | dormant / long-term | `macos-15-intel` / `bash` | 不在当前 Windows-first GPU smoke scope | `sw_vers`、`sysctl`、`system_profiler` 在 run 中记录 | 激活时需要 Developer ID、notarization、staple、spctl；当前无凭据 | native build、solution test、R2R verify；签名与发行证据另行门控 |
| `osx-arm64` | dormant / long-term | `macos-14` / `bash` | 不在当前 Windows-first GPU smoke scope | Apple Silicon CPU/GPU/OS/driver 在 run 中记录 | 激活时需要 Developer ID、notarization、staple、spctl；当前无凭据 | native build、solution test、R2R verify；签名与发行证据另行门控 |

## win-x64 runner 能力边界

- `windows-latest` 是普通 CI 的 build/test runner，`graphicsNativeSmokeEligible=false`。普通 CI 全绿只证明其声明的 build、test、benchmark 与 verify-publish scope，不声称运行过 WGL/ANGLE。
- `.github/workflows/native-gpu-smoke.yml` 是独立的真实 GPU workflow，只接受 required 40 位 `candidate_sha` 的 `workflow_dispatch`。它不会由 `push`、`pull_request` 或 `pull_request_target` 自动执行，也不通过条件跳过伪造成功。
- 专用 runner 必须同时匹配 `self-hosted`、`Windows`、`X64`、`pixelengine-wgl-angle` 与 `pixelengine-native-smoke`，并通过 `tools/native-gpu-runner-preflight.ps1`。该 preflight 要求 Windows x64、非 Session 0 的交互桌面、真实非 Basic/Remote/Virtual GPU、有效 driver、.NET 10 和同源 Actions/SHA identity。
- preflight 的 fixture seam 仅供跨平台自动测试，生产 workflow 不传 `FixturePath`/`AllowFixture`；真实 GPU/WGL/ANGLE 结果只能由匹配 runner 运行产生。

## 功能支持矩阵

该表补充 RID 硬件清单的产品能力声明；机器可读的 RID 激活真相仍以 `tools/target-hardware-matrix.json` 为准。

| 功能 | `win-x64` | `win-arm64` | 其他 RID | 当前状态与解除条件 |
|---|---|---|---|---|
| GPU air/smoke 非权威视觉 pass | 未启用 | 未启用 | 未启用 | `deferred_not_enabled`；当前只有独立 pass/资源/shader 契约测试，待生产 `RenderPipeline`/`Hosting` 补齐 seed、dispatch、density 合成、回退和窗口 smoke |

## 证据纪律

- 每次真实 benchmark、CI、smoke 或发行运行必须记录 RID、runner label/image、CPU、GPU、driver、OS、`.NET`、`gitCommit` 和 `benchmarkRunId`；未知硬件只能写 `EXTERNAL_REQUIRED`，不能用 `windows-latest` 代替硬件身份。
- native GPU smoke 还必须记录 `run_id`、`run_attempt`、dispatch SHA、实际 checkout SHA、SessionId、交互状态，以及逐项目 TRX 的 passed/failed/skipped/not-executed。`pixelengine.native-smoke/v2` 的 `graphicsContext` 必须给出 Desktop GL vendor/renderer/version（>= 3.3、`isGles=false`）与真实 ANGLE/GLES identity（>= 3.0、`isGles=true`、`isAngle=true`）；任何缺项都不能升级为真实 GPU 成功证据。
- workflow 最终对 `artifacts/native-gpu-smoke` 下除 `SHA256SUMS` 自身外的所有文件按 ordinal 相对路径排序，生成小写 SHA-256 清单；preflight、TRX、log、summary 和 workflow context 因而可同源审计。
- `win-arm64` 当前在 CI 中是 `build_only=true`，`load-only` 只能证明产物结构/加载入口，不能证明 ARM64 真机运行。
- `linux-arm64` 的 QEMU smoke、本机虚拟显示适配器、`ready`/`counters_present` 和任何短 probe 都不能升级为目标硬件性能证据。
- ETW Cache Misses/Branch Mispredictions 需要 Windows elevated session；macOS signing/notarization 需要外部 Developer ID 与 notary 凭据。本清单只固定条件，不声称这些外部证据已完成。

校验结果：`Target hardware matrix valid: 6 RIDs; active=win-x64,win-arm64; conditional=win-arm64; observed_local=win-x64.`
