# 2026-07-14 EDITOR-003 Unity 6.5 对标循环 12：运行态 Inspector 编辑与窄面板信息层级

taskIds: `EDITOR-003`
implementationCommits: `bbd8947c2fe0e27bc76b73ac59df34fa0166dc43`, `389c9935b59024c0458c2c42a69ad39433493480`, `5ccfbd895323d6e7ed2bdf1cbe1186ba20776e41`
runCommit: `5ccfbd895323d6e7ed2bdf1cbe1186ba20776e41`
runSessionId: `local-20260714-editor003-unity-parity-cycle12`
evidenceState: `local_runtime_inspector_complete_external_input_dpi_review_pending`

## 结论

本轮继续处理真实使用反馈中尚未闭合的 Play Mode Inspector：运行态 Transform、整数、浮点、`decimal` 与 `Vector2/3/4` 不再是旧式输入框或只读文本，而是和编辑态一致的 label/value 表格与 Unity-like 拖拽控件；所有整数宽度保持精确类型，`decimal` 走不会丢精度的提交式文本状态，退出 Play 后临时修改按 session 快照恢复。

窄 Inspector 的 label 列从 36%/128px 调整为 44%/144px 上限，长字段名在单元格内使用 Unicode ellipsis，并保留完整 hover tooltip；Transform 和组件浮点显示采用紧凑 `%g`，不再被无意义的 `.000` 挤占空间。运行态提示在窄列中自动换行，label 背景与 value 控件形成稳定分界。

新增的真实窗口探针不是“选择 entity 即通过”：它必须在 Play 中找到 `PlayerController` 实体，并确认目标实体之后发生新的 Inspector Draw revision、Transform property table、组件 header/property table 和至少一个真正可写且当前值有效的 numeric drag field。正式 runner 同时拒绝空白、纯色、顶部 chrome 缺失、右侧 surface 缺失和 Inspector 区域黑帧；失败会换一份隔离 workspace 重试，任何失败尝试均保留诊断。

本轮没有把桌面自动化失败冒充物理拖拽：Windows 控制可以发现 Editor 窗口，但激活/点击持续超时且 accessibility tree 为空，因此只声明控件已真实绘制、数据转换与写回契约已自动化验证，不声明本轮完成了 runtime 字段的真实鼠标拖拽。`EDITOR-003` 仍保持外部阻塞，解除条件不变。

## 实现提交

| Commit | 内容 |
|---|---|
| `bbd8947c` | Play Mode Transform 与组件 numeric/vector/decimal 编辑，精确类型转换、临时状态和 Stop 恢复；统一运行态 Inspector label/value 样式 |
| `389c9935` | 窄 label 列、长 label ellipsis、紧凑浮点显示、UTF-8 探针输出；真实 Draw revision 与组件控件结构快照 |
| `5ccfbd89` | 隔离 workspace 的 runtime Inspector 真实窗口 runner；结构摘要、区域完整度、截图哈希和最多三次 fail-closed 重试 |

## 自动化验证

本机：Microsoft Windows 11 专业版 build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 验证 | 结果 |
|---|---|
| `PixelEngine.Editor.Tests` Release 全量 | 117 passed / 0 failed |
| `PixelEngine.Hosting.Tests` Release 全量 | 806 passed / 7 skipped / 0 failed（813 total；3m54s） |
| `GameObjectInspectorPanelTests\|EditorShellOptionsTests` | 55 passed / 0 failed |
| `dotnet build PixelEngine.sln -c Release --no-restore` | 32 projects；0 warning / 0 error |
| `pwsh -NoProfile -File tools/run-editor-runtime-inspector-probe.ps1 -OutputRoot artifacts/editor-runtime-inspector-probe/5ccfbd89 -MaxAttempts 3` | attempt 1/3 accepted；commit、结构摘要、BMP 区域与 SHA256 全部写入 `report.json` |

新增或强化的自动化边界：

- byte/sbyte/short/ushort/int/uint/long/ulong/float/double/decimal 的 runtime 写回保持目标类型和范围；
- `Vector2/3/4` 按轴拖拽并拒绝长度不符、非有限值和非 vector 类型；
- `decimal` 编辑中间态不会被每帧 inspection snapshot 覆盖，Enter/失焦时精确提交；
- Play→Edit 恢复临时 runtime 修改，不污染 authoring scene；
- runtime Inspector 探针拒绝错误 handle、缺 Transform 表、缺组件表、缺 numeric drag 或未发生真实 Draw revision；
- 新 CLI probe 独立于 Game View probe，并自动使用 ephemeral user state。

## Commit-bound 真实窗口证据

正式输出：`artifacts/editor-runtime-inspector-probe/5ccfbd89/report.json`。`artifacts/` 是可再生目录，稳定证据以本文记录的 command、run commit、结构字段和 SHA256 为准。

| 字段 | 结果 |
|---|---|
| run commit | `5ccfbd895323d6e7ed2bdf1cbe1186ba20776e41` |
| attempt | `1 / 3`；accepted |
| window/framebuffer | `1024×720`；BGRA32 BMP；2,949,174 bytes |
| Play 状态 | entered=`True`；remained_in_play=`True` |
| runtime entity | selected=`True`；handle=`script:7`；resolved=`True` |
| Transform | property table rendered=`True` |
| Components | headers=`12`；property tables=`12`；numeric drag fields=`85` |
| 其他字段 | vector drag fields=`0`；decimal fields=`0`（当前 Demo behaviours 没有这两类字段；类型行为由单元测试覆盖） |
| Draw revision | `20`，且严格晚于 entity selection 前 revision |
| sampled colors | overall=`225`；chrome=`305`；right surface=`377`；Inspector surface=`244` |
| near-black ratio | chrome=`0.0`；right surface=`0.0`；Inspector surface=`0.0` |
| framebuffer SHA256 | `79b4c4ccb3a4425dff67e023236fc4a95c29a987157d0dd5c50fe5ff4421a6cf` |

截图已目视核对：Scene authoring world、Hierarchy、Project 和右侧 Inspector 均完整；Inspector 中可见换行的 Play Mode 提示、`script:7 · Entity 7`、Transform Position/Rotation/Scale 的彩色轴与 drag value，以及 `PlayerController` 的 label/value numeric fields。长字段以 ellipsis 收敛，没有越过 value 列。

## 字体核对

PixelEngine Editor 使用 `apps/PixelEngine.Editor.Shell/Fonts/Inter-Regular.ttf`。它与同机 Unity 6.5 `6000.5.3f1/Editor/Data/Resources/Fonts/Inter-Regular.ttf` 的 SHA256 完全相同：

`fc87daef80ebd62ca64506a7bcb999172fcb57f2ab3b022899da2f23fe3cb46c`

因此先前“字体不像 Unity”的主要来源不是 font family，而是字号密度、栅格化、列宽、留白和信息层级。本轮通过紧凑数值、label/value 分栏、ellipsis 与窄列换行修正其中可由 Editor 样式控制的部分；没有声称 ImGui 与 Unity UI Toolkit 的 glyph rasterization 完全相同。

## 外部解除条件

- 在不同物理 DPI 的两块显示器（至少一块 Windows 200%）之间拖动窗口，复走坐标、命中、IME 与 layout persistence。
- 使用 Explorer 真实 pointer drag 将文件/目录跨窗口拖入 Project，而不是用平台消息自动化冒充人工手势。
- 在可稳定激活 desktop GL 窗口的环境中，实际拖拽 runtime Transform、整数、浮点和 vector 字段并核对 Stop 恢复。
- 独立 reviewer 按同一 commit 完整复走 Unity 6.5 差异矩阵并签署最终结论。
