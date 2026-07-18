# 2026-07-19 EDITOR-003 Unity 对标循环 14：Scene / Game 当前世界统一

taskIds: `EDITOR-003`
implementationCommit: `0fdd1e143c05e0785dbe0a476f5db531af703ed2`
runInstanceId: `5710fa5a22bc450181128f59e7dec6eb`
runClientInstanceId: `codex-scene-game-0fdd1e14`
playSessionId: `eed44a93c1ba461ab8db05094df2c48c`
evidenceState: `local_scene_game_current_world_complete_external_dpi_drag_review_blocked`

## 结论

本轮闭合了用户反馈的本地可复现差异：Scene View 与 Game View 不再各自持有互不失效的世界。Edit 中两者读取同一 authoring world，Game View 使用游戏相机生成不带 runtime UI、且不受尚未 reveal 的 runtime fog 遮黑的创作预览；Play/Paused 中两者读取同一活动 runtime world，Scene View 保留独立相机、grid、marker、selection、W/E/R gizmo 与 Brush，Game View 保留游戏相机、lighting/fog、letterbox 和最终 UI。共享的是世界状态与 revision，不是两块视图的相机、输入或叠层。

运行中从 Scene View 对 runtime entity 和 runtime world 的修改会立即进入 Game View；临时 Play 的 Stop 会恢复 world 与对象状态，不污染 authoring scene。本轮没有改变 CPU sim 权威、cell 数据布局、checkerboard、dirty rectangle 或 Box2D 边界。

`EDITOR-003` 仍保持 `[!]`：不同物理 DPI/200% 显示器跨屏、Explorer→Editor 人工 pointer drag、runtime 数值物理拖拽与独立 reviewer 仍需外部条件。本地统一切片完成不替代这些证据。

## 实现契约

- `EditorGameViewContract` 明确 `WorldSource`：Edit 为 `AuthoringWorld`，Play/Paused 为 `RuntimeWorld`；Scene 与 Game 保持各自 camera/presentation ownership。
- `RenderPhaseDriver` 新增显式 world-content invalidation。Edit presentation 只在提交边界忽略未揭示 fog mask，不修改权威 fog/lighting；Play/Paused 恢复真实 fog。
- `SceneWorldTexture` 在 provider 重建、runtime step、Brush 写入与 Stop 恢复时按 revision 失效；`RenderBufferBuilder` 可清除 empty-world cache，避免“强制构建”仍复用旧黑帧。
- Scene View 在 Play/Paused 投影 runtime chunks、script entity 与 rigid body marker；selection 优先选择 runtime entity，W/E/R gizmo 通过既有 runtime editor data source 写临时 Transform，Brush 写当前 runtime world。
- Hierarchy 的 runtime 行显示 authoring 投影名称与实时 Transform。Stop 复用临时 Play snapshot 恢复对象和 cell，authoring scene 不标 dirty。
- automation 的 `hierarchy.selection.set/get` 在 Edit 返回 `stableId`，在 Play/Paused 返回 `entityId`/`bodyId`；stable ID 可通过 runtime projection 安全映射，不依赖显示名或列表索引。`runtime.entity.transform.set` 的 UI closure 同时登记 Inspector 与 `panel.scene.gizmo`。

## 提交同源真实 Editor 验收

Release Editor 从 implementation commit 的最终二进制启动，使用隔离的 discovery、artifact、import、user-data 与 log 目录：

`artifacts/editor003-unified-world-commit-0fdd1e14/`

CLI 经 `discover`、`ping` 和 `capabilities --matrix` 连接实例 `5710fa5a22bc450181128f59e7dec6eb`，独立验证 172 个 capabilities 与 329 个 UI commands：

| 身份 | SHA256 |
|---|---|
| capability digest | `71646e3c403d0856441e59135b2219581162f88d395e9a6b5977bfcac7c0b321` |
| UI command digest | `100edff1c3002fb7c797f7284365377c8fc29626409d49ced1bdf1b4b10d0f95` |
| matrix digest | `fe1c43b84f518b6fd2380acee0c6d8af9baa773fc3df22dfa6a971ba7eebab54` |

所有截图均由 `scene.capture` / `game.capture --verify-artifact` 生成，server/local byte length 与 SHA256 双重验证通过。

