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

该入口会运行默认可玩程序化场景、主场景、route-attempt、通关、生命、相机、反应/温度、音频、粒子/光照九个 scripted window probe，并要求各自输出关键摘要 marker，例如 `player_visual=present`、`playable_shots=`、`fps=`、`frame_p99_ms=`、`frame_low1_fps=`、`720x480`、`brush_material=`、`pause_open=False`、`goal_reached=True`（通关专项 probe）、`damage_events=`、`camera_followed=True`、`reactions_observed=True`、`audio_probe_one_shot_played=True` 与 `particle_light_probe_depleted=True`。每个 probe 还会传入 `--capture-frame <probe>/capture.bmp`，并在报告中记录 `capture.bmp` 的 path、sha256、byte size、width、height、bits-per-pixel 与 `capture_unique_visible_pixels`；没有写出有效 BMP，或截图为全透明、黑/白空白、近似纯色画面，会直接失败。其中默认可玩程序化场景 probe 覆盖首屏玩家可见、相机跟随、固定内部渲染分辨率、按住左键发射破坏弹、长窗口帧率诊断与自由粒子进入真实窗口链路；route-attempt 使用 `--scripted-window-route` 在默认关卡中执行较长脚本化路线，只要求输出 `goal_reached=` 字段与长跑状态，用来捕捉移动、碰撞、破坏重建与触发器同步问题；它们仍不能替代 `fullRoutePlaythroughVideo` 人工完整路线证据。这些 marker 与截图只证明真实窗口相位链路中的机器探针仍可复现，不证明视觉质量、听感、手感或真实玩家体验通过。

附加人工 evidence manifest：

```pwsh
./tools/demo-manual-acceptance-preflight.ps1 -EvidenceManifestPath artifacts/demo-manual-acceptance/evidence.json -AllowBlocked
```

## 状态语义

`blocked_missing_manual_evidence` 表示尚未提供人工 evidence manifest。`scripted_probe_only` 表示只跑了 `--scripted-window-demo` / `--scripted-window-route` 机器 probe，不能替代人工验收。`blocked_missing_manual_scope_evidence` 表示 manifest 缺少必须 scope。`blocked_invalid_manual_evidence` 表示 schema、未知 scope、元数据、缺 checklist/criteria、缺文件、视频时长不足、视频结构或实际 duration 无法校验、sha256 不匹配等清单错误，脚本会写出报告并以 5 退出。`manual_evidence_attached_pending_review` 表示所有必须 scope 都有文件且 manifest 声明的 SHA256 与实际文件匹配，但仍需人工复核证据是否真的覆盖 plan/13 的 `[!]` 项。

## 必须 scope

manifest 使用 `schemaVersion: 1`，`evidence` 数组只能包含这些 scope：`controlFeelReport`、`materialBrushAndReactionVideo`、`rigidBodyGameplayVideo`、`particleLightingVideo`、`audioListeningReport`、`fullRoutePlaythroughVideo`、`hudMenuEditorVideo`、`hotReloadWindowReport`。每个 entry 必须声明 `path`、`sha256`、`kind`、`reviewer`、`capturedAt`、`notes` 与 `checklist`，脚本会重新计算文件 SHA256 并比对；视频 scope 还必须声明 `durationSeconds`，其中完整通关路线至少 30 秒，其它视频至少 10 秒。视频文件不再只做容器头 sniff：`.mp4/.mov` 必须能解析出 `ftyp`、`moov`、视频 track、正 duration 和非空 `mdat`，实际 duration 不能短于 scope 要求且 manifest 不得虚报超过实际时长；`.mkv/.webm` 必须能通过 `ffprobe` 确认 video stream 与 duration，否则拒绝进入待审。缺失、未知 scope、缺 checklist、时长不足、只有 `ftyp`/EBML 头、视频结构无效或 hash 不匹配都不能进入待审状态。

这些 scope 对应 plan/13 剩余阻塞：真实输入手感、真实鼠标/滚轮/数字键操作与 CA 视觉接管、刚体可推/可砸/可继续破坏、粒子与 bloom/fog 视觉质量、音频听感与空间感、完整路线通关、HUD/菜单/Editor 交互、开发态热重载体验。

## Checklist 字段

`checklist` 是每个 scope 的机器可读覆盖清单，所有 key 都必须为 `true`。`criteria` 必须使用同一组 key，并为每个 key 写明至少 20 个字符的人工判定标准。它们不代表验收自动通过，只用于拒绝没有明确覆盖体验点或判定标准的泛泛 notes/录屏。

- `controlFeelReport`: `runJumpWallKick`、`sandPileTraversal`、`rigidOwnedStanding`
- `materialBrushAndReactionVideo`: `realMouseWheelDigits`、`sandWaterOilGasObserved`、`reactionTemperatureObserved`
- `rigidBodyGameplayVideo`: `pushAndImpact`、`digBridgeCollapse`、`continuedDamage`
- `particleLightingVideo`: `particlesVisible`、`bloomFogLighting`、`noParticleLeak`
- `audioListeningReport`: `materialImpacts`、`ambientAndReaction`、`spatialMix`
- `fullRoutePlaythroughVideo`: `routeCompleted`、`materialsReactionsBodiesShown`、`audioLightingHudShown`
- `hudMenuEditorVideo`: `hudReadable`、`menuButtonsClicked`、`editorDockspaceOpened`
- `hotReloadWindowReport`: `behaviourSourceEdited`、`alcReloadObserved`、`statePreserved`

最小 entry 结构示例：

```json
{
  "scope": "controlFeelReport",
  "kind": "report",
  "path": "artifacts/demo-manual-acceptance/control-feel.md",
  "sha256": "<sha256>",
  "reviewer": "reviewer-name",
  "capturedAt": "2026-07-03T00:00:00Z",
  "notes": "真实设备操作后的观察结论与残余风险说明。",
  "checklist": {
    "runJumpWallKick": true,
    "sandPileTraversal": true,
    "rigidOwnedStanding": true
  },
  "criteria": {
    "runJumpWallKick": "说明真实键盘输入下跑跳蹬墙可控且没有卡死。",
    "sandPileTraversal": "说明玩家在 settled 沙堆斜面上可移动且不会陷入。",
    "rigidOwnedStanding": "说明玩家站在 RigidOwned 刚体像素上不穿透。"
  }
}
```
