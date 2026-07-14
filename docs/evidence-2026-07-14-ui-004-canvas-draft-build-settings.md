# 2026-07-14 UI-004 Canvas 草稿恢复与 Build Settings 提交边界验真

taskIds: `UI-004`
implementationCommits: `9ae831614c6917c8558d85f7ee194044d4766381`, `1286c735b708b81b95c3f4998e0516b545e9a555`, `1ad36282a0ca5d6980a2bc7ba7fc7f9e0e85e48d`, `29a631f4874bac074c6095043b5662bc471b1ad4`, `47acbcd5198172af32c03a3cb1f57ce96543f5c8`, `1f9e5b6e1d63118dcf95314ceebfbdbf9cf30e52`, `a3d36cd22459c62f57f28c51c08d90c2d7b35b14`
runCommit: `a3d36cd22459c62f57f28c51c08d90c2d7b35b14`
runSessionId: `local-20260714-ui004-canvas-draft-build-settings-footer-overflow`
evidenceState: `local_build_settings_overflow_complete_clean_final_output_refresh_pending_external_input_dpi_review_pending`

## 结论

Canvas / CanvasScaler 的非法 Inspector 草稿现在留在产品错误边界内：非法 manifest 相对路径、非有限或越界 scaler 值不会从 `Draw` 逃逸导致 Editor 崩溃；Edit projection 保留上一份有效预览，同一连续非法状态只写一次 Console；Play、Save、Save As、dirty transition、Open Scene、workspace 恢复与 project open 都返回可执行诊断，不再形成逐帧异常或打不开工程的死链。

`Build` 与 `Build And Run` 现在是明确的场景持久化提交点。命令在创建 build-player 请求前统一要求 Edit Mode、提交尚未结束的 Inspector/gizmo 编辑、校验 authoring scene，并自动保存 dirty scene；任一步失败都会把诊断留在 Build Settings 且不会启动 build service，因此不会再静默打包旧的磁盘场景。

真实 1024×720 窄停靠区中的 Build Settings profile 已改为 Unity-like 的左侧 label / 右侧 value 双列，label 宽度按 44% 响应并限制为 72–144px，字段之间有稳定分隔；可滚动 body 与固定 footer 的 `Build` / `Build And Run` 仍完整可达。footer 现在按当前字体的实测宽度在完整、主操作加 overflow、全 overflow 三档间切换：正式窗口的 222px 可用宽度小于四项直排所需的 284px，因此保留 Build / Build And Run，并把取消与重新预检收进向上锚定的 `...`；更窄时所有动作仍从同一入口可达。正式 runner 从 git tracked 文件复制隔离工程，重开后主动聚焦 Build Settings、等待至少 20 个完整帧，再打开 overflow 并额外等待完整帧；同时拒绝摘要不完整、区域黑帧、低 alpha 和纯色 framebuffer，不会向 Demo 根目录写入临时 `BuildSettings.json`。

`UI-004` 继续保持 `[~]`：本报告闭合本机 Canvas 草稿恢复、构建提交边界与窄 Build Settings 真实窗口证据，不把脚本化动作冒充物理鼠标键盘、不同 DPI/跨屏或外部 reviewer。根 `最终输出/` 已在此前最终 docs HEAD `289b0b06` 上完成 detached clean-worktree 重建、提升和复跑；新增 footer 实现 `a3d36cd2` 后该输出按门禁定义重新变旧，须在后续阶段性代码收敛后从新的最终 docs HEAD 重建，当前不冒充已刷新。

## 实现提交

| Commit | 内容 |
|---|---|
| `9ae83161` | Inspector 捕获 manifest path / CanvasScaler 草稿校验错误并显示冲突诊断 |
| `1286c735` | authoring projection 保留上一有效状态、非法连续状态去重、Play/Save/transition fail-closed；六场景 Game View 矩阵无回归 |
| `1ad36282` | 非法 Canvas 场景在 Load/Open/workspace/project open 路线中返回可恢复诊断 |
| `29a631f4` | Build / Build And Run 前统一校验 Edit Mode、flush、validate 与 dirty scene 自动保存；失败不创建构建请求 |
| `47acbcd5` | 窄 Build Settings 响应式 label/value 双列与可读分隔；持久化 probe 主动聚焦面板 |
| `1f9e5b6e` | 重开后等待 20 帧；新增隔离工程、摘要与 framebuffer 四区域 fail-closed runner |
| `a3d36cd2` | footer 三档响应式动作布局；窄停靠区 overflow 向上锚定，runner 打开菜单并记录实际横向预算 |

