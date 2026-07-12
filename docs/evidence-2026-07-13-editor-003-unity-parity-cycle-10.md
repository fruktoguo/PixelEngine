# 2026-07-13 EDITOR-003 Unity 6.5 对标循环 10：交互、Game UI、Scene 工具与素材预览

taskIds: `EDITOR-003`
implementationCommit: `924edafe4ea70b80a343ef5f662fe3ad54c234d4`
baseImplementationCommit: `7036e86fdbad74d03b2a0b26ff3c83ea6025a58b`
runSessionId: `local-20260713-editor003-unity-parity-cycle10`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮闭合了用户在实际编辑 Demo 时发现的八组问题：Scene 内容不再越过工具栏；Play→Stop→Play 可以重复进入且 Game UI 生命周期重建；runtime UI 只合成在 Game View 内；Transform 数值支持 Unity-like label drag；组件 enable checkbox 与折叠箭头命中区分离；Player/Goal 可用 W/E/R Scene gizmo 修改；Brush 变成 Scene 内可浮动、左/右停靠的工具面板；Project 选择纹理、音频、脚本后提供类型化预览和音频试听。

主动巡检又修复了三类未在原始清单中的问题：Window 菜单会聚焦已显示 panel 而不是反向隐藏；默认窄 Project dock 自动切换单栏导航；系统标题栏关闭也必须经过 dirty-scene Save / Don't Save / Cancel。验收阶段还发现并消除了菜单布局探针的假绿、工具链测试的 MSBuild stdout/stderr 管道死锁，以及新增 telemetry UI Screen 后的陈旧 Project 预览断言。

真实 Windows 输入已复走 Transform label drag、Player Scene gizmo、Project 图片/音频/脚本预览与试听、Reset Layout、两轮 Play→Stop→Play，以及 dirty scene 的系统关闭 Cancel / Don't Save。脚本化真实窗口探针进一步覆盖 Hierarchy 命令、Preferences、Play/Pause/Step、关闭工程并重开、Game View 玩家移动与 UI stack 重建、菜单布局、默认工作台脚本热重载和 build-player。最终 Release build 为 32 projects、0 warning、0 error；13 个测试项目为 1,835 passed、40 个显式环境 smoke skipped、0 failed。

`EDITOR-003` 继续保持 `[~]`。本轮关闭了当前机器上可复现的交互问题并可生成干净最终输出，但没有不同 DPI/200% 目标显示器，也不能越过 Computer Use 安全边界伪造 Explorer 跨窗口 pointer drag；这些外部环境和独立 reviewer 条件仍按 canonical task 保留。

## 实现提交

| Commit | 内容 |
|---|---|
| `7ac5385a` | Scene 画布 clipping、W/E/R 2D gizmo、Local/Global、Inspector Transform label drag、组件 header 命中区 |
| `b2fe1764` | Step 延迟到 ImGui frame 后、Play 重入脚本/UI 重绑定、Game UI viewport overlay 与 telemetry Screen |
| `dc1e3fa8` | Brush 从独立全局窗口迁入 Scene 浮动/左右停靠 overlay，并修复 active-but-hidden 状态 |
| `01784bc3` | Project 纹理尺寸/缩略图、WAV metadata/真实试听、脚本文本预览与选择版本缓存 |
| `d2e6bde7` | Window 菜单显示并聚焦 panel；窄 Project dock 单栏导航与响应式 Preview |
| `96baecfb` | Silk native Closing 立即接入 dirty guard，避免窗口在下一帧前已被销毁 |
| `584145ce` | 菜单布局探针按 stable id 和相对计数验证删除，并增加整体 succeeded 判据 |
| `1fcbe4a5` | 工具链 `dotnet run` 禁用可复用 build server，并发读取 stdout/stderr，消除管道 EOF 死锁 |
| `924edafe` | Project 语义预览断言同步 8 个 Screen / 8 个 preload，并验证 telemetry id |

## 自动化验证

本机：Microsoft Windows 11 专业版 build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 验证 | 结果 |
|---|---|
| `pwsh -NoProfile -File tools/run-tests.ps1 -Configuration Release` | 32 projects；0 warning；0 error；13 个测试项目 1,835 passed / 40 skipped / 0 failed |
| `--scripted-hierarchy-probe` | create/parent/cycle reject/duplicate/rename/reparent/select/delete 全部 `True` |
| `--scripted-gameview-probe` | X `51.000→126.542`；6 visual + 6 overlay；Stop stack=0；第二次 Play stack=2；controller enabled、not faulted |
| `--scripted-menu-layout-probe` | `succeeded=True`；10 个必要 panel、Reset、create/duplicate/rename/delete、新场景、重开原场景全部通过 |
| `--scripted-default-workbench-probe` | project/script/hot reload/Behaviour attach/save/Play/Stop/build-player 全部通过；player zip 生成 |
| `--scripted-preferences-probe --window-ticks 8` | Preferences 在无工程态可见；100% scale；隔离临时用户状态 |
| `--scripted-probe --window-ticks 60` | Play/Pause/Step/Resume/Edit/Save/Close Project/Reopen 全部 `True` |
| managed native leak detector 聚焦复跑 | 修复前 3 分钟 hang；修复后 GL/OpenAL/Box2D/ALC + preflight 12 秒通过 |

完整逐项目终态：

