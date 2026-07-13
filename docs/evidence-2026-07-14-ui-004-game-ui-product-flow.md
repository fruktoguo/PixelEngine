# 2026-07-14 UI-004 游戏 UI 产品状态流与视觉层级验真

taskIds: `UI-004`
implementationCommits: `716e05463d3f7dc6a3931e81b044aff3e17959df`, `8a700d2083bf1b806169e83101380d60803dc711`
runSessionId: `local-20260714-ui004-game-ui-product-flow`
evidenceState: `local_game_ui_product_flow_complete_external_physical_input_dpi_pending`

## 结论

commits `716e0546` 与 `8a700d20` 闭合了默认 Demo 游戏 UI 的两类产品缺陷：首先修复主菜单、HUD、telemetry 与两份 CanvasScaler 校准 overlay 同时叠加，以及开始游戏时真实 RmlUi 因把 telemetry path 写入 HUD document 而崩溃；随后把主菜单与 gameplay HUD 重构为互斥、可读、符合游戏层级的状态。

默认 Player 现在只显示主菜单；开始游戏成功创建 HUD 后才隐藏菜单；普通 gameplay 只显示生存状态与任务状态，不再常驻 telemetry、Constant Pixel Size 或 Constant Physical Size 校准条。诊断动作才会同时打开 telemetry 与两个辅助 Canvas，关闭诊断或 Stop 会对称清空。Esc 在主菜单阶段不再误开暂停层。

实现提交 `8a700d20` 上的真实 desktop GL/RmlUi 窗口短跑确认：三个 scene Canvas 均已挂载，requested/active backend 均为 `RmlUi`，`fallback=False`；脚本化开始游戏后的状态为 `ui_main_visible=False`、`ui_hud_visible=True`、`ui_telemetry_visible=False`、`ui_pixel_overlay_visible=False`、`ui_physical_overlay_visible=False`。主菜单与 gameplay 两张 `1028×720` BGRA framebuffer 已人工检查，分别只呈现菜单卡片和 HUD，不再出现跨状态叠加。

`UI-004` 继续保持 `[~]`：本报告中的 gameplay 切换来自可重跑的引擎脚本化 UI event，不冒充物理鼠标点击。Computer Use 能发现并截图真实 Player，但刷新窗口句柄后两次激活均返回 `failed to activate captured window`，因此诊断按钮物理点击、150%/200% DPI、跨屏和外部 reviewer 仍未闭合。

## 根因与修复

### 状态叠加

- `GameUiDemoController.StartForService` 启动时同时 `ShowMainMenu()` 与 `ShowHud()`，导致产品状态从第一帧就重叠。
- `lava-mine.scene` 又把 `pixel-overlay` 与 `physical-overlay` 配为两个辅助 Canvas 的 `initialScreenId`，使 CanvasScaler 校准 UI 永久覆盖在正式游戏上。
- 修复后仅主菜单是默认 screen；Start action 先创建 HUD，只有 HUD 成功后才隐藏菜单；两个辅助 overlay 只由 diagnostics 状态显式控制。

### RmlUi 文档路径崩溃

修复前真实 RmlUi 开始游戏会产生：

```text
System.Collections.Generic.KeyNotFoundException: RmlUi 文档 5 未绑定 UI 模型路径: 2114758668
at PixelEngine.UI.RmlUiBackend.SetModelValue(...)
at PixelEngine.Demo.GameUiDemoController.SetTelemetryValue(...)
at PixelEngine.Demo.GameUiDemoController.ShowHud()
```

原始崩溃日志：`artifacts/ui004-game-ui-flow-precommit/runtime/demo-crash-20260713-223716.log`，SHA256 `bd20a59bd0b276bf5f885cfbe238605b6b71a72b27c91f1890a980f9dd09507c`。Fake service 过去未校验 document/path 合同，因此单元测试误放过了将 telemetry 字段写入 HUD document 的错误。修复后 telemetry 只写 telemetry screen，测试 fake 也按 HUD/telemetry document 分别拒绝未知 path。

### 产品视觉层级

- 主菜单改为全屏暗色 scrim + 右侧任务卡片，突出标题、任务说明与主操作“开始游戏”，设置/背包/对话降为次级按钮。
- gameplay HUD 拆为左上生存状态、右上任务状态和独立的暂停/诊断控制，不再复用菜单或 scaler 校准文案。
- DOM 回归测试固定 `menu_scrim`、`briefing`、`status_panel`、`objective_panel` 与 action contract，并拒绝普通 menu/HUD 出现 Constant Pixel/Physical Size 文案。

