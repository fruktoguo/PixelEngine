# 2026-07-02 Demo 真实窗口人工验收预检入口

## 目的

`tools/demo-manual-acceptance-preflight.ps1` 用于收集 plan/13 剩余真实窗口人工验收证据。它不会把脚本化窗口短跑、截图、录屏或报告自动判为通过；即使 evidence manifest 齐全，也只输出 `manual_evidence_attached_pending_review`，等待人工确认视觉、听感、手感、完整路线和热重载体验。

## 命令

仅生成清单和阻塞报告：

```pwsh
./tools/demo-manual-acceptance-preflight.ps1 -AllowBlocked
```

运行部分现有机器 probe 并生成辅助证据：

```pwsh
./tools/demo-manual-acceptance-preflight.ps1 -RunScriptedProbes -AllowBlocked
```

附加人工 evidence manifest：

```pwsh
./tools/demo-manual-acceptance-preflight.ps1 -EvidenceManifestPath artifacts/demo-manual-acceptance/evidence.json -AllowBlocked
```

## 状态语义

`blocked_missing_manual_evidence` 表示尚未提供人工 evidence manifest。`scripted_probe_only` 表示只跑了 `--scripted-window-demo` 机器 probe，不能替代人工验收。`blocked_missing_manual_scope_evidence` 表示 manifest 缺少必须 scope。`manual_evidence_attached_pending_review` 表示所有必须 scope 都有文件与 SHA256 记录，但仍需人工复核证据是否真的覆盖 plan/13 的 `[!]` 项。

## 必须 scope

manifest 使用 `schemaVersion: 1`，`evidence` 数组至少包含这些 scope：`controlFeelReport`、`materialBrushAndReactionVideo`、`rigidBodyGameplayVideo`、`particleLightingVideo`、`audioListeningReport`、`fullRoutePlaythroughVideo`、`hudMenuEditorVideo`、`hotReloadWindowReport`。

这些 scope 对应 plan/13 剩余阻塞：真实输入手感、真实鼠标/滚轮/数字键操作与 CA 视觉接管、刚体可推/可砸/可继续破坏、粒子与 bloom/fog 视觉质量、音频听感与空间感、完整路线通关、HUD/菜单/Editor 交互、开发态热重载体验。
