# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 08：Scene、IME、Build And Run 与停靠输入

taskIds: `EDITOR-003`
implementationCommit: `133967c2c6ff2780e77449182849d0dc384c193f`
baseImplementationCommit: `4087369c82931dcb515a2864360389e7a5614f93`
runSessionId: `local-20260712-editor003-unity-parity-cycle08`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮关闭了默认工作台中四条会直接破坏 Unity 心智模型的真实交互缺口：Scene 普通点击不再隐式画材质；Undo/Redo、Build Settings、Build And Run 等快捷键走全局命令路由且不会被 Brush 快捷键误抢；同一 HWND 上两个 ImGui context 共享 Windows IME 所有权并在切焦点时清理 composition/candidate；Project 与 Inspector 的快速 tab drag 不再因回调重采样未来鼠标坐标或同帧丢失 release 而无法 undock。

真实窗口已经完成 Play→Pause→Step→Edit、中文候选窗定位与取消、Build And Run→独立 Player、Project/Inspector undock→浮动移动→redock、正常退出→同一 UserData 重启恢复、Reset Layout，以及同 DPI 双屏跨 monitor 呈现。Release build 为 32 projects、0 warning、0 error；13 个测试项目合计 1,800 passed、40 个显式环境 smoke skipped、0 failed。

`EDITOR-003` 继续保持 `[~]`。本机两块显示器都为 2560×1440、144 DPI（150%），因此只能证明跨 monitor 和同 DPI 路线，不能证明不同 DPI/200% 的连续移动。Computer Use 的安全边界仍不允许从 Explorer 跨窗口拖入文件；循环 07 的真实 `WM_DROPFILES → Silk → Project → manifest/磁盘/Console` native 链路不能冒充该人工手势。Prefab、Settings、外部脚本编辑和最终全表复走也尚未清零。

## 本轮实现

