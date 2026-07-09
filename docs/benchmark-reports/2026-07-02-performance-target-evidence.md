# 2026-07-02 目标性能证据预检入口

## 目的

`tools/performance-target-evidence-preflight.ps1` 用于校验 plan/16 仍阻塞的目标硬件性能证据。它只读取 evidence manifest，先检查 AVX-512 降频净损、6 RID cells/frame、帧预算、硬件计数器四类证据的 scope 与 SHA256，再解析每份报告中的机器可读字段，拒绝空报告或只声明 scope/hash 的弱证据；它不运行本机短样本，也不把本机 win-x64 短 BenchmarkDotNet 报告当作目标硬件验收通过。

## 命令

无 manifest 时只生成阻塞报告：

```pwsh
./tools/performance-target-evidence-preflight.ps1 -AllowBlocked
```

带 manifest 时校验证据文件存在、hash 匹配，且报告包含必要机器可读字段：

```pwsh
./tools/performance-target-evidence-preflight.ps1 -EvidenceManifestPath artifacts/performance-target-evidence/evidence.json -AllowBlocked
```

## 状态语义

`blocked_missing_target_performance_manifest` 表示缺少目标性能 evidence manifest。`blocked_invalid_target_performance_evidence` 表示 manifest JSON 无法解析、`schemaVersion` 不为 1，或缺少顶层 `benchmarkRunId/gitCommit` 同源字段。`blocked_missing_target_performance_scope_evidence` 表示 manifest 存在且 schema 有效，但缺少必要 scope、文件不存在、SHA256 不匹配、6 RID cells/frame 没有逐 RID 标记 `benchmarkDotNet=true`，或 evidence 报告缺少/未满足必要机器可读字段、BenchmarkDotNet 报告特征与 `benchmarkRunId/gitCommit` 同源约束。`target_performance_evidence_attached_pending_review` 表示所有必需证据文件都存在、hash 匹配且机器可读字段满足最低阈值，但仍需人工确认原始报告、硬件环境、采样过程与统计结论确实证明 plan/16 阻塞项。

## Manifest 结构

manifest 使用 `schemaVersion: 1`，顶层必须包含同一次目标性能采样的 `benchmarkRunId` 与 `gitCommit`。`evidence[]` 每项必须包含 `scope`、`path`、`sha256`，脚本会重新计算文件 SHA256 并比对。允许且必需的 scope 为 `avx512_downclock_net_loss`、`hardware_counters_cache_branch`、`frame_budget_target_hardware`，以及 `cells_frame/win-x64`、`cells_frame/win-arm64`、`cells_frame/linux-x64`、`cells_frame/linux-arm64`、`cells_frame/osx-x64`、`cells_frame/osx-arm64`；未知 scope 会被拒绝。`cellsFrame` 对象必须覆盖同样六个 RID，且每个 RID 都必须标记 `benchmarkDotNet: true`。

## Evidence 报告机器可读字段

每个 evidence 文件可以使用 `key: value`、`key=value` 或 Markdown 表格 `| Key | Value |` 形式写入字段。每个文件都必须声明与 manifest 顶层完全一致的 `benchmarkRunId` 与 `gitCommit`，避免把不同目标硬件 run 或不同提交的报告拼成一份伪证据。预检只做最低语义门禁，不替代人工复核原始 BenchmarkDotNet、ETW、帧时间图或硬件环境。

- `avx512_downclock_net_loss` 必须包含 `targetCpuName`、`dotnetVersion`，且 `benchmarkDotNet=true`、`vector512HardwareAccelerated=true`、`avx512Enabled=true`、`noNetDownclockLoss=true`。
- `hardware_counters_cache_branch` 必须包含 `benchmarkDotNet=true`、`elevatedEtwKernelSession=true`、`cacheMissesPresent=true`、`branchMispredictionsPresent=true`，并且报告文本包含 `Cache Misses` 与 `Branch Mispredictions` 列名。
- `frame_budget_target_hardware` 必须包含 `targetHardware`、`source=PixelEngineDiagnostics`、`scenario`（`lava_mine_typical` / `streaming_long_march` / `full_active_liquid_stress`）、`demoScene=lava-mine`、`sampleSeconds>=60`、`frameSamples>=3600`、`fixedTickNoCatchUp=true`、`playerPackageRun=true`、`realWindowRun=true`、`degradationPolicyObserved=true`、`frameTimelineCaptured=true`、`caP99Ms<=8`、`renderP99Ms<=4`、`physicsP99Ms<=4`、`logicAudioP99Ms<=1`。
- 每个 `cells_frame/<rid>` 必须包含 `rid=<rid>`、`benchmarkDotNet=true`、`representativeHardware=true`、`activeCellsPerFrame>=2000000`、`caFrameMs<=8`、`measuredIterations>=3`、`iterationCount>=measuredIterations`，且报告正文必须包含 `BenchmarkDotNet v`、`CellThroughputBenchmark.StepJobSystem` 与 `FullActiveLiquid`，不能只附手写 key-value 摘要。