| 项目 | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| Audio | 50 | 0 | 0 | 50 |
| Content | 8 | 0 | 0 | 8 |
| Core | 30 | 0 | 0 | 30 |
| Demo | 137 | 1 | 0 | 138 |
| Editor | 118 | 0 | 0 | 118 |
| Hosting | 706 | 5 | 0 | 711 |
| Physics | 82 | 0 | 0 | 82 |
| Rendering | 182 | 24 | 0 | 206 |
| Scripting | 96 | 0 | 0 | 96 |
| Serialization | 55 | 0 | 0 | 55 |
| Simulation | 195 | 0 | 0 | 195 |
| UI | 138 | 10 | 0 | 148 |
| World | 38 | 0 | 0 | 38 |
| **合计** | **1,835** | **40** | **0** | **1,875** |

第一次全量命令在 Hosting 内超过 20 分钟且没有汇总；blame-hang 把唯一活动用例定位到 `ManagedNativeLeakDetectorWritesManifestAcceptedByNativeLeakPreflight`。进程树证明 `dotnet run` 主进程已退出，而可复用 MSBuild node 仍持有重定向 writer，使 `ReadToEnd()` 等不到 EOF。修复后该用例 12 秒通过。第二次全量暴露 telemetry Screen 由 7 增至 8 后的陈旧断言，修正并聚焦通过。上表只采用第三次完整命令的明确终态，不把超时或含失败的前两次命令冒充通过。

## 真实窗口路线

| 路线 | 真实结果 |
|---|---|
| Scene toolbar / canvas | authoring world 从工具栏下方开始；向上平移/自动 framing 不再覆盖 toolbar |
| Player / Goal Scene 编辑 | Hierarchy 选择后 Scene 显示 gizmo；W/E/R 切 Move/Rotate/Scale，修改立即反馈到 marker 与 Inspector |
| Transform label drag | LevelDirector Position X 从 `0` 拖到 `6.250`；标题出现 dirty `*`，Scene marker 同步移动 |
| Inspector component header | fold arrow、enable checkbox、组件名为三个独立命中区域；窄栏轴字段自动分行 |
| Brush overlay | 工具只存在于 Scene canvas；可浮动、左停靠、右停靠、关闭并重新打开，不再创建 OS 独立窗口 |
| Project 图片 | 选择 PNG 后显示真实缩略图与像素尺寸 |
| Project 脚本 | 窄 dock 显示 `格式: CS`、源码片段与水平滚动条 |
| Project 音频 | 显示 WAV 时长/采样率和完整 `▶ 试听`；点击后真实 AudioSystem 播放并更新状态栏 |
| Play→Stop→Play | 两个完整循环均成功；Game UI 始终限制在 Game View，Stop 后消失，第二次 Play 完整恢复 |
| Window 菜单 / Reset Layout | 已显示 panel 获得焦点而不是被隐藏；Reset 恢复 Scene、Hierarchy、Project、Inspector、Console |
| 系统标题栏关闭 dirty scene | 第一次 Alt+F4 显示 Save / Don't Save / Cancel；Cancel 保留窗口和 dirty；第二次 Don't Save 关闭且未写盘 |

## Framebuffer 证据

两张图均由当前 Release Editor 的 `--capture-frame` 从真实 OpenGL framebuffer 写出；文件保留在本地 `artifacts/`，不作为源码提交。

| 状态 | Frame | Bytes | SHA256 |
|---|---|---:|---|
| Edit 默认工作台 | 1029×720 | 2,960,694 | `3b9d69a323a7c5cf95e5ba17bf1af26bce807c4883a1998f44be54ab9f95254c` |
| 第二次 Play 的 Game View/UI | 1029×720 | 2,960,694 | `4993c14db06287418c3fea621998207156fcc5491f6402e3b8a887e1a1193a57` |

Edit frame 可见 Scene 内容位于 toolbar 下、Hierarchy 三个对象、窄 Project 单栏导航与右侧 Inspector/Console tab。Game View frame 可见 runtime world 和 HUD/menu 只合成在 Game View 图像矩形内；右侧 Editor panel 没有被 runtime UI 覆盖。

## 主动巡检结论

| 风险 | 结果 |
|---|---|
| panel 菜单二义性 | 已修复为 show + focus，不再 toggle-hide |
| Project 默认窄栏 | 已修复为 `<420px` 单栏，保留 breadcrumb、folder item 与底部预览 |
| OS close 绕过 dirty guard | 已在 native Closing callback 当帧 cancel 并请求受保护退出 |
| 菜单探针假绿 | 已新增 `Succeeded` 聚合判据；删除按 stable id 与相对计数验证 |
| 测试子进程永久等待 | 已禁用 build server/node reuse 并并发排空 stdout/stderr |
| 新 UI Screen 与 Project 预览漂移 | 8/8 精确计数与 telemetry id 已纳入回归 |
| TODO / stub / 占位实现 | Editor/Shell 扫描未发现新的产品路径 stub；“不可用”文本均为服务缺失时的显式诊断 |

## 仍保留的 canonical 外部条件

- 本机两块显示器均为 144 DPI（150%），无法证明不同 DPI/200% 跨屏连续坐标路线。
- Computer Use 安全边界不允许从 Explorer 跨窗口 pointer drag；既有 `WM_DROPFILES → Silk → Project` native probe 不能冒充人工手势。
- `EDITOR-003` 的最终关闭仍需独立人工 reviewer 和完整 Unity 差异矩阵；本轮不把这些外部条件伪造为完成。
