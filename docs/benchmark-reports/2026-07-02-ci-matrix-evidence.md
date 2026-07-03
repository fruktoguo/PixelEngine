# 2026-07-02 CI 矩阵证据预检入口

## 目的

`tools/ci-matrix-evidence-preflight.ps1` 用于校验 plan/14 的 6-RID build/test 与 CI publish verify 运行证据。它只检查 GitHub Actions 产出的 evidence manifest 是否完整并记录 SHA256，不把本地运行或不完整 manifest 当作 CI 全绿证明。

## 命令

无 manifest 时只生成阻塞报告：

```pwsh
./tools/ci-matrix-evidence-preflight.ps1 -AllowBlocked
```

CI 汇总 job 会下载 `ci-evidence-*` artifacts，生成 `artifacts/ci-matrix-evidence/evidence.json`，再运行：

```pwsh
./tools/ci-matrix-evidence-preflight.ps1 -EvidenceManifestPath artifacts/ci-matrix-evidence/evidence.json -AllowBlocked
```

## 状态语义

`blocked_missing_ci_manifest` 表示缺少 CI evidence manifest。`blocked_invalid_ci_evidence` 表示 manifest JSON 无法解析或 `schemaVersion` 不为 1。`blocked_missing_ci_scope_evidence` 表示 manifest 存在且 schema 有效，但缺少 6-RID build-test、benchmark guard 或 publish verify 的某个必要证据，文件 SHA256 不匹配，或 evidence markdown 中的 `conclusion`、`rid`、`runner`、`tests_ran`、`build_only`、`channels`、`workflow`、`event`、`run_id`、`run_attempt`、`sha` 等关键字段与 manifest/矩阵语义不一致。`ci_matrix_evidence_attached_pending_review` 表示证据文件、SHA256、基础 markdown 字段、同一个 GitHub Actions run / commit 身份、CI push/pull_request workflow 元数据和预期 hosted runner 约束已齐全，但仍需人工确认对应 GitHub Actions run 的 job 结论确实闭合 plan/14 的 6-RID build/test 与 R2R/AOT verify 语义。

## 必需证据

manifest 使用 `schemaVersion: 1`。`buildTest` 必须覆盖 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`；其中 `win-arm64` 当前仍是 build-only，不能伪装成真实 arm64 测试。`benchmarkGuard` 必须包含反汇编守门和基准回归门禁 job 报告，并声明 `runner: windows-latest`。`verifyPublish` 必须覆盖 `win-x64`、`linux-x64`、`osx-x64`、`osx-arm64`，并由 `tools/verify-publish.ps1` 验证默认 R2R/AOT 双通道。所有 report 节点必须带 `sha256` 和预期 `runner`，`workflowRunReport` 对应 `workflowRunSha256`；预检会重新计算并比对，并拒绝 `conclusion != success`、workflow 不是 `CI`、event 不是 `push`/`pull_request`、`run_attempt` 无效、`ref` 不属于分支/PR、RID 不匹配、`runner` 不匹配、`tests_ran` / `build_only` 不符合矩阵、verify report 未声明 `channels: r2r,aot`，以及 benchmark/build/verify 报告的 `run_id` 或 `sha` 与 `workflowRunReport` 不一致的证据。