最小示例：

```json
{
  "schemaVersion": 1,
  "benchmarkRunId": "run-20260704-performance-001",
  "gitCommit": "abcdef123456",
  "cellsFrame": {
    "win-x64": { "benchmarkDotNet": true },
    "win-arm64": { "benchmarkDotNet": true },
    "linux-x64": { "benchmarkDotNet": true },
    "linux-arm64": { "benchmarkDotNet": true },
    "osx-x64": { "benchmarkDotNet": true },
    "osx-arm64": { "benchmarkDotNet": true }
  },
  "evidence": [
    { "scope": "avx512_downclock_net_loss", "path": "artifacts/performance-target-evidence/avx512.md", "sha256": "<sha256>" },
    { "scope": "hardware_counters_cache_branch", "path": "artifacts/performance-target-evidence/hardware-counters.md", "sha256": "<sha256>" },
    { "scope": "frame_budget_target_hardware", "path": "artifacts/performance-target-evidence/frame-budget.md", "sha256": "<sha256>" },
    { "scope": "cells_frame/win-x64", "path": "artifacts/performance-target-evidence/cells-frame-win-x64.md", "sha256": "<sha256>" },
    { "scope": "cells_frame/win-arm64", "path": "artifacts/performance-target-evidence/cells-frame-win-arm64.md", "sha256": "<sha256>" },
    { "scope": "cells_frame/linux-x64", "path": "artifacts/performance-target-evidence/cells-frame-linux-x64.md", "sha256": "<sha256>" },
    { "scope": "cells_frame/linux-arm64", "path": "artifacts/performance-target-evidence/cells-frame-linux-arm64.md", "sha256": "<sha256>" },
    { "scope": "cells_frame/osx-x64", "path": "artifacts/performance-target-evidence/cells-frame-osx-x64.md", "sha256": "<sha256>" },
    { "scope": "cells_frame/osx-arm64", "path": "artifacts/performance-target-evidence/cells-frame-osx-arm64.md", "sha256": "<sha256>" }
  ]
}
```

`avx512.md` 最小字段示例：

```md
targetCpuName: Example AVX-512 CPU
benchmarkRunId: run-20260704-performance-001
gitCommit: abcdef123456
dotnetVersion: 10.0.8
benchmarkDotNet: true
vector512HardwareAccelerated: true
avx512Enabled: true
noNetDownclockLoss: true
```

`hardware-counters.md` 最小字段示例：

```md
benchmarkDotNet: true
benchmarkRunId: run-20260704-performance-001
gitCommit: abcdef123456
elevatedEtwKernelSession: true
cacheMissesPresent: true
branchMispredictionsPresent: true

| Method | Cache Misses | Branch Mispredictions |
|---|---:|---:|
| ReactionLookupBenchmark.FindDirect | 100 | 12 |
```

`frame-budget.md` 最小字段示例：

```md
targetHardware: representative-target
benchmarkRunId: run-20260704-performance-001
gitCommit: abcdef123456
source: PixelEngineDiagnostics
scenario: lava_mine_typical
demoScene: lava-mine
sampleSeconds: 120
frameSamples: 7200
fixedTickNoCatchUp: true
playerPackageRun: true
realWindowRun: true
degradationPolicyObserved: true
frameTimelineCaptured: true
caP99Ms: 7.5
renderP99Ms: 3.5
physicsP99Ms: 3.5
logicAudioP99Ms: 0.8
```

`cells-frame-<rid>.md` 最小字段示例：

```md
rid: linux-x64
benchmarkRunId: run-20260704-performance-001
gitCommit: abcdef123456
benchmarkDotNet: true
representativeHardware: true
activeCellsPerFrame: 2500000
caFrameMs: 7.2
measuredIterations: 5
iterationCount: 5

// BenchmarkDotNet v0.15.8
// Benchmark: PixelEngine.Benchmarks.CellThroughputBenchmark.StepJobSystem
// Scenario: FullActiveLiquid
```
