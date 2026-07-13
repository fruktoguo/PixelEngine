# 2026-07-14 UI-004 Game View 展示矩阵验真

taskIds: `UI-004`
implementationCommit: `cf4a1ddd5d95f5a66a6851e470fad04c2bf337ce`
runSessionId: `local-20260714-ui004-gameview-presentation-matrix`
evidenceState: `local_gameview_presentation_matrix_complete_external_dpi_input_pending`

## 结论

commit `cf4a1ddd` 在真实 Windows desktop GL Editor 上建立并通过五场景 Game View 展示矩阵：16:9、4:3、9:16、固定 `1920×1080` 与 `Maximize On Play`。每个场景均从隔离 workspace 启动，真实进入第一轮 Play、移动玩家、Stop 清空 runtime UI，再进入第二轮 Play 并恢复脚本控制器、UI stack 与 world/Web UI 合成。

本轮不再分别读取可能跨 revision 的 preset、texture size 与 world rect。`GameViewPanel` 只在 Hosting descriptor、viewport texture dimensions、revision 和 world content rect 全部同源时返回不可分割的 `ScriptedGameViewPresentationSnapshot`；探针 v2 因而会对旧帧/新帧混合状态 fail closed。

Editor framebuffer 截图也由 `SwapBuffers` 之后的回读移到 before-swap hook。原因是 DXGI/WGL presenter 在 swap 后会立即以 `WRITE_DISCARD` 重新锁定共享纹理，旧顺序可能抓到被丢弃或不完整的 Editor 帧。新路径在 capture request 到达后额外执行一个 zero-delta 完整 tick，并在同一帧 swap 前回读。Project Picker/Hub 的无项目路径同样显式完成 draw→capture→swap。

矩阵报告 `schema=pixelengine.editor-gameview-presentation-probe/v1`、`gitCommit=cf4a1ddd5d95f5a66a6851e470fad04c2bf337ce`、`allPassed=true`。所有 BMP 均非空，并对顶部菜单/工具栏、右侧 Inspector/Console surface，以及非最大化场景中的 Hierarchy dock 做区域颜色与 near-black fail-closed 检查；这些区域 `nearBlackRatio=0`。独立裁剪复核也确认顶部工具栏、Hierarchy、Project 与 Inspector 没有被 Game UI 覆盖。

`UI-004` 继续保持 `[~]`。本报告闭合本机脚本化 Game View preset、fixed-resolution、Maximize On Play 与 Play 重入矩阵；它不把 96 DPI 单屏、Fit 后显示或自动动作冒充物理输入、不同 DPI/跨屏和外部 reviewer 验收。

## 运行命令

```pwsh
pwsh -NoProfile -File tools/run-editor-gameview-presentation-probe.ps1 `
  -OutputRoot artifacts/ui004-gameview-presentation-matrix-cf4a1ddd `
  -WindowTicks 100
```

原始报告：`artifacts/ui004-gameview-presentation-matrix-cf4a1ddd/report.json`。`artifacts/` 是 volatile 原始输出；本文件和 `docs/evidence-index.json` 是长期稳定登记。

## 五场景矩阵

| 场景 | Presentation / source | 固定 640×360 world content | Framebuffer SHA256 |
|---|---|---|---|
| 16:9 | `519×292` / `EditorAspectRatio` | `0:0:519×292` | `695f30d10ab985cfbf8e5514a4eb0d9841519088d92f24e2bb7dc76bc9c0ad93` |
| 4:3 | `519×389` / `EditorAspectRatio` | `0:48:519×292`，上下 letterbox | `2103710b0cc0983a2465abfa41afea80c8cf17db28454e73ffda8071272e70c6` |
| 9:16 | `320×568` / `EditorAspectRatio` | `0:194:320×180`，上下 letterbox | `6081402bc99c2598865d122fab4dde5c137a57140c5bf39c9dc06cc10ebbc60c` |
| Fixed 1920×1080 | `1920×1080` / `EditorFixedResolution` | `0:0:1920×1080` | `b30ff72d5ba932feea2854e12f1f34662d0ae280f61f760c4e1eceb68ea38eee` |
| Maximize On Play | `1280×720` / `PlayerDefault`；`maximized=True` | `0:0:1280×720` | `00d467628197089caba5266fb212010a10229e490e059423b5cfaf70ea5936a3` |

五个场景共享以下实测结果：

- `presentation_synchronized=True`；descriptor、texture revision/size 与 world rect 同源。
- 玩家 `X=51.000→126.542`；`player_moved=True`。
- 第一轮与第二轮均提交 6 条 world visual commands 和 6 条 Web UI overlay commands。
- UI stack `2→0→2`；Stop 后为 0，第二轮 Play 恢复为 2。
- 第二轮控制器存在、enabled 且 `faulted=False`。
- framebuffer 为 `1024×720×32bpp`；顶部、右侧与 dock 区域均不是空白/全黑帧。

固定 `1920×1080` 场景验证的是引擎 presentation texture 与 world mapping 确实为 `1920×1080`；由于当前物理桌面只有 `1024×768`，它在 `519×568` Game View display area 中以 Fit 显示，不能冒充 1920×1080 物理可视区域验收。

## 自动化验证

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore` | 32 projects；0 warning / 0 error |
| Hosting 全量 | 795 passed / 7 个显式环境条件 skipped / 0 failed；TRX: `artifacts/ui004-gameview-presentation-matrix-cf4a1ddd/test-results/hosting/hosting.trx` |
| Editor 全量 | 117 passed / 0 failed；TRX: `artifacts/ui004-gameview-presentation-matrix-cf4a1ddd/test-results/editor/editor.trx` |
| Demo 全量 | 140 passed / 1 个 native 条件 skipped / 0 failed；TRX: `artifacts/ui004-gameview-presentation-matrix-cf4a1ddd/test-results/demo/demo.trx` |
| 定向 snapshot/capture/tool tests | 10 passed / 0 failed |
| 五场景真实 desktop GL 矩阵 | 5/5 passed；每场景 100 ticks；Play→Stop→Play + 原子 presentation + BMP 区域检查 |
| Project Picker/Hub capture route | 完整 draw→capture→swap；非空 framebuffer；视觉复核通过 |
| PowerShell parser / `git diff --check` | passed |

## 证据边界与剩余条件

- Editor 生命周期发生在真实窗口、真实 Engine/Script/UI backend 中，但动作按 tick 自动触发，不等同于人工鼠标点击、键盘编辑或 IME 操作。
- 当前桌面为 `1024×768`、96 DPI；没有 150%/200% 物理 DPI、不同 DPI 显示器跨屏或足够大物理 1920×1080 surface 的证据。
- 还需要在可激活窗口的环境中人工复走 Game View preset/scale/crop/pan、Maximize On Play、两轮 Play/Stop 和 UI 交互。
- ManagedFallback 与 RmlUi 的人工输入/IME parity、以及外部 reviewer 对 Unity-like 体验的最终复核仍未关闭。
- 本实现与证据进入 canonical 记录后，仍需从 detached clean worktree 刷新并独立验证仓库根 `最终输出/`。
