# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 03：Hierarchy / Inspector

taskIds: `EDITOR-003`
implementationCommit: `3de287d87a915a690a12ee1ad724417899aadaff`
baseImplementationCommit: `36b14c6fa02a4ba2e19eeecb3a2728417fcf01a6`
runSessionId: `local-20260712-editor003-unity-parity-cycle03`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮以同机 Unity 6.5 `6000.5.3f1` 的 Hierarchy 与 Main Camera Inspector 为直接参照，完成 PixelEngine 已支持 2D authoring 表面的 Hierarchy / Inspector 收敛。对象搜索、场景根、对象图标、Scene Visibility、Scene Picking、active + name header、2D Transform、Behaviour component、Add Component 与组件启用均已具备真实行为；没有为视觉相似伪造 PixelEngine 尚不支持的 Tag、Layer、Static 或 3D Z。`EDITOR-003` 继续保持 `[~]`：Project / Console / Project Picker、Windows Graphics Capture 白屏、DPI/resize/dock、IME/drag-drop，以及完整 author→play→edit→build→run 真实输入路线仍需后续循环。

## 同机 Unity 参照与语义纠偏

Unity Hierarchy 的左侧两列分别是只影响编辑态的 Scene Visibility（眼睛）与 Scene Picking（手），GameObject active 则位于 Inspector header。首个实现提交曾把眼睛映射到可落盘 `Enabled`；同机复核发现该映射虽然“有行为”，但语义错误，因此没有作为完成结果保留。后续提交 `3de287d8` 将两者拆开：Hierarchy 两列使用非落盘编辑器状态并真实控制 Scene View；Inspector active 独立保存到 `.scene` / prefab 并影响 runtime Behaviour。

## 本轮实现

- Hierarchy 改为紧凑 `+` 菜单、真实名称搜索、可折叠场景根、对象类型图标、选中/禁用/隐藏反馈，并保留拖放、右键菜单、rename 与共享 selection。
- Scene Visibility / Picking 提供全局列头与逐对象开关；父级状态递归覆盖子级。Visibility 真实移除 authoring marker/procedural preview，Picking 保留绘制但阻断鼠标命中与 gizmo。
- visibility/picking 不写入 `.scene` / prefab、不置 dirty、不改变 GameObject active，并使用独立 `SceneViewVersion` 刷新 preview，避免纯编辑器状态重建 runtime script projection。
- Inspector 使用 active checkbox + name header；name 连续输入只形成一次 Undo；Transform 使用 Unity 式 component header 与 Position X/Y、Rotation Z（角度显示、弧度存储）、Scale X/Y 紧凑字段。
- Behaviour 使用短类型名、可折叠灰色 component header、独立 enabled checkbox、右键 Move Up / Move Down / Remove 和左 label / 右 value 字段布局；Add Component 使用居中按钮与可搜索 popup。
- GameObject `Enabled` 新增 `.scene` / prefab 往返与旧场景缺字段默认启用兼容；runtime materialization 递归考虑父级 active，禁用父级会禁用子级 Behaviour。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 命令 | 结果 |
|---|---|
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --no-restore -v:minimal --blame-hang-timeout 3m --blame-hang-dump-type none` | 668 passed；4 个显式环境 smoke skipped；0 failed；Blame 确认全部测试完成 |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --no-restore --filter "FullyQualifiedName~SceneAuthoringPreviewTests|FullyQualifiedName~GameObjectInspectorPanelTests|FullyQualifiedName~RuntimeProjectionBakesHierarchyAndBindsStableIdsVector2AndMaterialId|FullyQualifiedName~LegacySceneWithoutGameObjectEnabledDefaultsToActive"` | 26/26 passed |
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release -m:1 --no-restore` | 105/105 passed |
| `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release -m:1 --no-restore` | 110 passed；10 个显式 GL/ANGLE 环境 smoke skipped |
| `dotnet build PixelEngine.sln -c Release -m:1 --no-restore` | 0 warning；0 error |
| `pwsh -NoProfile -File tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | 两个实现提交均 passed；分别只含本轮 11 个与 7 个实现/测试/设计文件 |

## 真实窗口输入与 framebuffer 复核

同机 Unity 参照窗口：`BallWorld - Untitled - Windows, Mac, Linux - Unity 6.5 (6000.5.3f1) <DX12>`，真实激活并复核 Hierarchy 眼睛/手/Name 列、Inspector active/name、Transform/Camera component header 与 Add Component 层级。

PixelEngine 使用 `computer-use` 激活 `PixelEngine Editor - PixelEngine Demo - Lava Mine`，先点击 Hierarchy 的 `LevelDirector`，再以内部 capture-frame 保留选中联动结果：

- `$env:TEMP\pixelengine-editor003-inspector-v3f.bmp`
- 1280×720，3,686,454 bytes
- SHA256 `3B79669886029D095DEE21B3DBE7371A7534AED3D73C2B3F008B05A14B4FF5C2`
- 复核：眼睛/手/Name 列对齐；LevelDirector 选中同步 Inspector 与 Scene gizmo；Transform 与 Behaviour header、组件 enabled checkbox 均无重叠或裁切。

随后在真实窗口坐标 `(472, 160)` 点击 `LevelDirector` 的 Scene Visibility 眼睛并等待一帧，再正常 `Alt+F4` 关闭以写出最终帧：

- `$env:TEMP\pixelengine-editor003-scene-visibility-v3g.bmp`
- 1280×720，3,686,454 bytes
- SHA256 `04360BFE6E247BC28933DC544F5ED5F2B864B7656EA3DF99D61017531A91FFCC`
- 复核：父级眼睛显示 crossed 状态，子级在 Hierarchy 中保留但变暗；Scene View 从 `LevelDirector procedural preview · 640×360` 变为无 marker 的 `object bounds · 320×180`；Inspector 因没有对象选择显示空态。该结果与自动化的父级递归、非落盘、不可拾取断言相互独立。

Windows Graphics Capture 对 PixelEngine client 仍只返回白色/黑色组合面，内部 framebuffer 正常；该差异未被本轮截图掩盖，继续作为后续窗口系统阻塞项。

## 下一轮差异

- Project Window 仍混用中文操作按钮，目录树/资产网格、breadcrumb、搜索过滤、缩略图与 selection density 尚未达到 Unity 的层级。
- Console 的等级 toggle、collapse/clear、列表/详情分栏、计数和双击定位仍需真实输入对标。
- Project Picker 的首次启动信息架构、recent project 卡片、创建/打开路径和错误恢复仍需收敛。
- Windows Graphics Capture 白屏、150%/200% DPI、连续 resize、dock/undock、键盘焦点、IME 与 drag/drop 尚未闭环。
- Play/Pause/Step、Undo/Redo、Scene/Game 切换、外部脚本、Prefab、Settings 与 Build And Run 仍需以同一真实工程完整复走。
