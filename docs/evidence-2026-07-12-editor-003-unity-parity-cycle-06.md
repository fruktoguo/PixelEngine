# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 06：Windows 捕获与平台输入

taskIds: `EDITOR-003`
implementationCommit: `83c55fe05420ae290e32cbf287563296c4ab904e`
baseImplementationCommit: `1ae62d28001cb3a4c5e5cf366d1705e6adeaa453`
nativeRmlUiCommit: `1b69207f7dfb0e6c751e553b13770bf689406fa0`
runSessionId: `local-20260712-editor003-unity-parity-cycle06`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮关闭了此前五轮始终显式保留的 Windows Graphics Capture 白屏差异。PixelEngine Editor 现在仍在同一 HWND、同一 desktop GL context 中完成引擎与 UI 绘制，但最终帧经 `WGL_NV_DX_interop2` 共享给 D3D11，再由 FlipDiscard swap-chain 呈现给 DWM；同一 WGC probe 已能直接捕获完整 Hub、菜单、工具栏、Scene、Hierarchy、Project 与 Inspector，而不是内部 framebuffer、GDI 截图或第二窗口替代品。共享 managed ImGui renderer 同时补齐焦点、系统光标、`WantSetMousePos` 与 Windows IME caret/candidate 平台桥；RmlUi compositor 不再破坏调用方 framebuffer。

`EDITOR-003` 继续保持 `[~]`。当前进程已验证 per-monitor DPI aware、150% DPI 与 resize 资源重建，但 200%/跨屏连续 DPI、实际 dock/undock 恢复、真实中文输入法 composition、外部文件拖入 Project，以及连续 author→play→edit→build→run 路线尚未全部复走，不能用本轮捕获链闭合提前结束整项。

## 同机 Unity 与根因对照

同机 Unity 6.5 `6000.5.3f1`、PixelEngine 旧 WGL、临时 x64 ANGLE probe 与本轮实现均由同一 Windows Graphics Capture 路径按 HWND 捕获：

| 表面 | 真实结果 | 证据 |
|---|---|---|
| Unity 6.5 默认工作台 | 完整客户区，可见菜单、面板与 Scene | `%TEMP%/unity-true-wgc.png`，3844×2101，286,975 bytes，SHA256 `968E154C72010AF6B8D3F120DF52EE9E95B051E348A3C04EB266A21562FD0FC0` |
| PixelEngine 旧 desktop WGL | 白色客户区；内部 framebuffer 正常 | `%TEMP%/pixelengine-editor003-true-wgc.png`，1284×767，19,193 bytes，SHA256 `85F9585AD3B7B3F5C5E05AC476F0D317B3433A616609E0663E810CD0B64B02B8` |
| GLFW/EGL + Chrome 150 x64 ANGLE probe | 仍为白色客户区，证明 ANGLE identity 不等于 WGC 可见 | `%TEMP%/pixelengine-editor003-angle-v6-chrome-probe-wgc.png`，1284×767，19,118 bytes，SHA256 `248BE1CF98D80A8C6F2CE469D95FA58C636B46B0AF6D62925FF74EFE33E65CCE` |
| PixelEngine 本轮 DXGI presenter | 完整、方向正确的同 HWND 客户区 | Hub 与 Workbench 证据见下节 |

Win32 属性探针同时确认 Unity 与旧 PixelEngine framebuffer alpha 均为 255，PixelEngine 未使用 layered、display-affinity 或 `WS_EX_NOREDIRECTIONBITMAP`；所以没有把根因误归为透明度，也没有用 alpha 清除值掩盖。Silk ANGLE NuGet 的 `win-x64` native asset 实测为 32-bit PE，不能进入 x64 Editor；临时借用浏览器 ANGLE 又无法解决捕获，最终路线因此收敛到系统 D3D11/DXGI 与 driver WGL interop。

## 本轮实现

- 新增 `DesktopGl33DxgiInterop` 后端：capture-compatible 选择先尝试 desktop GL + DXGI presenter，driver/设备/swap-chain/resize 失败时记录诊断并回退既有 desktop GL；玩家和非 Windows 路线不变。
- GL 将完整帧绘入独立 D3D11 render-target/shader-resource texture 注册出的 renderbuffer/FBO；D3D fullscreen triangle 采样共享 texture、完成 Y 翻转并写入 FlipDiscard 当前 buffer 0。present 全程 GPU-to-GPU，不使用 CPU readback、GDI 或额外窗口。
- resize 时严格按 unlock/unregister → 重建共享 texture、GL renderbuffer/FBO 与当前 swap-chain buffer → relock 的顺序；present 每帧只重新取得 Flip model 当前 buffer 0，不缓存两个固定 backbuffer。
- `PixelEngine.Gui` 新增 Gui/Editor 共用的 managed `ImGuiGlRenderer`，直接复用 `RenderWindow.Gl`，覆盖 desktop GL/GLES shader、font/user texture、VtxOffset、clip/scissor、reset callback 和 GL state 恢复；移除 `Hexa.NET.ImGui.Backends` 的隐藏 native/context 假设。
- 新增共享 `ImGuiPlatformBridge`：窗口 focus 进入 `AddFocusEvent`，失焦清空左右 modifier；ImGui cursor 与 `WantSetMousePos` 同步 Silk 系统光标；Windows `Platform_SetImeDataFn` 生成 composition/candidate form 并锚定 caret 下方。
- RmlUi native GL3 backend 在 render 前后保存并恢复调用方 draw/read framebuffer；native 子模块提交为 `1b69207f`，父仓库记录该精确指针。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64；进程 DPI awareness `2`，当前窗口 `GetDpiForWindow=144`。

