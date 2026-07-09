# PixelEngine 稳定 Evidence Index

本索引登记可长期保留的证据报告。机器可读真相是 [`evidence-index.json`](evidence-index.json)，可用 `pwsh tools/validate-evidence-index.ps1` 重算每个报告的 SHA256 并检查路径、任务 ID、Git commit 和 run/session 身份。

`artifacts/`、`BenchmarkDotNet.Artifacts/`、`scratch/`、`publish/` 与 `最终输出/` 都是可再生或会被清理的输出目录，不能作为唯一证据。索引中的 `commit` 是报告文件最后一次写入的 Git commit；`runSessionId` 只有在原始报告确实记录时才填写。历史报告没有 run identity 时明确标为 `not_recorded_in_source_report`，不会被补造为完成证据。

| entry | task IDs | 状态 | commit | run/session | 报告 | SHA256 |
|---|---|---|---|---|---|---|
| `completed-baseline-capabilities` | `BASE-001`–`BASE-018` | `baseline_coverage_only` | `5af1541f` | 未记录 | [completed baseline](../plan/tasks/10-completed-baseline.md) | `7fff9692…d7874c` |
| `scope-decisions-20260710` | `SCOPE-001`–`SCOPE-006` | `decision_record` | `5af1541f` | 未记录 | [scope decisions](../plan/tasks/20-scope-decisions.md) | `96519945…e47d1f` |
| `target-hardware-matrix-20260710` | `EVID-003` | `complete_inventory_control_plane` | `796b5781` | `local-20260710-evid003-target-matrix` | [target hardware validation](evidence-2026-07-10-target-hardware-matrix.md) | `5bf8793d…1fd5bc` |
| `task-catalog-20260710` | `PLAN-001`, `EVID-001` | `complete` | `5af1541f` | `local-20260710-evid001-task-catalog` | [task catalog validation](evidence-2026-07-10-task-catalog-validation.md) | `6d6b7b26…d71f5f42` |
| `plan14-short-20260702` | `BASE-016`, `PERF-001`, `PERF-003`, `PERF-011` | `historical_calibration` | `eb8895f8` | 未记录 | [plan14 short](benchmark-reports/2026-07-02-plan14-short.md) | `1d09196c…e9496` |
| `jobsystem-parallelrange-20260703` | `BASE-016`, `PERF-004` | `historical_short_benchmark` | `eb8895f8` | 未记录 | [JobSystem allocation](benchmark-reports/2026-07-03-jobsystem-parallelrange-zero-allocation.md) | `045ea7e7…a4052` |
| `gc-mode-20260702` | `BASE-016`, `PERF-001`, `PERF-011` | `historical_short_benchmark` | `b7d01c85` | 未记录 | [GC mode](benchmark-reports/2026-07-02-gc-mode.md) | `1988ed77…38856` |
| `latency-branch-calibration-20260702` | `BASE-016`, `PERF-001`, `PERF-009` | `preflight_or_short_calibration` | `eb8895f8` | 未记录 | [latency/branch](benchmark-reports/2026-07-02-latency-branch-calibration.md) | `250cfff4…b8f6f8` |
| `ci-matrix-preflight-20260702` | `CI-003` | `preflight_contract_only` | `85b770a3` | 未记录 | [CI matrix](benchmark-reports/2026-07-02-ci-matrix-evidence.md) | `3c206f61…8a465` |
| `performance-target-preflight-20260702` | `PERF-008`–`PERF-012` | `preflight_contract_only` | `655d4204` | 未记录 | [target performance](benchmark-reports/2026-07-02-performance-target-evidence.md) | `193737fc…f07544` |
| `win-x64-publish-20260702` | `BASE-017`, `REL-001`, `REL-005` | `local_validation_only` | `fbcafc7c` | 未记录 | [win-x64 publish](release-reports/2026-07-02-win-x64-publish.md) | `014bbfc5…80843` |
| `demo-window-longrun-20260702` | `BASE-010`, `BASE-015`, `EVID-002`, `PERF-008` | `process_smoke_only` | `2b5e6d5d` | 未记录 | [demo longrun](runtime-reports/2026-07-02-demo-window-longrun.md) | `995b1c29…f270d2` |
| `demo-window-smoke-20260702` | `BASE-010`, `BASE-013`, `BASE-015` | `local_window_smoke` | `7313d82b` | 未记录 | [demo smoke](runtime-reports/2026-07-02-demo-window-smoke.md) | `ffc03334…ec444` |
| `demo-scripted-window-20260702` | `BASE-010`, `BASE-015`, `DEMO-001`–`DEMO-005` | `scripted_probe_only` | `99b886bf` | 未记录 | [scripted window](runtime-reports/2026-07-02-demo-scripted-window.md) | `f4b94515…0ad75c` |
| `demo-manual-acceptance-preflight-20260702` | `DEMO-001`–`DEMO-005`, `EDITOR-003`, `UI-001` | `preflight_contract_only` | `7aa5b06e` | 未记录 | [manual acceptance preflight](runtime-reports/2026-07-02-demo-manual-acceptance.md) | `a43063c2…68327d` |
| `particle-frame-probe-20260702` | `BASE-005`, `PERF-010` | `local_probe_only` | `7e3eb033` | 未记录 | [particle frame probe](runtime-reports/2026-07-02-particle-frame-probe.md) | `bf95ecc4…91036` |
| `performance-hud-window-20260704` | `BASE-008`, `BASE-010`, `PERF-001`, `PERF-008`, `PERF-010` | `local_window_sample` | `34d341e5` | `hud-slice2-dynamic` | [HUD window](runtime-reports/2026-07-04-performance-hud-steady-window-samples.md) | `49884c1c…80527` |
| `editor-shell-attach-probe-20260706` | `BASE-013`, `EDITOR-003` | `scripted_probe_only` | `d009c234` | 未记录 | [EditorShell attach](runtime-reports/2026-07-06-editor-shell-attach-probe.md) | `f8182250…acf21d` |

完整硬件、命令、报告路径、SHA256、状态和备注以 JSON 为准；此表使用短 hash 仅便于人工浏览。当前索引中的历史条目保留“可追溯但未完成”的状态，不能据此把 `CI-*`、`PERF-*`、`DEMO-*`、`EDITOR-*` 或 `UI-*` 的阻塞任务改成 `[x]`。
