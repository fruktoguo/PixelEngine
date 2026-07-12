# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 09：权威像素世界编辑预览

taskIds: `EDITOR-003`
implementationCommit: `06c6100f3f7d835ec02f1f7fb974a902f528ed6d`
baseImplementationCommit: `af964432afcfcdcae39294c4af992f48f6c5b110`
runSessionId: `local-20260712-editor003-unity-parity-cycle09`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮关闭了 Scene View 仍以五块硬编码矩形冒充 Demo 世界的真实性缺口。项目现在必须由 Behaviour 显式实现 `IAuthoringWorldPreviewProvider` 才能加入编辑态世界预览；Editor 不执行任意 `OnStart` / `OnUpdate`，而是通过 Scripting 公开的受限写入契约把确定性铺设结果写入已驻留的权威 Simulation 网格。Demo 的运行时与 authoring 路径复用同一份泛型铺设逻辑，并由逐 cell 等价测试锁定。

Scene View 使用独立的 1 cell = 1 texel BGRA8 纹理和 authoring camera，真实显示 640×360 地形、材质、边界与 marker。相同 provider content hash 的场景重投影只调用 adopt，不清场，因此 Brush 修改和 Play 前快照可以保留；hash、尺寸、provider 集合变化或 provider 移除才触发确定性重建/清场。空 GameObject marker 同时编码 rotation 与非均匀 scale，W/E/R 不再只改变 Inspector 数值而没有 Scene 反馈。Editor 还会在 Scripting 前挂载项目音频；缺少 `Content/audio` 时使用会保留参数校验的显式 `NullAudioApi`，不再因 `Context.Audio` 缺失让 Play 退出。

真实 Windows 窗口已直接显示实际像素地形。`B` 激活 Brush 后以 `empty` 在左侧熔岩区写入 13 个 cell，可见像素洞即时出现；随后 W/E/R、约 18 秒 Play、Stop 均完成，进程未崩溃，Stop 回到编辑态后像素洞仍在。Release build 为 32 projects、0 warning、0 error；13 个测试项目最终为 1,808 passed、40 个显式环境 smoke skipped、0 failed。

`EDITOR-003` 继续保持 `[~]`。本轮没有可用的不同 DPI/200% 显示器，也没有越过 Computer Use 安全边界伪造 Explorer 跨窗口 pointer drag；Prefab、Settings、外部脚本编辑与最终 Unity 差异矩阵仍需后续完整复走。

## 本轮实现

- 新增 Scripting 层 `IAuthoringWorldPreviewProvider`、`IAuthoringWorldEditApi`、descriptor/context；Demo 只依赖公开 Scripting API，未直接引用 Simulation 实现类型。
- `AuthoringWorldPreviewRuntime` 从 Script Scene 捕获启用的显式 provider，以 StableId、类型、尺寸和 provider content hash 生成确定性指纹；同指纹采用现有网格，变化时清理驻留 chunk 后重建，provider 移除时发布空快照。
- provider 描述必须完全落在已驻留 chunk 内；同一场景的多个 provider 必须声明相同尺寸，避免写入一个 Editor 无法完整呈现的世界。
- `SceneWorldTexture` 复用运行时 `RenderBufferBuilder` 生成材质色与温度 glow，通过 `WorldTexture` / PBO 上传独立 BGRA8 纹理，并校验 OpenGL 最大纹理尺寸。
- Brush 只有实际写入 cell 时才使纹理失效；provider 重建、移除和 Play→Edit 快照恢复也会使纹理失效。场景投影版本、Scene View 版本与 authoring snapshot version 共同控制预览缓存。
- `LevelDirector` 的运行时和 authoring writer 统一裁剪到 descriptor bounds；宽高、spawn、goal 与 hazard probe 共同进入 content hash。authoring populate/adopt 将 `_worldBuilt` 同步为真，随后进入 Play 不再清场覆盖画刷结果。
- Editor 在挂载 Scripting 前初始化项目 Audio；有音频目录时使用内容后端，无目录时注入显式无声后端。无声后端仍拒绝空 cue、非有限坐标和负音量。