| 阶段 | artifact | 尺寸 | 结果 | SHA256 |
|---|---|---:|---|---|
| Edit Scene | `de996ec6941244b4b1261cb0a2207f34` | 652×590 | 完整 authoring terrain、marker、grid 与边界可见 | `3e3fb4ce83b959e7316c672a0b7626a8ab106887bf8952e3fa5459677b517772` |
| Edit Game | `9fc078e533f748e49c53cd8f933952b3` | 1280×720 | 游戏相机构图显示完整材质，不是黑帧或 emissive-only；无 runtime UI/fog | `048659294b87f5a29c422a477c8d34313aa8d0f72d4d1c5846635ff46a584a99` |
| Paused Scene，移动后 | `e739b7713ec449128c21bf9cca8cf419` | 652×590 | runtime entity/body marker 可见；Goal selection/gizmo 位于新 Transform | `0b19aed6ba7e817b351bfdc07eb80b210bfd86680ff6ad87095b8b6168a22ebc` |
| Paused Game，画笔前 | `c27f1efcac624287b074a76d28ff9766` | 1280×720 | runtime lighting/fog/UI 正常 | `76482857c07f112ee2b0b9f16b298b95359b00ff3278c19ca4fed1649c4e9541` |
| Paused Scene，画笔后 | `cab8f0b775f44731b25bad6b4266ff6a` | 652×590 | Brush overlay 显示 9×9 / 81 cells，世界出现像素洞 | `80cf555cc93459003bc581d087f8f1c843c811035c07b1cc011d9d5eac332761` |
| Paused Game，画笔后 | `9c6b03536f184c2f851d52ccc0ed40bc` | 1280×720 | 同一位置同步出现 9×9 像素洞，hash 与画笔前不同 | `80b593bede84dd36c9ae9bc95212afd261a070e79e4b7c6ade83898bd1238993` |
| Stop 后 Edit Game | `cd406ee6fc9d4ddbb6511fcf0637ed23` | 1280×720 | authoring world 恢复且仍非黑屏 | `aebe99b01778b67438928a66295aab9dfb3a7684e59c4809aeb04d726d74378d` |

### 运行态编辑与回滚

1. Edit 初态 `workspace.get` 为 `mode=Edit`、`sceneDirty=false`；`runtime.cell.inspect(100,300)` 为 `materialId=2 / dirt`。
2. 以 `TemporarySnapshot` 进入 Play 并暂停。一次过期 global revision 被服务端正确拒绝为 `revision_conflict`；重读 `play.get` 后以最新 revision 重试成功，没有强制覆盖。
3. `hierarchy.selection.set(stableId=3)` 返回 `play:eed44a93c1ba461ab8db05094df2c48c:entity:3`。`runtime.entity.transform.set` 将 Goal 从 `(570,208,0,1,1)` 改为 `(520,180,0.35,1.4,1.2)`，Scene artifact 显示移动、旋转和缩放后的 marker/gizmo。
4. Paused 下将 Scene 工具切到 `Brush / Dig / Square / radius 4`，在 `(100,300)` 应用画笔，返回 `writtenCells=81`、未驻留/越界均为 `0`；cell 变为 `materialId=0 / empty`。Scene/Game 的 artifact SHA256 同时变化。
5. `play.stop` 返回“已恢复临时 Play 快照：tick=0, chunks=192”。随后 `workspace.get` 为 `mode=Edit`、`sceneDirty=false`，cell 恢复为 `dirt`；`inspector.get(stableId=3)` 返回 Goal local/world Transform `(570,208,0,1,1)`。
6. 最后通过公共 `workspace.exit` 关闭实例；PID `102512` 已退出，没有直接终止进程或读取 credential 内容。

## 构建、测试与门禁

硬件/运行时：Microsoft Windows build 26100，AMD Ryzen 7 5800X，AMD Radeon RX 7900 XT driver `32.0.31021.5001`，.NET SDK `10.0.108`，win-x64。

| 验证 | 最终结果 |
|---|---|
| `pwsh tools/run-tests.ps1 -Configuration Release -NoRestore` 的 solution build | 0 warnings / 0 errors |
| 14 个 test projects | 2305 passed / 48 个显式环境 smoke skipped / 0 failed |
| `PixelEngine.Hosting.Tests` 最终全量 | 965 passed / 7 skipped / 0 failed |
| `PixelEngine.Editor.Automation.Matrix --check` | snapshot 一致；172 capabilities / 329 UI commands |
| `tools/validate-task-catalog.ps1` | 通过；82 canonical tasks，51 done / 4 open / 0 active / 27 blocked |
| `git diff --check` | 通过（仅既有 CRLF→LF 提示，无 whitespace error） |

首次全量 Hosting 运行准确发现两个本轮契约同步缺口：旧 discipline assertion 仍要求 Brush 只在 Edit 可用；`DrawRuntimeGizmo` 的 3 个 ImGui 输入点缺少 control-primitive closure 标记。修复后定向 `2/2` 与 Hosting 全量均通过，没有跳过 hook、排除失败测试或把首次失败抹掉。
