# 2026-07-14 UI-004 clean final output 与中文路径 RmlUi 闭环

taskIds: `UI-004`
implementationCommit: `3b1d9cd204857c91414d642eb79a1b51c0027c8d`
runSessionId: `local-20260714-ui004-clean-final-unicode-path`
evidenceState: `clean_final_output_complete_external_window_matrix_pending`

## 结论

本轮没有把“请求 RmlUi”或 staging 窗口能启动当成发布成功。commit `32a0d7d7` 先把 Game UI 的实际 backend、fallback reason 与 native profile 写入稳定 probe，并让正式输出更新与独立 verifier 对 3 Canvas、实际 backend 和 fallback 状态 fail-closed。随后从正式中文目录再次启动 Player，发现此前 ASCII staging 未覆盖的真实缺陷：`Rml::LoadFontFace` 无法打开 `最终输出\游戏Demo\content\ui\fonts\NotoSansSC-VF.ttf`，运行时因此回退 `ManagedFallback`。

commit `3b1d9cd2` 为 RmlUi 安装 UTF-8 感知的 native `FileInterface`：Windows 使用 `std::filesystem::u8path` 与 `_wfopen` 打开 UTF-16 路径，其他平台继续使用 UTF-8 `fopen`。正式输出脚本同时改为在 `游戏Demo构建` 中文路径内发布并运行 Player，probe 必须报告 `content_path_non_ascii=True`、`active=RmlUi`、`fallback=False`，从而阻止纯 ASCII staging 再次产生假绿。

最终从全新 detached clean worktree 构建的 `win-x64 / Release / r2r / RmlUi` 产物已提升到仓库根 `最终输出/`。clean 输出、提升后的根正式输出均通过独立 verifier；根正式目录再运行 80 tick 退出码为 0，实际 backend 为 RmlUi，没有 fallback。

UI-004 仍保持 `[~]`：本报告不把自动窗口 probe 冒充真实鼠标/键盘输入、不同物理 DPI/跨屏、三种 Player WindowMode 或外部 reviewer 证据。

## 根因与修复

- `RmlUiBackend` 已正确把托管路径编码为 UTF-8；缺陷位于 RmlUi 默认 `FileInterfaceDefault`，Windows 上直接 `fopen(const char*)` 会按 ANSI code page 解释路径。
- 字体文件本身、打包内容 hash、native DLL 和 GL profile 均正常；只有移动到含中文组件的正式目录后失败，因此不能用 ASCII build directory 的通过结果替代正式路径验收。
- `PeUiFileInterface` 在 `Rml::Initialise()` 前注册，并在初始化失败、context 创建失败和最后一个 renderer 销毁后解除，保持 RmlUi 全局接口生命周期对称。
- `RmlUiBackendLoadsFontFromUnicodePathWhenGlSmokeIsEnabled` 把真实 `NotoSansSC-VF.ttf` 复制到 `像素引擎-最终输出/.../游戏Demo/content/ui/fonts` 后初始化 native backend，锁定该回归。
- `FinalOutputVerifierRejectsRequestedRmlUiFallback` 与 `FinalOutputVerifierRejectsAsciiOnlyDemoProbe` 分别拒绝实际 backend 回退和纯 ASCII content path。

## 自动化与 native 验证

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore -m:1` | 0 warning / 0 error |
| `PixelEngine.UI.Tests` 全量 | 156 passed / 12 个显式 native 条件 skipped |
| `PIXELENGINE_RENDERING_GL_SMOKE=1` UI 全量 | 164 passed / 4 个 ANGLE-only 条件 skipped |
| `PixelEngine.Hosting.Tests` 全量 | 791 passed / 7 个显式环境条件 skipped |
| Hosting Game UI desktop GL 定向 | 3 passed / 0 skipped |
| `PixelEngine.Demo.Tests` 全量 | 140 passed / 1 个显式 native 条件 skipped |
| Demo RmlUi screen load/render | 1 passed / 0 skipped |
| FinalOutput 定向 | 13 passed / 0 skipped |
| `tools/build-native.ps1 -Rid win-x64 -Configuration Release` | native rebuild 通过 |
| 双中文路径 R2R 根启动器 20 tick | exit 0；RmlUi；无 fallback |
| `git diff --check` | clean |

双中文路径诊断 Player 的稳定摘要：

```text
game_ui_probe attached=True, canvases=3, requested=RmlUi, active=RmlUi, fallback=False, content_path_non_ascii=True, fallback_reason=<none>, native_profile=RmlUi_Renderer_GL3; #version 330 core; profileId=0
```

## detached clean worktree 与正式输出

- clean worktree：`artifacts/clean-worktrees/ui004-final-3b1d9cd2`，detached HEAD `3b1d9cd204857c91414d642eb79a1b51c0027c8d`。
- submodule：Box2D `8c661469`、FreeType `0a0221a1`、dlg `395ccad2`、RmlUi `1b69207f`。
- 构建命令：`pwsh -NoProfile -File tools/update-final-output.ps1 -Rid win-x64 -DemoChannel r2r -Configuration Release -DemoRuntimeUiBackend RmlUi`。
- Player 实际从 `artifacts/final-output-staging/.../游戏Demo构建/player` 运行 80 tick；不是 ASCII-only probe。
- clean 输出独立审计：`ok=True`，`gitCommit=3b1d9cd2...`，`checksum_count=341`。
- clean `最终输出/游戏Demo` 根启动器复跑 20 tick：exit 0，3 Canvas，RmlUi，无 fallback，中文 content path 为 true。
- 提升后的仓库根 `最终输出/` 再次独立审计：341 项 checksum 全部通过；根启动器复跑 80 tick：exit 0，`window_frame_probe` 存在，RmlUi，无 fallback。

正式输出关键记录：

| 文件 | Bytes | SHA256 |
|---|---:|---|
| `_验证记录/demo-window.bmp` | 3,110,454 | `e07078c494a4d629189fb5b464d011b26183ca8beb28c093cf5f1dd7ac1f0426` |
| `_验证记录/editor-default-workbench.bmp` | 3,686,454 | `70674cb7153141d05b582ec0e57559f62c314267b7106d0a9cda48527533e052` |
| `_验证记录/logs/demo-window.stdout.log` | 5,226 | `23c91e32281bd0dcb923b619f44c8e8a22efe6f3e7c893344857a61f4e429a74` |
| `_验证记录/manifest.json` | 3,112 | `d2679bdc2266d7aba18ad1481f7c31b648d8f8f1c7f42d2d942237cb984582ec` |
| `SHA256SUMS` | 37,748 | `6e015e24dff050e88b70044f3840b85c155bb5ff8e1618bec7a8835bfe5aaf49` |

manifest 记录 `demoRuntimeUiBackendRequested=RmlUi`、`demoRuntimeUiBackendActive=RmlUi`、`demoRuntimeUiBackendFallback=false`，并把 Demo window probe 标为 `unicodePath=true`。

## 未关闭条件

- 可采信的真实窗口鼠标/键盘路线：main menu、settings、pause/resume、result，以及 Play→Stop→Play。
- 16:9、4:3、portrait、固定 1920×1080、150%/200% 物理 DPI 与跨屏复走。
- Windowed、Maximized Window、Borderless Fullscreen 三种 Player WindowMode 的同 commit 真实窗口矩阵。
- 外部 reviewer 对 UI 产品面和 Unity-like 工作流的复核。