## 自动化验证

本机：Microsoft Windows 11 专业版 build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1` | 32 projects；32.78 s；0 warning；0 error |
| 13 个测试项目逐项目 Release、`--no-build --no-restore --disable-build-servers -m:1` | 1,808 passed；40 个显式环境 smoke skipped；0 failed |
| `StructuralDamageEntriesDoNotAllocateAfterWarmup` 隔离复跑 | 5/5 passed；随后完整 Simulation 195/195 passed |
| `pwsh -NoProfile -File tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `pwsh -NoProfile -File tools/validate-evidence-index.ps1` | 47 entries；62 referenced task IDs；valid |
| `git diff --cached --check` | passed；实现提交仅含本轮 18 个设计、实现与测试文件 |

完整逐项目终态：

| 项目 | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| Audio | 50 | 0 | 0 | 50 |
| Content | 8 | 0 | 0 | 8 |
| Core | 30 | 0 | 0 | 30 |
| Demo | 135 | 1 | 0 | 136 |
| Editor | 111 | 0 | 0 | 111 |
| Hosting | 689 | 5 | 0 | 694 |
| Physics | 82 | 0 | 0 | 82 |
| Rendering | 182 | 24 | 0 | 206 |
| Scripting | 95 | 0 | 0 | 95 |
| Serialization | 55 | 0 | 0 | 55 |
| Simulation | 195 | 0 | 0 | 195 |
| UI | 138 | 10 | 0 | 148 |
| World | 38 | 0 | 0 | 38 |
| **合计** | **1,808** | **40** | **0** | **1,848** |

第一次串行全套在 Simulation 的 `StructuralDamageEntriesDoNotAllocateAfterWarmup` 观察到一次 5,072 B 线程分配，其他 10 个已执行项目均通过。该测试和本轮修改没有路径交集；失败命令终止后，测试在五个独立进程中 5/5 通过，随后完整 Simulation 195/195 通过，UI/World 也取得明确终态。上表采用这些最终项目级终态，不把第一次有失败的命令冒充通过，也没有为这次非稳定噪声修改 Simulation 产品代码。

新增回归覆盖：

- provider 显式 opt-in，组件类名不能隐式启用预览；
- content hash 相同复用并保留 Brush 写入，变化重建，provider 移除清场；
- descriptor 超过驻留 chunk 明确拒绝；
- Demo `OnStart` 运行时网格与 authoring provider 逐 cell 完全等价；
- Brush 成功写入才使纹理失效；
- 空 GameObject marker 的几何反映 rotation 与非均匀 scale；
- 无 audio 目录在 Scripting 前注入 `NullAudioApi`，非法参数仍保留精确诊断；
- Demo 源码纪律继续禁止 `using PixelEngine.Simulation`。

## 真实窗口路线

使用正式 `apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe` 打开 `PixelEngine Demo / Lava Mine`。本轮没有修改或保存工程/场景文件；Brush 只写临时 authoring world，关闭 Editor 后 `git status` 未出现 `project.pixelproj` 或 scene artifact。

| 路线 | 真实结果 |
|---|---|
| 打开 Demo Scene | Scene View 状态为 `authoritative cell world · 640×360 cells`；实际地面、平台、斜坡、三处熔岩池、边界与 Player/Goal marker 可见，不再是五块示意矩形 |
| `B` → Brush | Tools 显示显式 Brush；默认 `empty` 材质在左侧熔岩区连续写入，footer 报告 13 cells，可见像素洞立即出现 |
| `W` / `E` / `R` | 选择 Player 后依次切换 Move、Rotate、Scale，工具状态可见且进程保持稳定 |
| Play 长跑 | 点击 Play 后连续运行约 18 秒，Editor HWND/标题持续存在，无 Audio 缺失异常或进程退出 |
| Stop | 再次点击 Play 退出运行态，回到 authoring；权威世界恢复，Brush 像素洞仍保留，证明同 hash adopt/快照恢复没有重新铺设覆盖修改 |
| 退出 | 正常关闭 Editor，无未保存工程/场景提示和残留窗口 |

