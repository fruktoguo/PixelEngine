# 2026-07-14 UI-004 Game View 响应式工具栏验真

taskIds: `UI-004`
implementationCommit: `d5f9ae1be2e77d6634581ce706ce55c21545df76`
runSessionId: `local-20260714-ui004-gameview-responsive-toolbar`
evidenceState: `local_gameview_responsive_toolbar_complete_external_input_dpi_pending`

## 结论

commit `d5f9ae1b` 修复了 Game View 顶部控件在窄 Editor/dock 中被整体裁掉、连 overflow 都无法访问的缺陷。修复前的 `360×720` 真实 desktop GL Editor 中，Game View 只有约 174px 可用宽度，旧的 preset + Scale + Max + overflow 仍被硬塞在同一行；framebuffer `17f29fa3e8c7d110d849216f074bdaf6d7c46d53cf73e965562fc4336abb6e5b` 只显示 `16:9` 与 `Fit`，`Max` 和 `...` 完全落到裁剪区外。

新实现不再以单个 `available < 560` 布尔值猜测布局，而是按当前 ImGui 字体、`FramePadding`、combo arrow、checkbox、`ItemSpacing` 和实际文本宽度计算预算，逐级选择 `Full`、`Compact`、`Narrow` 或 `OverflowOnly`。低频控件按空间依次移入始终保留的 overflow：隐藏 preset 时提供完整 Resolution 子菜单，隐藏 Scale 时提供 Fit/25%/50%/75%/100%/200%，同时保留 Maximize/Restore、Maximize On Play 与自定义 resolution 操作。任一 combo/popup 打开期间，`ToolbarCapturesInput` 也保持为 true，避免菜单键盘输入漏给 gameplay。

提交绑定的 `360×720` 场景实测 `toolbar_density=Narrow`、`toolbar_available=174`、`toolbar_occupied=174`、`toolbar_fits=True`、`toolbar_overflow_visible=True`；framebuffer `c08fcfab3a26e3d97c5eae79a96298d9e1693abc7a11ed9b68e6683fea239807` 清楚显示完整 `16:9` 与 `...`。宽窗口仍是 Full：1024px Editor 的 Game View 为 `519/519px`，preset、Scale、Maximize、Maximize On Play 与 overflow 全部可见。Maximize On Play 后的 1012px 面板为 `615/1012px`，没有无意义拉伸。

`UI-004` 继续保持 `[~]`。本报告证明响应式预算、可访问 overflow、真实窄窗口 framebuffer 与既有 presentation/Play 生命周期没有回归；它不把脚本化窗口动作冒充人工点击菜单，也不替代 150%/200% 物理 DPI、跨屏或外部 reviewer。

## 提交绑定矩阵

命令：

```pwsh
pwsh -NoProfile -File tools/run-editor-gameview-presentation-probe.ps1 `
  -OutputRoot artifacts/ui004-gameview-responsive-toolbar-d5f9ae1b `
  -WindowTicks 100
```

原始报告：`artifacts/ui004-gameview-responsive-toolbar-d5f9ae1b/report.json`；`schema=pixelengine.editor-gameview-presentation-probe/v1`，`gitCommit=d5f9ae1be2e77d6634581ce706ce55c21545df76`，`allPassed=true`。

| 场景 | Editor / Presentation | Toolbar | Framebuffer SHA256 |
|---|---|---|---|
| 16:9 | `1024×720` / `519×292` | Full；`519/519`；fits | `d5d27fe8661e0ae572b02ffcbb72bf40a22421d23307c17c2bdd1a1b13453ab8` |
| 4:3 | `1024×720` / `519×389` | Full；`519/519`；fits | `64ac8e864a2ae257260f9ec0dc9cd32b920fa5521ec18daf9cbd98efcb1f6502` |
| 9:16 | `1024×720` / `320×568` | Full；`519/519`；fits | `5768dd02dc671ff8fc90d30f0e855a5588d806209a02c6269c477981670fcca9` |
| Fixed 1920×1080 | `1024×720` / `1920×1080` | Full；`519/519`；fits | `faf0ffd929cc2b4556e95673017e423519920ad3bfe6aa9289606aab0b8383ae` |
| Maximize On Play | `1024×720` / `1280×720` | Full；`615/1012`；fits | `03790791edb9a597a80a97571b23f07ec1c1e4b874cac0e263468138416755ae` |
| Narrow toolbar | `360×720` / `174×98` | Narrow；`174/174`；overflow visible | `c08fcfab3a26e3d97c5eae79a96298d9e1693abc7a11ed9b68e6683fea239807` |

六个场景均再次确认：

- `presentation_synchronized=True`；Hosting descriptor、texture revision/size 与 world rect 同源。
- UI stack `2→0→2`；玩家 `X=51.000→126.542`；第二轮控制器 enabled 且无 fault。
- 每轮 6 条 world visual commands 与 6 条 Web UI overlay commands。
- toolbar occupied 不超过 available，overflow 始终存在。
- framebuffer 顶部与右侧区域 near-black ratio 均为 0；非最大化标准场景的 Hierarchy dock 也通过区域检查。

## 自动化验证

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore` | 32 projects；0 warning / 0 error |
| Hosting 全量 | 796 passed / 7 个显式环境条件 skipped / 0 failed；TRX: `artifacts/ui004-gameview-responsive-toolbar-d5f9ae1b/test-results/hosting/hosting.trx` |
| Editor 全量 | 117 passed / 0 failed；TRX: `artifacts/ui004-gameview-responsive-toolbar-d5f9ae1b/test-results/editor/editor.trx` |
| Demo 全量 | 140 passed / 1 个 native 条件 skipped / 0 failed；TRX: `artifacts/ui004-gameview-responsive-toolbar-d5f9ae1b/test-results/demo/demo.trx` |
| 响应式 resolver / snapshot / tool contract 定向 | 3 passed / 0 failed |
| 真实 desktop GL presentation 矩阵 | 6/6 passed；每场景 100 ticks；含 `360×720` 窄窗口 |
| task catalog / evidence index / PowerShell parser / `git diff --check` | passed |

## 证据边界与剩余条件

- 窄窗口 framebuffer 证明 overflow button 可见且布局不越界；Resolution/Scale/Maximize 菜单内容与输入阻断由同提交代码和测试约束，但本轮没有把脚本动作写成人工鼠标点击证据。
- 当前自动化桌面仍为 `1024×768`、96 DPI；未覆盖 150%/200% 物理 DPI 与不同 DPI 显示器跨屏。
- 固定 `1920×1080` 仍是 presentation texture + Fit 显示验证，不是 1920×1080 物理 surface。
- ManagedFallback/RmlUi 人工输入与 IME 对照、外部 reviewer 最终体验复核仍未关闭。
- 新实现提交进入 canonical 记录后，需要再次从 detached clean worktree 刷新并独立验证仓库根 `最终输出/`。
