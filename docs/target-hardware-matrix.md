# PixelEngine Windows-first 1.0 目标硬件与 Runner 清单

机器可读真相是 [`tools/target-hardware-matrix.json`](../tools/target-hardware-matrix.json)，一致性校验命令是 `pwsh tools/validate-target-hardware-matrix.ps1`。该清单消费 [`tools/release-rids.json`](../tools/release-rids.json)，不复制或改写 RID 激活真相。

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

| RID | 产品状态 | Runner | CPU/GPU/OS/driver | 权限与外部条件 | Smoke / benchmark |
|---|---|---|---|---|---|
| `win-x64` | required / active | `windows-latest` / `pwsh` | 本机已观测；每次 runner run 仍须记录 `ImageVersion` 与硬件身份 | ETW counters 需管理员；当前 codesign=false；CI run 必须同 commit | native build、solution test、R2R verify、3600-tick window；BenchmarkDotNet `CellThroughputBenchmark.StepJobSystem` MediumRun |
| `win-arm64` | conditional / active build-only | `windows-latest` / `pwsh`；x64 host → arm64 target | CPU/GPU/OS/driver 标为 `EXTERNAL_REQUIRED`，需 ARM64 设备或可信 runner 回填 | ETW 需目标设备权限；codesign=false；load-only 不能解除条件 | cross/native build + load-only 仅为入口；最终需 ARM64 包真实启动、进入 `lava-mine`、退出 |
| `linux-x64` | dormant / long-term | `ubuntu-latest` / `bash` | runner `lscpu`/`lspci`/`uname`/glibc 在 run 中记录 | dormant 不阻塞 1.0；激活时补 glibc/native smoke | native build、solution test、R2R verify；BenchmarkDotNet 在匹配 runner 运行 |
| `linux-arm64` | dormant / long-term | `ubuntu-24.04-arm` / `bash` | ARM64 runner 身份在 run 中记录；QEMU 只用于 smoke | CI smoke 可 `sudo` 安装 qemu；QEMU 不能冒充目标性能硬件 | native/QEMU smoke、solution test、R2R verify；native ARM64 benchmark 需同架构硬件 |
| `osx-x64` | dormant / long-term | `macos-15-intel` / `bash` | `sw_vers`、`sysctl`、`system_profiler` 在 run 中记录 | 激活时需要 Developer ID、notarization、staple、spctl；当前无凭据 | native build、solution test、R2R verify；签名与发行证据另行门控 |
| `osx-arm64` | dormant / long-term | `macos-14` / `bash` | Apple Silicon CPU/GPU/OS/driver 在 run 中记录 | 激活时需要 Developer ID、notarization、staple、spctl；当前无凭据 | native build、solution test、R2R verify；签名与发行证据另行门控 |

## 功能支持矩阵

该表补充 RID 硬件清单的产品能力声明；机器可读的 RID 激活真相仍以 `tools/target-hardware-matrix.json` 为准。

| 功能 | `win-x64` | `win-arm64` | 其他 RID | 当前状态与解除条件 |
|---|---|---|---|---|
| GPU air/smoke 非权威视觉 pass | 未启用 | 未启用 | 未启用 | `deferred_not_enabled`；当前只有独立 pass/资源/shader 契约测试，待生产 `RenderPipeline`/`Hosting` 补齐 seed、dispatch、density 合成、回退和窗口 smoke |

## 证据纪律

- 每次真实 benchmark、CI、smoke 或发行运行必须记录 RID、runner label/image、CPU、GPU、driver、OS、`.NET`、`gitCommit` 和 `benchmarkRunId`；未知硬件只能写 `EXTERNAL_REQUIRED`，不能用 `windows-latest` 代替硬件身份。
- `win-arm64` 当前在 CI 中是 `build_only=true`，`load-only` 只能证明产物结构/加载入口，不能证明 ARM64 真机运行。
- `linux-arm64` 的 QEMU smoke、本机虚拟显示适配器、`ready`/`counters_present` 和任何短 probe 都不能升级为目标硬件性能证据。
- ETW Cache Misses/Branch Mispredictions 需要 Windows elevated session；macOS signing/notarization 需要外部 Developer ID 与 notary 凭据。本清单只固定条件，不声称这些外部证据已完成。

校验结果：`Target hardware matrix valid: 6 RIDs; active=win-x64,win-arm64; conditional=win-arm64; observed_local=win-x64.`
