# 2026-07-14 EDITOR-003 Unity 6.5 对标循环 13：Preferences 信息层级与字体密度

taskIds: `EDITOR-003`
implementationCommits: `bc88a10b617ba6a789ee434ba1c12ef5fe76c316`, `55621f81b447c17f3eb7f4d09ac7477a451a60b9`, `cf13620891fbdb4d44512712e0e6a9598e44ff4d`
runCommit: `cf13620891fbdb4d44512712e0e6a9598e44ff4d`
runSessionId: `local-20260714-editor003-unity-parity-cycle13`
evidenceState: `local_preferences_font_density_complete_task_active_external_input_dpi_review_pending`

## 结论

本轮重新打开 `EDITOR-003` 的本地实现工作：真实 Preferences 窗口仍存在 label/value 混在同一视觉层级、英文界面混入中文诊断、以及 18px 基准字体明显大于 Unity 的可复现差异，因此不能继续声称“本地问题已清零”。Appearance、General、External Tools 与 Shortcuts 现在统一使用响应式 label/value 表格，label 背景和纵向分隔线明确区分说明与值；窄窗口和高 UI Scale 会压缩 label 预算而保留 value 区，不再让字段值被长标签挤没。

Preferences 的硬编码帮助、诊断和动作反馈已纳入 `en-US` / `zh-CN` 本地化。检查中还复现了一个真实文本安全缺陷：英文帮助中的 `150% is` 经过 ImGui `TextWrapped` 的 printf 风格入口后会显示成 `150 2040388176s`。实现改为 `PushTextWrapPos` + `TextUnformatted`，百分号和任意用户可见文本都不再被当作 format string。

字体问题通过同机 Unity 6.5 `6000.5.3f1` 探针定位到“字号与 CJK fallback”，而不是 Inter 文件本身：Unity 默认 Inspector `label`/`textfield` 为 12px，`singleLineHeight=18`、`standardVerticalSpacing=2`、`labelWidth=150`；PixelEngine 旧默认基准为 18px。Unity 与 PixelEngine 的 `Inter-Regular.ttf` SHA256 完全相同，均为 `fc87daef80ebd62ca64506a7bcb999172fcb57f2ab3b022899da2f23fe3cb46c`。PixelEngine 现在把 Shell、Preferences 和已打开工程的 Editor context 统一到 12px 基准；Windows 运行时依 Unity `fontsettings.txt` 优先 `%WINDIR%\Fonts\msyh.ttc`，缺失或非 Windows 时使用发行包内 Noto Sans SC，保留离线和跨平台可启动性。

字号收敛后，Build Settings 的 222px footer 实际只需 201px，四个动作可以全部 inline。旧 runner 仍强制打开已经不存在的溢出菜单，造成 3/3 假失败；探针现按真实 `Inline` / `Overflow` / `AllOverflow` 密度分支，并只在存在 popup 时等待完整一帧。正式提交绑定捕获首轮通过。Game View 六场景矩阵也在同一提交上全部通过，360×720 窄窗口仍正确落入 `Narrow` 并保留 overflow。

## 实现提交

| Commit | 内容 |
|---|---|
| `bc88a10b` | Preferences 四个分类统一响应式 label/value 信息层级；补齐中英文文案；以非格式化包裹文本修复 `%` 被 printf 解释的显示损坏 |
| `55621f81` | 以 Unity 6.5 本机探针校准 12px 基准；Shell/Preferences/工程 Editor context 共用字体设置；Windows CJK 对齐 Microsoft YaHei，发行包保留 Noto fallback |
| `cf136208` | Build Settings runner 服从实际 footer 密度，Inline 不再伪造 overflow 操作；在新字体密度下重跑 Build Settings 与 Game View 正式矩阵 |

## Unity 6.5 同机参照

Unity 安装：`C:\Program Files\Unity\Hub\Editor\6000.5.3f1`，Editor version `6000.5.3f1 (c2eb47b3a2a9)`。临时探针工程位于可再生 `artifacts/unity-font-probe/`，不作为唯一稳定证据；下表把关键输出固化在本报告。

| 项 | Unity 6.5 实测 |
|---|---|
| `EditorGUIUtility.pixelsPerPoint` | `1.0` |
| `EditorGUIUtility.singleLineHeight` | `18.0` |
| `EditorGUIUtility.standardVerticalSpacing` | `2.0` |
| `EditorGUIUtility.labelWidth` | `150.0` |
| `EditorStyles.label.fontSize` | `12` |
| `EditorStyles.textField.fontSize` | `12` |
| Inter 主字体 | `Inter-Regular.ttf`，与 PixelEngine 文件逐字节同 SHA256 |
| Windows 中文 fallback | Unity `fontsettings.txt` 指向 Microsoft YaHei；PixelEngine 运行时采用 `%WINDIR%\Fonts\msyh.ttc`，缺失则回退 Noto Sans SC |

