# 2026-07-02 目标性能证据预检入口

## 目的

`tools/performance-target-evidence-preflight.ps1` 用于校验 plan/16 仍阻塞的目标硬件性能证据。它只读取 evidence manifest，检查 AVX-512 降频净损、6 RID cells/frame、帧预算、硬件计数器四类证据的 scope 与 SHA256；它不运行本机短样本，也不把本机 win-x64 短 BenchmarkDotNet 报告当作目标硬件验收通过。

## 命令

无 manifest 时只生成阻塞报告：

```pwsh
./tools/performance-target-evidence-preflight.ps1 -AllowBlocked
```

带 manifest 时校验证据文件存在且 hash 匹配：

```pwsh
./tools/performance-target-evidence-preflight.ps1 -EvidenceManifestPath artifacts/performance-target-evidence/evidence.json -AllowBlocked
```

## 状态语义

`blocked_missing_target_performance_manifest` 表示缺少目标性能 evidence manifest。`blocked_invalid_target_performance_evidence` 表示 manifest JSON 无法解析或 `schemaVersion` 不为 1。`blocked_missing_target_performance_scope_evidence` 表示 manifest 存在且 schema 有效，但缺少必要 scope、文件不存在、SHA256 不匹配，或 6 RID cells/frame 没有逐 RID 标记 `benchmarkDotNet=true`。`target_performance_evidence_attached_pending_review` 表示所有必需证据文件都存在且 hash 匹配，但仍需人工确认报告内容确实证明 AVX-512 无降频净损、6 RID cells/frame 达标、帧预算达标，并且硬件计数器报告包含 Cache Misses 与 Branch Mispredictions。

## Manifest 结构

manifest 使用 `schemaVersion: 1`。`evidence[]` 每项必须包含 `scope`、`path`、`sha256`，脚本会重新计算文件 SHA256 并比对。允许且必需的 scope 为 `avx512_downclock_net_loss`、`hardware_counters_cache_branch`、`frame_budget_target_hardware`，以及 `cells_frame/win-x64`、`cells_frame/win-arm64`、`cells_frame/linux-x64`、`cells_frame/linux-arm64`、`cells_frame/osx-x64`、`cells_frame/osx-arm64`；未知 scope 会被拒绝。`cellsFrame` 对象必须覆盖同样六个 RID，且每个 RID 都必须标记 `benchmarkDotNet: true`。

最小示例：

```json
{
  "schemaVersion": 1,
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