- Scene 默认回到 Move/selection；Brush 是显式工具，`B` 只在允许的上下文切入，世界坐标与 brush footprint 在写入前按 authoring bounds 裁剪，边界外拖动不会越界或用异常控制流。
- Undo/Redo、Build Settings、Build And Run 使用 ImGui 全局 shortcut route；修正 modifier 冲突，`Ctrl+B` 不再同时激活 Brush。
- Build Settings 将可滚动 scene body 与固定 footer 分离，小停靠区也始终可见 Build/Build And Run；`TryStartBuild` 统一菜单、快捷键和按钮路径，`RunAfterBuild` 只在对应命令成功启动时生效。
- Shell 的 pending panel focus 由目标 panel 在下一帧调用 `SetNextWindowFocus` 消费，菜单和快捷键打开 Build Settings 后可预测地获得焦点。
- `WindowsImeContextController` 按 HWND 共享 IMM32 状态，在多个 ImGui context 间仲裁 text input owner；focus 转移时取消 composition、关闭 candidate 并恢复原 IME context。行为依据 Microsoft 的 [`ImmNotifyIME`](https://learn.microsoft.com/en-us/windows/win32/api/immdev/nf-immdev-immnotifyime) 与 [`IMN_CLOSECANDIDATE`](https://learn.microsoft.com/en-us/windows/win32/intl/imn-closecandidate) 契约。
- Editor 与 Game/UI connector 均以最后一个已按事件顺序投递的 MouseMove 位置作为 button 坐标；只有从未收到 position 时才读取设备快照，避免快速拖拽的 down 起点被后续目标坐标覆盖。
- 完整 down→move→up 若发生在两次 ImGui frame 之间，release scheduler 会把 up 推迟到至少一个可见 press frame；若第二次 down 先到，则先发出待发 up，再发新 down，保持 `press → release → press` 顺序。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64；双显示器均为 2560×1440、144 DPI（150%）。

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore -m:1` | 32 projects；13.18 s；0 warning；0 error |
| Hosting 全套，Release、`--no-build --no-restore -m:1`、normal verbosity 与 blame-hang | 684 passed；5 个显式 native/环境 smoke skipped；0 failed；4.48 min |
| 其余 12 个测试项目逐项目 Release、`--no-build --no-restore -m:1` | 1,116 passed；35 个显式环境 smoke skipped；0 failed |
| UI ordered-pointer / release-scheduler / IME targeted regression | 27/27 passed |
| Hosting source-contract targeted regression | 7/7 passed |
| `pwsh tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | passed；实现提交仅含本轮 32 个设计、实现与测试文件 |

完整逐项目结果：

| 项目 | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| Audio | 50 | 0 | 0 | 50 |
| Content | 8 | 0 | 0 | 8 |
| Core | 30 | 0 | 0 | 30 |
| Demo | 134 | 1 | 0 | 135 |
| Editor | 111 | 0 | 0 | 111 |
| Hosting | 684 | 5 | 0 | 689 |
| Physics | 82 | 0 | 0 | 82 |
| Rendering | 182 | 24 | 0 | 206 |
| Scripting | 93 | 0 | 0 | 93 |
| Serialization | 55 | 0 | 0 | 55 |
| Simulation | 195 | 0 | 0 | 195 |
| UI | 138 | 10 | 0 | 148 |
| World | 38 | 0 | 0 | 38 |
| **合计** | **1,800** | **40** | **0** | **1,840** |

一次直接执行 solution 全套的外层命令在 15 分钟超时前没有给出最终汇总，并留下 Hosting testhost；清理残留进程后，Hosting 独立前台完成，再逐项目完成其余 12 个测试。上表只采用这些有明确项目级终态的结果，不把超时命令记作通过，也不重复计数 targeted regression。

## 真实窗口路线

测试使用隔离 UserData `%TEMP%\pixelengine-editor003-cycle08`，未修改用户默认布局。

| 路线 | 真实结果 |
|---|---|
| Scene → Play → Pause → Step → Edit | 顶栏状态与运行态逐步切换；退出 Play 后回到 authoring；普通 Scene click 不画材质 |
| 中文 IME | 文本控件获得焦点后候选窗出现在 caret 附近；点击 Scene 会取消 composition/candidate；随后 `B`/`W` 工具快捷键恢复 |
| Build Settings → Build And Run | 固定 footer 在小 dock 中可达；构建进度到 100%；完成后启动独立 Player；`Ctrl+B` 未激活 Brush |
| Project tab | 从默认 dock 快速拖出，移动浮动窗口，再停到 Hierarchy tab group |
| Inspector tab | 从默认 dock 快速拖出，移动浮动窗口，再停到 Console tab group |
| 正常退出与重启 | 使用同一隔离 UserData 重启后，Project/Hierarchy 与 Inspector/Console 的 tab group 和 active tab 恢复 |
| Layout > Reset Layout | 恢复默认 Scene、Hierarchy、Project、Inspector 工作台，Inspector 为右侧活动页签 |
| 跨显示器 | 窗口从 primary monitor `15337862` 移到 secondary monitor `48043572`，呈现持续有效；两屏 DPI 相同，不能覆盖不同 DPI route |

重启前保存的 ImGui dock 关系为：

```ini
[Window][Hierarchy]
DockId=0x3,0
[Window][Project]
DockId=0x3,1
[Window][Inspector]
DockId=0x4,1
[Window][Console]
DockId=0x4,0
```

## Windows Graphics Capture 校验

以下均为 Computer Use 从真实 HWND 获取的内存 WGC frame；本轮没有把帧另存为 PNG，因此只记录尺寸、原点和 SHA256，不虚构文件路径。

| 状态 | Frame | SHA256 |
|---|---|---|
| Project undocked | 2560×1392；origin 0,0 | `58998d6ff25deb8978157527d3e5d84c50bcb28e0081a54374b17155d5894c78` |
| Project redocked with Hierarchy | 2560×1392；origin 0,0 | `eb1a588259009ab5c3895fc02f62bee122941fc4f30f7cbe56367ecb46783b86` |
| Inspector undocked | 2560×1392；origin 0,0 | `7bba7c0ca09d82e68495e7473aabb70ffba2339843913e9dc9d1fe5cf811e8d2` |
| Inspector redocked with Console | 2560×1392；origin 0,0 | `b4509ef933ea7855a44365be70504dc638f83fd5b22dfab444902b201b0d8207` |
| Same-UserData restart restored | 2528×1401；origin 48,5 | `73fb1c3206e869dca2bbb4bb1194c954bb6230ec0bcd3195ba99d4a7d41db4a0` |
| Reset Layout | 2528×1401；origin 48,5 | `184199b426dc72d29d9b646f6d1f912e85dc07f6646a279ccbf86074655b7b2b` |
| Cross-monitor span | 2075×1281；origin -732,239 | `9c47ece793f8a5a038cec21e34cc6923fb592262edcbd2ea873c49f125a545a0` |

## Unity 6.5 差异矩阵

| Unity 心智模型/路线 | 本轮状态 | 结论 |
|---|---|---|
| 普通 Scene selection 与显式 Brush | 已闭合 | 默认 Move，Brush 需明确选择；快捷键和边界写入已回归 |
| Play/Pause/Step 与退出 Play | 已闭合 | 真实窗口完整执行，状态与反馈可理解 |
| 全局 Undo/Redo 与 Build shortcuts | 已闭合 | active widget 不吞命令，modifier 不串到 Brush |
| 中文 composition/candidate 与切焦点清理 | 已闭合于当前 150% 环境 | 真实候选窗、caret anchor、Scene focus cancel 均通过 |
| Build Settings 与 Build And Run | 已闭合 | 小 dock footer 可达，构建后独立 Player 启动 |
| Project/Inspector undock、move、redock | 已闭合 | 两个核心 tab 均真实通过 |
| 退出重启 persistence 与 Reset Layout | 已闭合 | 自定义 group 恢复；Reset 回到 Unity-like 默认工作台 |
| 同 DPI 双屏移动 | 已闭合 | monitor handle 确认变化且 WGC 持续有效 |
| 200% 或不同 DPI 双屏连续移动 | **未验证** | 本机没有不同 DPI 目标屏，保留硬件缺口 |
| Explorer→Project 人工跨窗口拖入 | **未验证** | Computer Use 安全边界禁止；循环 07 只证明 native OS 链路 |
| Prefab、Settings、外部脚本编辑与失败恢复 | **待最终复走** | 既有实现存在，但本轮没有把整条产品路线重新验收 |
| 与 Unity 6.5 的最终全表清零 | **未完成** | 继续执行后续对标循环，`EDITOR-003` 保持 `[~]` |

## 下一轮差异

- 在具备不同 DPI/200% 显示器的环境验证拖跨屏幕、最大化、resize、IME caret 与 dock 浮窗的连续坐标切换。
- 在允许跨窗口 pointer drag 的人工环境完成 Explorer 文件/目录→Project，并同时核对 footer、Console、manifest 与磁盘。
- 逐项复走 Prefab、Project Settings、Preferences、外部脚本编辑、错误恢复和 720p 标签溢出；任何可复现差异继续修复，不因本轮路线通过而关闭任务。
- 完成 Unity 6.5 与 PixelEngine 最终差异矩阵、独立人工 reviewer 和完整 author→play→edit→build→run 后，才可把 `EDITOR-003` 改为 `[x]`。
