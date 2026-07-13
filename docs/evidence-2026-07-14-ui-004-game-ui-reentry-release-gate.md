# 2026-07-14 UI-004 游戏 UI 重入契约与正式输出门禁验真

taskIds: `UI-004`
implementationCommits: `61124f3dab959a43e73b9f32c9c3fbca8207cc8a`, `afac054336d842adc379d23a4502ee95fad859ad`
runSessionId: `local-20260714-ui004-game-ui-reentry-release-gate`
evidenceState: `local_game_ui_reentry_and_release_gate_complete_clean_final_output_pending`

## 结论

产品 UI 改为“默认只显示主菜单”后，Game View 的 Play→Stop→Play 正确生命周期已经从旧的 `2→0→2` 变为 `1→0→1`。commit `61124f3d` 修复仍以 `second_stack >= 2` 判断成功的过期 Editor probe：现在会记录首次 stack，要求首次 Play 恰好一个主菜单 screen、Stop 后为 0、第二次 Play 恰好恢复为 1，并继续要求控制器 enabled/无 fault、玩家视觉存在和 presentation 同步。

这不是放宽探针。新的 unit contract 明确拒绝 `2→0→2`，六场景脚本也分别断言 `first_ui_stack_depth=1` 与 `second_ui_stack_depth=1`。commit `afac0543` 进一步把该六场景矩阵接入 `update-final-output.ps1`：正式输出现在必须使用已经 publish 的 Editor 跑完 16:9、4:3、portrait、固定 1920×1080、Maximize On Play 与 360px 窄工具栏，报告和六张 framebuffer 全部进入 manifest、`_验证记录/` 与根级 `SHA256SUMS`；独立 verifier 会重新核对同一 commit、关键摘要字段与每张 framebuffer SHA256，失败时不会替换旧目录。

`UI-004` 继续保持 `[~]`。本报告闭合本地 Game View UI stack 重入语义和正式输出门禁实现，不替代物理鼠标键盘、150%/200% DPI、跨屏与外部 reviewer。仓库根 `最终输出/` 需要在本 evidence 提交后从该提交的 detached clean worktree 重新生成，旧的 `ef49e9bf` 产物不能作为当前 HEAD 的完成证据。

## 发现过程

`ef49e9bf` 的 detached clean-worktree 发布链本身已通过 native build、Editor 默认工作台、中文路径 RmlUi Player 和 341 项 checksum；但随后用正式目录中的 packaged Editor 手动补跑 Game View 矩阵时，第一个 16:9 场景返回：

```text
completed=False
first_play_exited=True
exit_ui_stack_depth=0
second_play_entered=True
second_ui_stack_depth=1
second_controller_enabled=True
second_controller_faulted=False
presentation_synchronized=True
second_play_ui_restored=False
```

失败日志：`artifacts/ui004-final-output-ef49e9bf/editor-gameview-packaged/aspect-16-9/stdout.log`，SHA256 `6a48e55ba6edf9565882cc075ce7ca161544421688f53a877f37bc5e9fd12ee4`。所有运行态信号都正确，唯一冲突是探针仍要求两个 screen；这证明产品修复没有回归，同时证明原发布链只跑默认工作台、不跑 packaged Game View，存在证据盲区。

## 提交绑定六场景矩阵

命令：

```pwsh
pwsh -NoProfile -File tools/run-editor-gameview-presentation-probe.ps1 `
  -EditorExecutable apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe `
  -ProjectRoot demo/PixelEngine.Demo `
  -OutputRoot artifacts/ui004-game-ui-reentry-61124f3d `
  -WindowTicks 100 -TimeoutSeconds 240
```

原始报告：`artifacts/ui004-game-ui-reentry-61124f3d/report.json`；`gitCommit=61124f3dab959a43e73b9f32c9c3fbca8207cc8a`，`allPassed=true`，SHA256 `bbafc8956c97a916d74f60e4073e7b9fe238611d645b600af4bf6e4220f62ac0`。