## 提交绑定真实窗口

执行命令：

```pwsh
dotnet demo/PixelEngine.Demo/bin/Release/net10.0/PixelEngine.Demo.dll `
  --no-hot-reload --no-vsync --window-ticks 100 `
  --content demo/PixelEngine.Demo/content --scene scenes/lava-mine.scene `
  --capture-frame artifacts/ui004-game-ui-product-flow-8a700d20-final/menu/framebuffer.bmp `
  --log-dir artifacts/ui004-game-ui-product-flow-8a700d20-final/menu/runtime

dotnet demo/PixelEngine.Demo/bin/Release/net10.0/PixelEngine.Demo.dll `
  --no-hot-reload --no-vsync --window-ticks 160 --scripted-window-demo `
  --content demo/PixelEngine.Demo/content --scene scenes/lava-mine.scene `
  --capture-frame artifacts/ui004-game-ui-product-flow-8a700d20-final/gameplay/framebuffer.bmp `
  --log-dir artifacts/ui004-game-ui-product-flow-8a700d20-final/gameplay/runtime
```

| 状态 | 真实结果 | 原始输出 SHA256 |
|---|---|---|
| 主菜单 | `1028×720×32bpp`；真实 RmlUi；单一右侧主菜单卡片；无 HUD/scaler overlay | framebuffer `18d425df2d58437f7238245d515a3f3a95eb084479d77f9f19d99955e4e47873`；stdout `5b54a6b5924e56e17667a323fc6023bd9631644ab53594cdc4971f97ab3b0ff0` |
| gameplay | `1028×720×32bpp`；主菜单 hidden、HUD visible、三种 diagnostics screen hidden | framebuffer `064c8e776532bd47023744aebc04e4828fbf0c93177b1214fefc6f03ba737104`；stdout `4d6e90ee74c514f4d5fe4874d5811bbd40917841d5fbafa2bfbe7d910851a45d` |

两次日志均记录 `game_ui_probe attached=True, canvases=3, requested=RmlUi, active=RmlUi, fallback=False`。gameplay 日志还记录 `ui_gameplay_started=True`、三个 runtime/service/scene canvas 计数均为 3、HUD 无 fault，并完成 160 个真实窗口 tick。

## 自动化验证

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1` | 32 projects；0 warning / 0 error |
| UI 全量 | 156 passed / 12 个显式 native 条件 skipped / 0 failed；TRX: `artifacts/ui004-game-ui-product-flow-8a700d20-final/test-results/ui/ui.trx` |
| Hosting 全量 | 796 passed / 7 个显式环境条件 skipped / 0 failed；TRX: `artifacts/ui004-game-ui-product-flow-8a700d20-final/test-results/hosting/hosting.trx` |
| Rendering 全量 | 195 passed / 27 个显式 GL/ANGLE/window 条件 skipped / 0 failed；TRX: `artifacts/ui004-game-ui-product-flow-8a700d20-final/test-results/rendering/rendering.trx` |
| Editor 全量 | 117 passed / 0 failed；TRX: `artifacts/ui004-game-ui-product-flow-8a700d20-final/test-results/editor/editor.trx` |
| Demo 全量 | 142 passed / 1 个显式 native 条件 skipped / 0 failed；TRX: `artifacts/ui004-game-ui-product-flow-8a700d20-final/test-results/demo/demo.trx` |

## 证据边界与剩余条件

- 真实 framebuffer 与 RmlUi 状态日志证明默认菜单、gameplay HUD 和 diagnostics screen 的产品状态互斥；脚本化 Start event 不是人工鼠标输入。
- Computer Use 在同一机器上成功发现 `PixelEngine.Demo.exe`、取得实时标题与窗口截图；`get_window_state` 可用，但 fresh `get_window` 后的 `activate_window` 仍重复失败，因此停止继续输入，没有写成点击通过。
- 当前 active desktop 为 `1024×768`、96 DPI；未覆盖 150%/200% 物理 DPI、不同 DPI 显示器跨屏或足够大的 1920×1080 物理 surface。
- 外部 reviewer 对主菜单、HUD、键鼠手感与 diagnostics 的最终体验复核仍缺失。
- 本报告登记后仍需从 detached clean worktree 重建、替换并独立验证仓库根 `最终输出/`。