## Windows Graphics Capture 校验

以下均为 Computer Use 从真实 HWND 获得的内存 JPEG frame；本轮没有把帧另存为文件，因此只记录尺寸、原点、字节数和 SHA256，不虚构 artifact 路径。

| 状态 | Frame | Bytes | SHA256 |
|---|---|---:|---|
| 初始权威 authoring world | 2528×1401；origin 48,5 | 240,425 | `3b3721db5c14990a70757b4f4d64841f5e28ae3477612671e075064e6807d93d` |
| Brush 写入 13 cells | 2528×1401；origin 48,5 | 240,481 | `a0633f2c6614803677ce5b9a0220fe1366999599852ed60e04f9ffd4254bafcb` |
| Move / W | 2528×1401；origin 48,5 | 224,432 | `5a7f7d280839f27ed27f9ae2b23c19e34324404724688789258f6d5848dad28a` |
| Rotate / E | 2528×1401；origin 48,5 | 224,338 | `74ac2218e3b1156be25c64b9a5ebe0e7eb557c4fb618af01fd39f116bab65b8d` |
| Scale / R | 2528×1401；origin 48,5 | 224,524 | `5afa856446858a2e33fb9ddd36eca344c315fd0baafc702b5b9501a7f27c88ab` |
| Play 起始 | 2528×1401；origin 48,5 | 224,625 | `3e2d20f6db0488b875706ca2036c25b2759837aa1f00310ab8eec4422b5c8e1d` |
| Play 约 18 秒 | 2528×1401；origin 48,5 | 224,625 | `3e2d20f6db0488b875706ca2036c25b2759837aa1f00310ab8eec4422b5c8e1d` |
| Stop 恢复并移开 hover | 2528×1401；origin 48,5 | 225,429 | `2c39893e0b6b823e34494eae8c3c0125a0724c85f99ef1ac91239bd66098bee7` |

Play 起始与约 18 秒后的 Scene authoring frame 完全相同是预期结果：Scene View 显示的是被快照保护的编辑态纹理，不是运行时 Game View；这组相同 SHA 同时证明长跑期间没有被运行时重新铺设或 UI 随机噪声改写。

## Unity 6.5 差异矩阵

| Unity 心智模型/路线 | 本轮状态 | 结论 |
|---|---|---|
| Edit 模式看到真实关卡世界 | 已闭合 | 显式 provider 产生权威 640×360 cell 纹理，非硬编码示意 |
| Brush 写入即时视觉反馈 | 已闭合 | 成功写入 13 cells 后纹理失效并显示像素洞 |
| Play 不覆盖 authoring 修改 | 已闭合 | 同 hash adopt + Play 快照恢复后像素洞保留 |
| 空对象 Transform 反馈 | 已闭合 | marker 编码 world rotation/scale，W/E/R 真实复走 |
| Editor Play 音频服务 | 已闭合 | 真实/无声后端均在 Scripting 前可用，18 秒长跑无退出 |
| 200% 或不同 DPI 双屏连续移动 | **未验证** | 本机没有不同 DPI 目标屏 |
| Explorer→Project 人工跨窗口拖入 | **未验证** | Computer Use 安全边界禁止；既有 native OS 链路不能冒充人工手势 |
| Prefab、Settings、外部脚本编辑与最终全表 | **待最终复走** | 本轮只闭合权威世界预览切片，`EDITOR-003` 保持 `[~]` |

## 下一轮差异

- 在具备不同 DPI/200% 显示器的环境验证跨屏、最大化、resize、IME caret 与 dock 浮窗连续坐标切换。
- 在允许跨窗口 pointer drag 的人工环境完成 Explorer 文件/目录→Project，并核对 footer、Console、manifest 与磁盘。
- 逐项复走 Prefab、Project Settings、Preferences、外部脚本编辑、错误恢复和 720p 标签溢出。
- 完成 Unity 6.5 与 PixelEngine 最终差异矩阵、独立人工 reviewer 和完整 author→play→edit→build→run 后，才可把 `EDITOR-003` 改为 `[x]`。