## 自动化验证

本机：Microsoft Windows 11 专业版 build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 验证 | 结果 |
|---|---|
| `PixelEngine.Hosting.Tests` Release 全量（HEAD `1f9e5b6e`） | 811 passed / 7 skipped / 0 failed（818 total；4m06s） |
| `EditorConsoleStoreTests\|EditorShellBuildTests\|HostingProjectDisciplineTests`（commit `a3d36cd2`） | 90 passed / 0 failed |
| `dotnet build PixelEngine.sln -c Release --no-restore`（commit `a3d36cd2`） | 32 projects；0 warning / 0 error |
| `tools/run-editor-gameview-presentation-probe.ps1`（commit `1286c735`） | 16:9、4:3、9:16、固定 1920×1080、Maximize On Play、360×720 narrow toolbar；6/6 passed |
| `tools/run-editor-build-settings-probe.ps1`（commit `a3d36cd2`） | attempt 1/3 accepted；隔离工程、21 聚焦后帧、overflow 打开、profile roundtrip 与四区域 framebuffer 全部通过 |
| `tools/validate-task-catalog.ps1` | 81 canonical；49 done / 5 open / 1 active / 26 blocked；valid |

新增回归覆盖：

- Canvas Inspector 的非法 manifest / scaler 草稿不从 Draw 逃逸；
- invalid draft 不替换上一份有效 runtime projection，同一非法 streak 不刷屏；
- invalid draft 阻止 Play、Save 与构建，同时工程/场景打开路线仍可恢复；
- failed scene preparation 在 build service 收到请求之前终止；
- 窄 Build Settings label 列在 120/300/600px 输入下分别收敛为 72/132/144px；
- footer 在 274px 边界保留四项 inline、240px 切换主操作加 overflow，非有限/不足最小宽度退化为全 overflow；
- runner 必须使用 tracked 隔离工程、至少 20 个聚焦后帧、再等待一个 overflow 完整帧，并通过完整摘要、四区颜色/near-black/opaque 门禁和 report SHA。

## Commit-bound 真实构建

构建提交边界实现 commit：`29a631f4874bac074c6095043b5662bc471b1ad4`。

命令路线：Release Editor Shell `--project demo/PixelEngine.Demo --scripted-build-probe --build-output artifacts/editor-build-probe/29a631f4/player-build --capture-frame artifacts/editor-build-probe/29a631f4/editor-frame.bmp`。

| 字段 | 结果 |
|---|---|
| build result | `ok=true`；`exitCode=0`；warnings=`[]`；error=`null` |
| target | `win-x64` / `r2r` / `Release` / `ManagedFallback` / `Windowed` |
| package | `PixelEngine-Demo-0.1.0-win-x64-r2r.zip`；60,937,297 bytes |
| package SHA256 | `a931572d0cc20665987726624096335bc885e9364fe49bc65755f9a6e6bdc022`，与归档实算一致 |
| phase result | native、publish、verify、package、audit 全部完成；publish/package audit passed |
| Editor framebuffer | `1028×720` BGRA32；SHA256 `8280d9376bc2543281fa9c9e0da5dd71746bb5bcc200bee659ef594875a08e06` |

## Commit-bound Build Settings 窗口证据

正式报告：`artifacts/editor-build-settings-probe/a3d36cd2/report.json`。`artifacts/` 是可再生目录，稳定证据以本文的 command、run commit、结构字段和 SHA256 为准。

