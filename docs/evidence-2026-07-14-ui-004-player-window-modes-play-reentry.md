# 2026-07-14 UI-004 Player 三窗口模式与 Play 重入验真

taskIds: `UI-004`
implementationCommit: `3b5499e1b5720b1d77020bb3ab56d1a01a4f42fe`
runSessionId: `local-20260714-ui004-player-window-modes-play-reentry`
evidenceState: `local_window_modes_and_play_reentry_complete_external_dpi_input_pending`

## 结论

本轮没有继续把 Player Settings 中的枚举回显或 Silk `WindowState` 当作窗口模式已生效。真实 Windows 运行发现原来的 `BorderlessFullscreen => WindowState.Fullscreen` 会让 GLFW 切换 monitor video mode，语义实际是 exclusive fullscreen；一次修复前复现中，桌面 monitor 从 `2560×1440` 被切到 `1920×1080`。commit `3b5499e1` 改为隐藏创建 `Normal + Hidden` 窗口，在任何引擎帧、输入初始化或 SwapBuffers 之前按 Win32 `rcMonitor` 铺满，再一次性显示，从而保持 desktop mode 并避免首帧小窗闪烁。

同一 commit 新增只读 Win32 探针，直接读取 HWND、`GWL_STYLE`、`IsZoomed`、window/client/monitor/work rect 与 DPI，并以 fail-closed 规则判定请求模式。打包后的同一份 R2R/RmlUi Player 被复制到三个中文路径，分别只改 `content/startup.json.windowMode` 后运行 80 tick；Windowed、MaximizedWindow、BorderlessFullscreen 全部 `applied=True`，三份均为 3 Canvas、实际 RmlUi、无 fallback，且都有非空真实 framebuffer。

当前自动化桌面为 `1024×768`、work area `1024×720`、96 DPI。配置的 Windowed presentation 是 `1080×720`，加 non-client frame 后无法放进 work area，Windows 因而合法夹取客户区为 `1028×720`；探针明确记录 `client_matches_presentation=False` 与 `presentation_fits_work=False`，没有伪造精确 1080 宽度。Borderless 仍精确覆盖完整 `1024×768` monitor，且前后 monitor rect 不变。

当前 Release Editor 还在真实 desktop GL 窗口中重跑了既有 Game View 生命周期探针：第一轮 Play 实际移动玩家并提交绘制命令，Stop 后 UI stack 清零，第二轮 Play 重新创建脚本控制器、UI stack 与玩家视觉。第二轮 framebuffer 显示 Play 激活、`Runtime (Play) · 7 entities`、完整 world 与 Web UI，Game UI 没有覆盖右侧 Inspector/Console。

`UI-004` 继续保持 `[~]`。本报告闭合本机三种 Player WindowMode 与脚本化 Play→Stop→Play 回归，不把自动动作冒充物理鼠标键盘，不把 96 DPI 单屏冒充 150%/200% 或跨屏，也不替代外部 reviewer。

## 根因与修复

- `WindowState.Fullscreen` 会进入 GLFW monitor/video-mode 路径，不符合 Unity-like Borderless Fullscreen 的 desktop-mode 语义。
- `RenderBackendSelector` 现在把 Borderless 映射为 `WindowState.Normal + WindowBorder.Hidden + IsVisible=false`。
- `RenderWindow` 在 `window.Initialize()` 后、`CreateInput()`/GL 查询/首帧之前读取目标 monitor；Windows 使用 `GetMonitorInfoW.rcMonitor`，避免 Silk/GLFW `IMonitor.Bounds` 返回 work area 而漏掉任务栏区域。
- 窗口完成 position/size 设置后只显示一次并泵一次 events，首帧从正确尺寸开始。
- `PlayerWindowModeProbe` 不改变窗口，只报告真实 HWND/style/geometry；Windowed 精确尺寸只在请求外框能够放进 work area 时强制，合法 OS clamp 会显式留下原因字段。
- `RenderWindowIntegrationTests` 与三模式 native smoke 进入同一 non-parallel collection，修复并行 GLFW 测试偶发 `Failed to register window class: 类已存在`。
- `tools/run-player-window-mode-probe.ps1` 从同一已打包 Player 建立三份中文路径副本，拒绝命令行 `--window-mode` 覆盖，检查实际窗口、RmlUi、多 Canvas、BMP 与 Borderless monitor mode。
- 正式输出更新与独立 verifier 现在都要求默认 `Windowed` 的真实 probe 为 `applied=True`，而不是只校验 build-result 配置。

## 同 commit Player 矩阵

构建命令：

```pwsh
pwsh tools/build-player.ps1 -Rid win-x64 -Channel r2r -Configuration Release -Output artifacts/ui004-window-mode-player-3b5499e1 -ProductName 'PixelEngine Demo' -StartScene scenes/lava-mine.scene -WindowWidth 1080 -WindowHeight 720 -WindowMode Windowed -VSync true -RuntimeUiBackend RmlUi -ReleaseChannel Production
```

构建的 native、publish、verify、package、audit 全部通过；发行 zip SHA256 为 `95a9fcddcd0c2ee31e276b81e60cd7a9e870a6ea02f1b7537ff74b26f8655736`。

矩阵命令：

```pwsh
pwsh tools/run-player-window-mode-probe.ps1 -PlayerRoot artifacts/ui004-window-mode-player-3b5499e1/player -OutputRoot artifacts/ui004-player-window-matrix-3b5499e1 -WindowTicks 80 -TimeoutSeconds 180
```

