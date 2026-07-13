# 2026-07-14 UI-004 Web Canvas Player dogfood 与三模式 CanvasScaler

taskIds: `UI-004`
implementationCommit: `ebbc9d91a230893a151ac39d8694f45314dfecd5`
runSessionId: `local-20260714-ui004-web-canvas-player`
evidenceState: `local_player_dogfood_complete_external_window_matrix_pending`

## 结论

本轮把 Demo 的玩家界面从旧脚本 ImGui 窗口切换到场景级 Web Canvas：`lava-mine.scene` 升级为 `.scene` v3，并显式声明 Scale With Screen Size、Constant Pixel Size、Constant Physical Size 三个 Canvas；默认 Player 启用 RmlUi，旧 `DemoHud` / `PlayableHud` / `PauseMenu` / 结算窗口只在场景没有 primary Web Canvas 时作为 Managed GUI 降级路径出现。

当前 commit 的 Release Player framebuffer 已同时显示主 Canvas、HUD、Constant Pixel Size 标尺和 Constant Physical Size 标尺，没有旧脚本 GUI 重叠；运行摘要确认场景解析、runtime registry 与脚本公开 API 均发现 3 个 Canvas，请求与实际后端均为 RmlUi，未发生 fallback。自动化、desktop GL native smoke 和完整 Release build 均通过。

本报告不把自动 framebuffer、合成输入测试或 native hit-test 冒充人工真实输入。本轮 Windows 应用控制能发现 `PixelEngine Demo` 窗口，但 desktop GL 窗口连续拒绝前台激活，故没有取得可采信的真实鼠标/键盘路线；UI-004 仍保持 `[~]`。

## 实现范围

- 默认 Demo 启用 `EnableGameUi()`，`startup.json` 请求 RmlUi；headless 路径仍保持 Game UI disabled。
- `.scene` v3 显式物化 3 个 Canvas，按 sorting order `0 / 100 / 200` 确定性排列，覆盖三种 `UiScaleMode`。
- 10 个 XHTML screen 使用透明 body 与定位 panel；HUD、telemetry、result 的 progress 提供可见 label/fill，不再表现为无语义色条。
- `GameUiDemoController` 只通过公开 `IGameUiService.CopyCanvases` / `PrimaryCanvas` dogfood 多 Canvas，并在暂停相位通过 `OnGui` 接收 Escape，保证 Esc→Pause→Esc→Resume。
- 旧脚本 GUI 统一通过 `LegacyGuiFallback` 门控；Web Canvas 存在时不再覆盖 game UI。
- `ManagedFallbackBackend` 为每个 backend instance 分配独立窗口 namespace，避免多个 Canvas 的本地 screen handle 相同而在共享 ImGui context 中互相覆盖。
- `EngineProbeApi.CaptureGameUi()` 只暴露 Canvas 数量和 backend selection 快照，Demo 不跨越 probe facade 直接读取 Hosting service。

## 自动化验证

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1` | 0 warning / 0 error |
| `PixelEngine.UI.Tests` 全量 | 155 passed / 11 个显式 native 条件 skipped |
| `PixelEngine.Hosting.Tests` 全量 | 789 passed / 7 个显式环境条件 skipped |
| `PixelEngine.Demo.Tests` 全量 | 140 passed / 1 个显式 native 条件 skipped |
| `PIXELENGINE_RENDERING_GL_SMOKE=1` UI RmlUi GL 类 | 12 passed / 4 个 ANGLE-only 条件 skipped |
| `PIXELENGINE_RENDERING_GL_SMOKE=1` Hosting `WhenGlSmokeIsEnabled` | 3 passed / 0 skipped |
| `PIXELENGINE_RENDERING_GL_SMOKE=1` Demo RmlUi screen load/render | 1 passed / 0 skipped |
| `tools/validate-task-catalog.ps1` | valid；81 canonical，49 done，1 active |
| `git diff --check` | clean |

其中 `WebCanvasSuppressesLegacyGuiAndOwnsEscapePauseFlow` 锁定旧 GUI 完全退场和暂停态第二次 Escape 恢复；`ManagedFallbackNamespacesWindowIdsAcrossBackendsSharingGuiContext` 锁定多 Canvas 共享 GUI context 时窗口身份不冲突；RmlUi native 测试使用当前 content 下的真实 10-screen manifest 与当前布局坐标。

## commit 绑定 Player framebuffer

运行命令：

```pwsh
dotnet run --project demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-build --no-restore -- --no-hot-reload --window-ticks 40 --scripted-window-demo --content demo/PixelEngine.Demo/content --scene scenes/lava-mine.scene --capture-frame artifacts/ui004-node6/player-rmlui-commit.bmp --log-dir artifacts/ui004-node6/player-rmlui-commit-logs
```

| 文件 | Bytes | SHA256 |
|---|---:|---|
| `artifacts/ui004-node6/player-rmlui-commit.bmp` | 3,110,454 | `baf43d19ef3355089c9efb607dbf96f08cf60448e7cec8f4436cc5767e4be5cf` |
| `artifacts/ui004-node6/player-rmlui-commit.stdout.txt` | 6,818 | `8d6c50584bc646dc04125522bcfff8fe01c9ef540c11762123c516a73087cfa4` |

关键运行摘要：

```text
ui_runtime_canvases=3
ui_service_canvases=3
ui_resolved_scene_canvases=3
ui_backend_requested=RmlUi
ui_backend_active=RmlUi
ui_backend_fallback=False
hosting_scene_kind=SceneFile
ui_canvases=3
ui_pixel_canvas=2
ui_physical_canvas=3
```

framebuffer 可见检查：左上 HUD、上方主菜单、左中 Constant Pixel Size 标尺、下中 Constant Physical Size 标尺均在世界之上透明合成；旧 `demo-hud`、`playable-hud` 和旧 pause window 均未出现。两个 overlay 没有遮挡 HUD 按钮或主菜单按钮。

`artifacts/` 是可再生目录，不是长期唯一证据；稳定结论、命令、commit、原始文件 hash 与限制均保存在本报告。

## Windows 真实输入边界

- Windows 应用控制成功列出 `PixelEngine.Demo.exe` 和标题为 `PixelEngine Demo | Render FPS ...` 的窗口，并取得 1082×752 的窗口状态。
- Windows Graphics Capture 对该 desktop GL surface 返回白色客户区；更重要的是，显式 activate 与输入动作两次均返回 `failed to activate captured window`。
- 因前台激活失败，本轮没有向报告登记 Start / Settings / Escape / Resume 的真实鼠标键盘通过结论，也没有用 PowerShell SendKeys 或其他旁路伪造。
- 可重跑的 native DOM hit-test、ordered pointer、Web Canvas Escape flow 自动化继续作为工程证据，但不替代真实输入路线。

## 硬件与运行环境

- Microsoft Windows 11 专业版 build 26100，win-x64。
- AMD Ryzen 7 5800X 8-Core Processor。
- AMD Radeon RX 7900 XT，driver `32.0.31021.5001`。
- .NET SDK `10.0.108`，Microsoft.NETCore.App `10.0.8`。

## 未关闭条件

- 通过可采信的真实窗口输入复走 main menu、settings、pause/resume、result，以及 Play→Stop→Play 生命周期。
- 按 UI-004 证据矩阵补 16:9、4:3、portrait、固定 1920×1080、150%/200% 物理 DPI、跨屏、Scene HTML preview、Game View maximize-on-play 和三种 Player WindowMode。
- 从 detached clean worktree 重建并独立验证 `最终输出/`。
- 外部 reviewer 复核 UI 产品面；自动截图和 source-contract 测试不能代替该结论。