| 字段 | 结果 |
|---|---|
| run commit | `a3d36cd22459c62f57f28c51c08d90c2d7b35b14` |
| attempt | `1 / 3`；accepted |
| project isolation | `attempt-1/project`；从 git tracked 文件复制；源 Demo 无 `BuildSettings.json` 残留 |
| persistence | applied / close requested / reopened / focused / captured / matches 全为 `True` |
| stable frames | focus 后 `21` 帧；overflow 请求后 `1` 个完整帧 |
| footer layout | available=`222px`；inline required=`284px`；responsive required=`192px`；density=`Overflow`；primary fit / actions accessible / popup open 全为 `True` |
| framebuffer | `1024×720`；BGRA32；2,949,174 bytes |
| sampled colors | overall=`222`；chrome=`245`；Scene=`575`；Build Settings=`246`；right=`223` |
| near-black ratio | chrome / Scene / Build Settings / right 全为 `0.0` |
| opaque ratio | chrome / Scene / Build Settings / right 全为 `1.0` |
| framebuffer SHA256 | `7b20c3e4365fa465e853ad13eeef0b0bdd4ce93a02381050dc2d5554732ffdb5` |

截图已目视核对：顶部 menu/Play toolbar、Scene authoritative world、Hierarchy、Console 与底部 Build Settings 全部完整；Build Settings 中可见目标平台、通道、配置、输出目录、产物名、版本、信息版本等左 label / 右 value 行，垂直分隔清楚。固定 footer 的 Build、Build And Run 与 `...` 均位于右边界内，overflow 菜单贴在按钮上方，明确显示禁用态“取消构建”和可用“重新预检”。

## Detached clean-worktree 正式输出

- clean worktree：`artifacts/clean-worktrees/ui004-final-680704f4`（目录名保留首次 docs commit），最终 detached HEAD `289b0b06f0272d96c276edecc6896ee65fd7c602`，tracked worktree clean。
- submodule：Box2D `8c661469`、FreeType `0a0221a1`、dlg `395ccad2`、RmlUi `1b69207f`；RmlUi 上游缺少 pinned object 时从本机已验证仓库恢复，最终状态无 `+/-`。
- 构建命令：`pwsh -NoProfile -File tools/update-final-output.ps1 -Rid win-x64 -DemoChannel r2r -Configuration Release -DemoRuntimeUiBackend RmlUi`。
- 完整门禁：native build、Editor publish、默认工作台真实 build-player、Game View 六场景、中文 staging 路径 RmlUi Player 80 帧、next-output verifier 全部 exit 0。
- clean `最终输出/` 独立 verifier：`ok=True`，`gitCommit=289b0b06f0272d96c276edecc6896ee65fd7c602`，`checksum_count=392`。
- clean 输出复制到主仓库安全 staging 后，提升前与提升后 verifier 均为 `ok=True` / 392 checksums；旧根目录仅在新 staging 通过后原子备份/替换。
- 提升后的仓库根 `最终输出/游戏Demo/PixelEngine Demo.exe` 再运行 80 帧：exit 0，`window_frame_probe` 存在，3 Canvas，requested/active=`RmlUi`，fallback=`False`，中文 content path=`True`。
- 根 Demo framebuffer：`1028×720` BGRA32，2,960,694 bytes，SHA256 `2dd5b9ddf3c0cdcde2a150f4b93a0ec5cb11dd65b1b62fd520bc0099df43923c`；已目视确认主菜单、任务卡片、按钮和背景世界完整。
- 当前实现已前进到 `a3d36cd2`，因此上述 `289b0b06` 输出只证明此前阶段，不满足当前 HEAD identity；下一次阶段性发布必须重建而非复制旧目录。

## 剩余边界

- 使用真实鼠标在窄 Build Settings 中编辑文本、切换 combo 并点击 Build And Run，核对焦点、tooltip 与错误恢复手感；本报告的 scripted route 不冒充物理输入。
- 在至少一块 Windows 200% 物理 DPI 显示器及不同 DPI 跨屏场景复走 CanvasScaler、Game View、Build Settings 与输入映射。
- 独立 reviewer 复走 UI-004 / EDITOR-003 最终 Unity 差异矩阵。
- 后续任何代码或稳定文档提交都必须再次刷新 `最终输出/`，并让独立 verifier 拒绝旧 commit identity。