稳定报告：`artifacts/ui004-player-window-matrix-3b5499e1/report.json`，`schema=pixelengine.player-window-mode-probe/v1`，`gitCommit=3b5499e1b5720b1d77020bb3ab56d1a01a4f42fe`，`allPassed=true`。

| 模式 | 真实平台状态 | Window / Client / Monitor | UI | Framebuffer SHA256 |
|---|---|---|---|---|
| Windowed | visible；not zoomed；caption + thick frame；合法小屏夹取 | `1044×759` / `1028×720` / `1024×768` | 3 Canvas；RmlUi；无 fallback；中文路径 | `227c1f5d90cffce33c07277c04bf46e5b602f196654ecfb95da3b29307ee0965` |
| MaximizedWindow | visible；zoomed；caption + thick frame；覆盖 work area | `1040×736` / `1024×697` / `1024×768` | 3 Canvas；RmlUi；无 fallback；中文路径 | `94f5f8ee7e250ee95a98a286c5c7f95ca676bbe2c0fb22f94586a89fc4a90ac8` |
| BorderlessFullscreen | visible；popup；无 caption/thick frame；not zoomed | `1024×768` / `1024×768` / `1024×768` | 3 Canvas；RmlUi；无 fallback；中文路径 | `c4015bfd8665f73dfc4b6f1849cac4cec74f0b10ed015707a5dd209f29b871f6` |

三张 framebuffer 均逐张以原始分辨率复核：world、玩家、HUD、主菜单、Constant Pixel Size 与 Constant Physical Size overlay 完整可见；4:3/小窗口下按既定 CanvasScaler 规则重排，没有空白帧、整窗裁切或旧脚本 GUI 重叠。

## Editor Play→Stop→Play

运行命令：

```pwsh
apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe --project demo/PixelEngine.Demo --scene scenes/lava-mine.scene --window-ticks 100 --scripted-gameview-probe --capture-frame artifacts/ui004-editor-gameview-3b5499e1/second-play.bmp --log-directory artifacts/ui004-editor-gameview-3b5499e1/runtime-logs
```

关键摘要：

```text
completed=True
start_x=51.000
end_x=126.542
player_moved=True
visual_commands=6
render_overlay_commands=6
first_play_exited=True
exit_ui_stack_depth=0
second_play_entered=True
second_ui_stack_depth=2
second_controller_found=True
second_controller_enabled=True
second_controller_faulted=False
second_visual_commands=6
second_play_ui_restored=True
```

第二轮 Play framebuffer：`1028×720`，2,960,694 bytes，SHA256 `a82455e357039917e9c1f3bff69dddfc494f52b7f725f1993a14b43155684547`。运行日志未命中 error/exception/fatal/fault。

## 自动化验证

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore` | 32 projects；0 warning / 0 error |
| Rendering real desktop GL targeted | 10 passed / 0 failed |
| `PIXELENGINE_RENDERING_GL_SMOKE=1` Rendering 全量 | 221 passed / 1 个 ANGLE-only skipped / 0 failed |
| Demo 全量 | 140 passed / 1 个 native 条件 skipped / 0 failed |
| Hosting WindowMode/FinalOutput 定向 | 15 passed / 0 failed |
| Hosting 全量 | 793 passed / 7 个显式环境条件 skipped / 0 failed；TRX: `tests/PixelEngine.Hosting.Tests/TestResults/ui004-hosting-full.trx` |
| Editor Shell Release build | 0 warning / 0 error |
| 同 commit R2R/RmlUi Player | native / publish / verify / package / audit 全部通过 |
| 三窗口模式打包矩阵 | 3/3 passed；80 ticks；真实 HWND/style/geometry + RmlUi + BMP |
| Editor Game View Play 重入 | completed；Stop stack=0；第二次 Play stack=2；控制器无 fault；真实 framebuffer |

## Windows 控制与证据边界

- Windows 应用控制可以发现 Player，并通过 Windows Graphics Capture 看到 1082×752 外框；但该 desktop GL 客户区在 WGC 中表现为黑白/缺失内容，不能作为画面正确性证据。
- 显式窗口激活和 Alt+F4 均返回 `failed to activate captured window`。本轮没有绕过这一边界发送物理输入，也没有把失败记成通过。
- 画面证据来自引擎在真实 GL swapchain 尺寸下回读的 BMP；窗口模式证据来自只读 Win32 API；二者相互独立。
- Editor Play 生命周期动作发生在真实窗口与真实 Engine/Script/UI backend 中，但按 tick 自动触发，属于脚本化输入证据，不等同于人工点击两轮 Play。

## 未关闭条件

- 在可激活窗口的环境中复走 main menu、settings、pause/resume、result 与两轮人工 Play/Stop。
- 在足够大的显示器上验证 Windowed 1080×720 精确客户区和固定 1920×1080 presentation。
- 150%/200% 物理 DPI、不同 DPI 显示器跨屏、portrait/16:9/4:3 与 maximize-on-play 的同会话真实窗口矩阵。
- ManagedFallback 与 RmlUi 的真实人工输入对照，以及独立 reviewer 对 Unity-like 产品面的最终复核。
- 本提交进入 canonical 记录后，从 detached clean worktree 刷新并独立验证仓库根 `最终输出/`。
