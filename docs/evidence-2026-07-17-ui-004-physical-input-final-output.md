# UI-004 物理输入、中文发行与 clean final-output 证据

## 结论

本证据绑定实现提交 `a3716e86e9013e0721ec3dbbc57b5c57bb22bee7`。该提交完成 UI-004 在当前 Windows 主机上可独立复验的工程与发行边界：打包后 RmlUi 中文内容无 replacement character，ManagedFallback 与 RmlUi 运行时 UI 均可由真实 Win32 `SendInput` 点击，帧间快速 down/up 不再丢失，detached clean worktree 生成并独立验证了仓库根 `最终输出/`。

本证据不关闭 200% 物理 DPI、可信物理显示器跨屏或独立 reviewer。当前系统同时加载 Parsec Virtual Display、Meta Virtual Monitor 与 OrayIddDriver，不能把枚举到的第二桌面冒充物理跨屏验收。

## 实现边界

- `RenderWindowUiInputSource` 用固定容量环形队列保存窗口事件泵中的按钮边沿；队列满时只合并尾部到最终权威状态，正常证据要求 `coalesced=0`。
- `UiInputRouter.Pump` 每帧只读取一次指针快照，避免同帧第二次读取提前吞掉 release；Editor 在 Edit/隐藏 Game View 时仍推进底层快照，防止旧点击进入下一次 Play。
- 窗口失焦排入按钮释放，输入源释放时解除键盘、鼠标和焦点订阅；按钮坐标复用 `OrderedPointerPosition` 保持 Silk 事件交付顺序。
- 物理 helper 先用 `SetForegroundWindow`，必要时才用标题栏激活 fallback；目标点击固定提交一对 INPUT，并在 down 后任何异常路径都尝试 mouse-up。
- Player 以 ready 文件发布真实 HWND，产品 UI 挂载前不点击；物理证据完成后稳定 30 帧再退出。Editor readiness 由公共 CLI 的 runtime data 与两次已校验 `game.capture` 判定，不使用固定启动睡眠冒充 ready。
- Demo 主菜单和设置页使用 ManagedFallback/RmlUi 都可解释的确定位置，消除文字重叠；正式目录 211 个文本文件未发现 U+FFFD。

## 自动化与构建

| 门禁 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release -p:TreatWarningsAsErrors=true` | 0 warning / 0 error |
| `PixelEngine.Editor.Automation.Tests` | 126 passed / 0 failed |
| `PixelEngine.UI.Tests` | 158 passed / 12 显式环境 skip / 0 failed |
| `PixelEngine.Rendering.Tests` | 197 passed / 27 显式环境 skip / 0 failed |
| `PixelEngine.Editor.Tests` | 133 passed / 0 failed |
| `PixelEngine.Demo.Tests` | 150 passed / 1 显式环境 skip / 0 failed |
| `PixelEngine.Hosting.Tests` 完整 TRX | 945 passed / 7 显式环境 skip / 0 failed，exit 0 |
| task catalog / PowerShell AST / whitespace / diff | 全部通过 |

完整 Hosting TRX 的本次本地运行目录为 `artifacts/ui004-hosting-full-20260717-055414`；该临时目录不是唯一证据，关键结果、命令和发行 hash 已固化在本文与 `最终输出/_验证记录`。

## 真实物理点击

正式报告：`最终输出/_验证记录/ui004-physical-input/report.json`，SHA256 `096b08279b2311d886ed5e50fdb4a55a1c2033d2264cd9a4490ec58e2689f098`。

| 场景 | 后端 | 结果 | 截图 SHA256 |
|---|---|---|---|
| Player “开始游戏” | RmlUi | press=1、release=1、button calls=2、event=1、pending=0、coalesced=0、action=`252960610` | `bed9dab9eaa26af0d8cf75e350b19b99aa800d90290a27bca944f5d8fa61dd58` |
| Player “设置” | ManagedFallback | press=1、release=1、button calls=2、event=1、pending=0、coalesced=0、action=`534032007` | `34d8982bbf73f8af6c12b6dddb81f22cd08a098fa489ebf5a2667fbb61b7b3c4` |
| Editor Game View “设置” | RmlUi | raw/forwarded press=1、release=1、button calls=2、event=1、runtime controller modal/action 已由 CLI 验证 | `2d591c3b4ee53b55736c34c96c082b32c7cd31e314926e386855c26a7a042ead` |

两条 Player 日志均记录 `dpi=144`、client `1080x720`、presentation `1080x720`、RmlUi active/no fallback，证明当前 150% DPI 主显示器路径。Game View 正式报告包含 16:9、4:3、9:16、固定 `1920x1080`、Maximize On Play 和窄工具栏六场景；报告 SHA256 为 `b148441505b1f2f7c1189d13ba9575624e89f62e4c0ba3b2f2f035b2231f37b4`。

## 正式输出

- source policy：tracked-clean-required；detached worktree HEAD 为 `a3716e86e9013e0721ec3dbbc57b5c57bb22bee7`，Box2D/FreeType/RmlUi 均按 pinned commit 初始化。
- `tools/update-final-output.ps1` exit 0；默认工作台、Game View 六场景、CLI-only automation E2E、中文路径 Demo build/window、三场景物理输入和 staging verifier 全部通过。
- `tools/verify-final-output.ps1 -OutputRoot 最终输出`：`ok=True`、541 checksums、172 capabilities、329 UI commands、42 external CLI processes。
- manifest：`最终输出/_验证记录/manifest.json`，SHA256 `7d57c3d61e215a26720c2676f3ebfb70b61ff0574d97339971e74071e86a613c`。
- checksum index：`最终输出/SHA256SUMS`，SHA256 `0a811d9c9cc31f2f224600d69334f7bb955437262805c273e72436969fda66b0`。
- 旧正式目录在提升后保留为 `artifacts/final-output-backup-20260717-062617`，未参与新目录验证。

## 未闭合条件

UI-004 继续阻塞于以下外部证据，未满足前不得转 `[x]`：

1. 200% 物理 DPI 显示器上的同提交窗口、输入、IME 与截图证据。
2. 可证明为真实物理显示器的跨 monitor 拖移、DPI revision、布局和点击证据；虚拟显示设备不计。
3. 独立 reviewer 对 Scene preview、Game View、三种 Player WindowMode、Play→Stop→Play 与输入手感的当前提交同源验收。