## 自动化验证

本机：Microsoft Windows 11 专业版 build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 验证 | 结果 |
|---|---|
| Preferences / localization / layout 定向 Hosting 回归 | 88 passed / 0 failed |
| 字体资产、两类 Editor context、Build Settings 与 Game View 组合定向回归 | 152 passed / 0 failed |
| `PixelEngine.EditorApp.Tests` Release | 18 passed / 0 failed |
| `dotnet build PixelEngine.sln -c Release --no-restore` | 0 warnings / 0 errors |
| `tools/validate-task-catalog.ps1` | 通过 |

## Preferences 真实窗口捕获

两次捕获均使用 Release desktop GL Editor、隔离 user state、20 个稳定帧，窗口位置 `(24,24)`、窗口尺寸 `980×672`、UI Scale `150%`，并确认分类导航可见。截图已人工检查：英文帧没有中文混入，`150% is` 完整；中文帧采用 Microsoft YaHei，label/value 分界和控件留白完整。

| Locale | Framebuffer SHA256 |
|---|---|
| `en-US`, 150% | `73f3bd8235582ac431c493f750c93a9655f769c6525cb2e005e848a98316aba5` |
| `zh-CN`, 150% | `1aa17e5df9f7b55bb324943e31ebf9536d3eeb0ce2dac51151255079b2ec9d28` |

## Build Settings 提交绑定窗口证据

正式报告：`artifacts/editor-build-settings-probe/cf136208/report.json`（可再生，report SHA256 `2beed92e7e51addfb6bd9bd2b196f93c8ac053abf91d1a8d7aeef0cedc4fd2fa`）。

| 字段 | 结果 |
|---|---|
| Git commit | `cf13620891fbdb4d44512712e0e6a9598e44ff4d` |
| 尝试 | attempt 1/3，`accepted=true` |
| Framebuffer | `1024×720`, SHA256 `8ef134d0ab5393fd04940aaebaac5ab8e929237eb183b96e483c3309926c82d1` |
| Footer | `Inline`; available `222px`; required inline `201px`; all actions accessible |
| 动作可见性 | Build 与 Build And Run 可见；overflow 不存在，符合实际密度 |
| 区域门禁 | overall/chrome/scene/build/right unique colors=`191/280/442/219/170`; near-black=`0`; opaque=`1` |

## Game View 提交绑定矩阵

正式报告：`artifacts/editor-gameview-presentation-probe/cf136208-font-density/report.json`（可再生，report SHA256 `ac13d0dbf06a1a3baeebca8d920a3f6e2d1bed319fc5928ff87a972585c96d8d`）。六场景均绑定 `cf13620891fbdb4d44512712e0e6a9598e44ff4d` 并通过。

| 场景 | Surface | Toolbar density | Framebuffer SHA256 |
|---|---:|---|---|
| `aspect-16-9` | `1024×720` | `Full` | `3be9f166a6ec24dc0c15a688939d5cbefdb3fe0d9292ecb4fbf6c804b2e3e6e1` |
| `aspect-4-3` | `1024×720` | `Full` | `4fe4d20e9589da8960ff7c73cd4b1803e7d0147e7e8a3402fa2e5f3643cc3dca` |
| `aspect-9-16` | `1024×720` | `Full` | `8edb83a7f88759fdfdbfbae60eeb52a1fe8bfe5425a4982aa2c1109b9a9461f4` |
| `resolution-1920-1080` | `1024×720` | `Full` | `4492c4c3f9554ab5c407415a72def2c8c4733b8b8fe245bc081a64b922766800` |
| `maximize-on-play` | `1024×720` | `Full` | `cce7f03bd346b846258e4434a24112c7c3149221a25cea0fe3c11f5cec9a4ac5` |
| `narrow-toolbar` | `360×720` | `Narrow` | `237c920adacc1304e12f492a18d9c7a1c527234cbe1903ec730c746cf652ba43` |

## 边界与下一步

- 本报告证明 Preferences 信息层级、本地化文本安全、Unity 字体基准、Build footer 自适应和 Game View 六场景在提交绑定的 desktop GL 路径上成立；不把静态截图冒充物理鼠标/键盘操作。
- 当前默认工作台仍可见 Inspector/Settings 等表面的硬编码中英文混杂，本地可复现问题尚未清零，因此 `EDITOR-003` 恢复为 `[~]` 并继续修复。
- 不同物理 DPI/200% 显示器跨屏、Explorer→Editor pointer drag、runtime 数值物理拖拽与独立 reviewer 仍是外部证据缺口。
- 仓库根 `最终输出/` 最近一次 clean-worktree 发行身份早于本轮实现，当前明确为 stale；待这一批可复现 Editor 缺陷收敛后，再从最终 docs HEAD 的 detached clean worktree 分阶段刷新并独立验证，避免把频繁小提交误当正式发行。