| 命令 | 结果 |
|---|---|
| `pwsh tools/build-native.ps1 -Rid win-x64 -Configuration Release` | RmlUi `PixelEngine.UI.Native.dll` 构建成功 |
| `$env:PIXELENGINE_RENDERING_GL_SMOKE='1'; $env:PIXELENGINE_D3D_DEBUG='1'; dotnet test tests/PixelEngine.Rendering.Tests/PixelEngine.Rendering.Tests.csproj -c Release --filter FullyQualifiedName~WindowsDxgiInteropPresenterRendersPresentsAndResizesWhenExplicitlyEnabled` | 1/1 passed；D3D debug 下完成 render/present、64×64→96×80 resize、再次 present |
| `$env:PIXELENGINE_RENDERING_GL_SMOKE='1'; dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RmlUiGlBootstrapSmokeTests&FullyQualifiedName!~Angle"` | 10/10 passed；含 native compositor GL/FBO state restore |
| `dotnet build PixelEngine.sln -c Release -m:1` | 32 projects；0 warning；0 error |
| `dotnet test PixelEngine.sln -c Release --no-build -m:1` | 1,770 passed；39 个显式 native/GPU/目标环境 smoke skipped；0 failed；exit code 0 |
| `pwsh tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | 实现提交 passed；只含本轮 41 个实现、测试、设计与 native pointer 文件 |

诊断过程中曾给 solution 级命令加 `--blame-hang-timeout 5m`；该 collector 在 Hosting 长测尚未结束时杀死 testhost，因此该次非零结果未被记作成功。随后被点名测试独立 1/1 passed、Hosting 全套 673 passed / 4 skipped，最终以上无 Blame 的整解命令以 exit code 0 完成。

## 真实窗口与 WGC 复核

当前实现完整 Hub：

- `%TEMP%/pixelengine-editor003-cycle06-hub-wgc.png`
- 3844×2101，1,092,691 bytes
- SHA256 `FA0D1538D7E5A5AEFD9166243AD76F2AB6558F4EB3D29CEB920C4294A88317D9`
- WGC 直接显示 Project Browser、Recent 工程、Search/Add/New 与完整深色客户区；屏幕上的 Codex pet overlay 未进入按 HWND 捕获的图像。

当前实现完整 Workbench：

- `%TEMP%/pixelengine-editor003-cycle06-workbench-wgc.png`
- 3844×2101，652,710 bytes
- SHA256 `0E686A46BC14C9D0DEFC91F3BDE82649BFC75B89277524F1D064796AD9204835`
- 真实启动 Hub、双击 Recent 打开 Demo 后捕获；菜单、Play toolbar、Scene、Hierarchy、Project、Inspector 与底部状态均完整、方向正确，无白面或外部 overlay。

同一 presenter resize probe：

- resize 前 `%TEMP%/pixelengine-editor003-dxgi-flip-wgc.png`：1284×767，53,931 bytes，SHA256 `F11A07BFA57607A80B5F046A9D8E3A615A0F2DDF3D265DFC38890337F46DB16C`
- resize 后 `%TEMP%/pixelengine-editor003-dxgi-flip-resized-wgc.png`：1530×963，60,697 bytes，SHA256 `570679DA51912EA7018D2FDD116B8569FB4C800ADAD2191AC65E058A5E9864A1`
- 两帧均由 WGC 直接得到完整 Hub；尺寸变化后 shared texture/FBO/swap-chain 重建正常，未复现旧帧、上下翻转或白屏。

## 下一轮差异

- 真实拖拽 dock tab、浮动/重新停靠、Layout reset 与跨 session 保存恢复尚需在隔离布局状态下完整复走。
- 200% DPI、跨不同 DPI 显示器的连续移动/resize 与一帧内坐标一致性仍需真实硬件路线；当前只关闭 150% 当前屏和 presenter resize。
- IME 平台 callback、caret/candidate form 与焦点清理已有实现和自动化，但真实中文输入法 composition/candidate 位置尚未由输入法 UI 截图验收。
- Silk window 尚未接外部文件 drop 到 Project import；内部 Project→Inspector/Scene typed drag/drop 不等于系统文件拖入。
- Play/Pause/Step、Undo/Redo、Scene/Game、Prefab、Settings、外部脚本编辑、Build And Run 与失败恢复仍需在同一工程连续执行并与 Unity 逐项清零。
