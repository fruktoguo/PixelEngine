# 2026-07-13 EDITOR-003 Unity 6.5 对标循环 11：运行态隔离与 200% 响应式收口

taskIds: `EDITOR-003`
implementationCommits: `d2f65ff57d9c75a3f7102f44ff45dc7929e79363`, `6ec279b89640251c9aa11fecb8a5956dbf40d01b`
runSessionId: `local-20260713-editor003-unity-parity-cycle11`
evidenceState: `local_reproducible_work_complete_external_conditions_blocked`

## 结论

本轮补齐 Play/Paused 与 authoring scene 的统一写隔离：Hierarchy 和 Inspector 保留选择/查看但不可修改，Undo/Redo 与所有 authoring command 在命令边界再次门控；重复进入 Play 不会覆盖临时快照，退出后可继续建立全新 session。随后主动巡检并修复 Project Picker 与 Preferences 在 200% UI Scale / 窄窗口下的横向溢出、自定义外部编辑器逐键写盘与空 quoted executable 误解析、以及布局 ini 被占用时 Reset Layout 直接抛异常。

当前机器可复现的本地代码与布局问题已清零。`EDITOR-003` 转为 `[!]`，只保留无法由本机自动化替代的三项外部解除条件：不同物理 DPI（含真实 200%）显示器之间跨屏、Explorer 到 Editor 的人工 pointer drag、独立 reviewer 的最终 Unity 差异矩阵复走。本轮 200% 指 Editor 自身 UI Scale，不冒充物理 200% monitor 证据。

## 实现提交

| Commit | 内容 |
|---|---|
| `d2f65ff5` | Play/Paused authoring 只读 UI、Undo/Redo 统一写屏障、Inspector 连续编辑旁路保护、嵌套 Play session 拒绝与快照来源保持 |
| `6ec279b8` | Project Picker 响应式 header/表格/路径/新建页、Preferences 紧凑导航与自定义命令草稿、Reset Layout 错误恢复、双语文案 |

## 自动化验证

| 验证 | 结果 |
|---|---|
| `GameObjectInspectorPanelTests|EngineExecutionModeTests` | 43 passed / 0 failed |
| `ProjectPickerWindowStateTests|EditorPreferencesTests|EditorShellLayoutTests|HostingProjectDisciplineTests|EditorScriptAssetOpenServiceTests|EditorCodeWorkspaceOpenServiceTests` | 109 passed / 0 failed |
| `EditorAppTests` | 18 passed / 0 failed |
| `PixelEngine.Editor.Shell` Release build | 0 warning / 0 error |
| `tools/validate-task-catalog.ps1` | valid；81 canonical，切换前为 1 active |

一次未过滤的 Hosting 测试在发行工具用例启动外部 `dotnet` 后超过 120 秒并被终止；该次没有终态，且遗留进程已按本轮启动时间清理，因此没有把它计入通过数字。上表只记录获得明确终态的定向回归。

## 真实窗口 framebuffer

四张图均由 Release Editor 的 `--capture-frame` 从真实 Windows 窗口 framebuffer 生成；文件保留在本地 QA 输出目录，不作为源码提交。1280×720 与 800×600 都使用 Editor UI Scale 200%、简体中文、隔离 Preferences / workspace / layout。

| 状态 | Frame | Bytes | SHA256 |
|---|---|---:|---|
| Project Picker 标准窗口 | 1280×720 | 3,686,454 | `daf9df428c80f80eb81f2bad3d69c9a9735b9bbf9469c776dbf4593114a8bcd0` |
| Preferences 标准窗口 | 1280×720 | 3,686,454 | `56851493bf4b7066b56dfd80ae3db811dbda9b813729f5833df127015e1968ac` |
| Project Picker 窄窗口 | 800×600 | 1,920,054 | `8c46e016cd7b564884043ca9baba5ea950f826e3c16c3e81bfbe0e69fd748ed4` |
| Preferences 窄窗口 | 800×600 | 1,920,054 | `1d0ba63e8b4b9330cf4393e9b57e34e25e147b551e38345b36c54997608a47c7` |

窄 Project Picker 中 Search、Add、New project 自动分行，空列表文案换行且操作按钮保持可达；窄 Preferences 中固定侧栏折叠为顶部 Category combo，设置区拥有独立纵向滚动，不再把 label/value 压出窗口。标准窗口保持紧凑横排与侧栏布局。

## 外部解除条件

- 在不同物理 DPI 的两块显示器（至少一块 Windows 200%）之间拖动窗口，复走坐标、命中、IME 与 layout persistence。
- 使用 Explorer 真实 pointer drag 将文件/目录跨窗口拖入 Project，而不是用 `WM_DROPFILES` 自动化冒充人工手势。
- 独立 reviewer 按同一 commit 完整复走 Unity 差异矩阵并签署最终结论。