| 场景 | Presentation | UI stack | Toolbar | Framebuffer SHA256 |
|---|---:|---:|---|---|
| 16:9 | `519×292` | `1→0→1` | Full / fits | `78431a4ac6f4411a4fd1d84b56277d22ff4e193de64eae4020b3a16a35e5eee3` |
| 4:3 | `519×389` | `1→0→1` | Full / fits | `2812fe2d7d11ce1a897139fee8c634c6d12a2af66b79170f25f613442abd694d` |
| 9:16 | `320×568` | `1→0→1` | Full / fits | `8a803416c66a00fa0fd4dc9aeb916e760e68d8a719065f76d65f64708e9cc7cd` |
| Fixed 1920×1080 | `1920×1080` | `1→0→1` | Full / fits | `50c1f2fd219266a9ff9775f4c1d4610b7e2e2713d2b5a086c6ca752995d2c970` |
| Maximize On Play | `1280×720` | `1→0→1` | Full / fits | `d46b9aa4cd1a9754925808858a59b4b9980925a4b751b3abf80ade6fc6825f23` |
| Narrow toolbar | `174×98` | `1→0→1` | Narrow / fits / overflow visible | `025496cb6b3da5966b38cbf80add0ea1f9b89f3c23fbd31ebc87a500beadbf48` |

六个场景都再次确认：玩家 `X=51.000→126.542`，world visual 与 runtime overlay command 均大于 0；Stop 后 UI stack 为 0；第二次 Play 的 `GameUiDemoController` found/enabled 且无 fault；presentation revision、world content rect、viewport texture 与 toolbar snapshot 同步；framebuffer 顶部、右侧和 dock 区域检查无空白假阳性。

## 正式输出门禁

新的更新链顺序为：

1. pinned native build；
2. Editor publish；
3. packaged Editor 默认工作台与项目 build probe；
4. 同一个 packaged Editor 的六场景 Game View presentation probe；
5. 中文路径 Player build 与 80 tick RmlUi 窗口 probe；
6. 组装 manifest/checksum，独立 `verify-final-output.ps1`；
7. 所有步骤通过后才原子替换 `最终输出/`。

manifest 新增 `editorGameViewPresentationProbe`，固定 `scenarioCount=6` 与 `uiStackLifecycle=1->0->1`。verifier 不只相信这两个布尔/字符串：它会解析报告，拒绝场景缺失/重复/未知、UI stack 漂移、控制器 fault、presentation 不同步、toolbar 越界、framebuffer 路径逃逸和 SHA256 不匹配。

## 自动化验证

| 验证 | 结果 |
|---|---|
| UI stack lifecycle unit contract | 5 passed / 0 failed；明确拒绝 `2→0→2`、退出残留与第二轮缺失/多余 screen |
| Game View 提交绑定真实窗口矩阵 | 6/6 passed；每场景 100 ticks；报告见上 |
| Solution Release build | 32 projects；0 warning / 0 error |
| Hosting 全量（probe contract 实现等价工作树） | 801 passed / 7 个显式环境条件 skipped / 0 failed；TRX SHA256 `c91cb60bf05b25faed53c29be7132f99871d0f2dfd7b22905bccd7919280dbd0` |
| `FinalOutput*` verifier/update 合同 | 14 passed / 0 failed |
| PowerShell parser / task catalog / `git diff --check` | passed |

负向 verifier 测试会构造最小正式输出并把首场景 `first_ui_stack_depth` 从 1 篡改为 2；即使 manifest 仍自报 `allPassed=true`，独立审计也必须失败并给出 `expected=1 actual=2`。

## 证据边界与剩余条件

- 六场景输入来自引擎脚本化 probe，不冒充人工鼠标点击或键盘手感验收。
- 当前 active desktop 为 `1024×768`、96 DPI；固定 1920×1080 是 presentation texture + Fit 验证，不是 1920×1080 物理 surface。
- 不同 DPI 显示器跨屏、150%/200% 物理 DPI、IME 人工对照和外部 reviewer 仍未关闭。
- 新发布门禁必须在本 evidence 提交的 detached clean worktree 中实际跑通、再独立验证并替换根 `最终输出/`；本报告不提前把尚未执行的最终替换写成通过。
