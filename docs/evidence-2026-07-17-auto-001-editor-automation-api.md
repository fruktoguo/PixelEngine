# 2026-07-17 AUTO-001 外部编辑器自动化公共 API 完成证据

taskIds: `AUTO-001`
implementationCommit: `23b03b54a76e3da869ae7aada252ab72e288be82`
runSessionId: `local-20260717-auto001-clean-23b03b54`
evidenceState: `complete`

## 结论

AUTO-001 的八个提交节点已经完整闭合。PixelEngine Editor 现在提供版本化、可发现、经双向 HMAC 认证的 Windows Named Pipe 公共 API，以及 Protocol、JSON Schema、Server、公开 .NET Client、独立 CLI、机器可读能力矩阵、文档和 `pixelengine-editor` Skill。Editor UI 与 API 使用同一组 semantic command、主线程/Engine safe phase、revision、事务、Undo/Redo、dirty guard、build/play service 和持久化路径，没有 test-only handler 或屏幕坐标旁路。

最终验收不是 in-process 测试：detached clean worktree 在空 user-data/discovery/artifact root 中启动全新 Editor，42 个独立 CLI OS 进程完成故障事务、正常事务与 Undo/Redo、两轮 Play/Stop、runtime/Console/Profiler/Pause/Step、继续修改保存、Build、Player 启动/运行确认/终止和 Editor 退出清理。10/10 必需 scope passed，0 skipped；整个流程未使用 MCP、Computer Use、OCR、屏幕坐标或 `--scripted-*` 作为 API 操作入口。

本结论只关闭 AUTO-001。运行时 Web UI 的真实物理点击、不同 DPI/跨屏和外部 reviewer 仍由 `UI-001`、`UI-004`、`EDITOR-002`、`EDITOR-003` 等 canonical task 跟踪，不因公共 API 完成而被冒充关闭。

## 提交节点

| 节点 | Commit | 内容 |
|---|---|---|
| 1 | `12d0e4d5` | canonical task、产品目标、权威架构与协议边界 |
| 2 | `24f2964d` | Protocol/Schema、发现、双向认证、Named Pipe Server/Client transport |
| 3 | `c4f5d49c` | 主线程与 Engine phase scheduler、revision、原子事务、Undo、事件、artifact、审计 |
| 4 | `273f3665` | workspace/window/layout/Scene/Hierarchy/Inspector/tool 真实语义能力 |
| 5 | `fd3d1552` | project/asset/preview/settings/Console/Profiler/runtime/Canvas 能力 |
| 6 | `fe52a901` | build/player、公开 .NET Client、独立 CLI 与开发者文档 |
| 7 | `9bd62fe3` | 172-capability / 329-UI-command 双向矩阵、安全/性能/重连测试与 Skill |
| 8 | `53074085` | clean final-output、同连接事务 CLI 与全新外部进程 E2E 工具链 |

节点八后的发行边界修复为 `31a3e806`、`1991b130`、`446095b0`、`23b03b54`：分别阻止 Editor automation assembly 泄漏进脚本 SDK、固定发行子进程 UTF-8、按真实 CLI 合同区分空发现与结构化错误、固定 Demo 玩家进程 UTF-8。它们没有放宽验收；独立 verifier 反而新增了 NuGet/Skill/矩阵/日志、错误通道和中文 Result marker 的 fail-closed 校验。

## 能力与协议闭包

- capability matrix：172 条 capability、329 条 UI command，双向引用、排序、Schema ref 与 canonical SHA256 均由同一 registry 生成并由独立工具重算。
- matrix digest：`29dd65b20cfb9c50cde41a999568d73391a21655501f908872d4e48a39b67c6a`。
- 覆盖 instance/workspace/project、window/panel/focus/dock/layout、Scene/Game/capture、Hierarchy/selection/GameObject、Inspector/Transform/component、Project/asset/import/reference/preview、Console、Play、runtime、Canvas/CanvasScaler、tool/gizmo/grid/snap/brush、Settings、Profiler、build/player、artifact/event/transaction。
- wire v1 固定 frame 与 UTF-8 JSON；Windows Named Pipe 为已实现 transport，Unix Domain Socket 仅保留版本化预留诊断，不冒充已实现平台。
- current-user ACL、descriptor/token/reparse-point/path containment、角色域分离双向 HMAC、scope permission、deadline/cancel、幂等、global/resource revision、transaction lease、disconnect rollback、event ack/resume/resync/backpressure、artifact quota/SHA256 和持久 JSONL audit 均有回归。
- 空闲时 scheduler 只检查一次原子 pending signal，不轮询 pipe/timer，不构造 snapshot；事件、制品、事务和队列均有显式上限。

## 外部进程 E2E

clean run 绑定 commit `23b03b54a76e3da869ae7aada252ab72e288be82`。报告记录 42 个 CLI 进程；三个预期非零结果为：

| Sequence | Operation | Exit | 证明 |
|---:|---|---:|---|
| 1 | `discover` | 3 | Editor 启动前实例与诊断均为空 |
| 8 | `transaction-execute-rollback` | 4 | `transaction_failed`，部分创建零残留 |
| 42 | `discover-after-exit` | 3 | Editor 退出后 descriptor 已移除 |

其余 39 个操作 exit 0。故障事务后 Hierarchy 无残留；正常事务提交后 Undo/Redo 恢复同一 marker。第一次 Play session `d878eedb986049bbafe13905a04f9f38` 与第二次 `12ddee601b414bf9b91955d7d22290ca` 不同，证明 runtime ID 没有跨 session 复用。Build state 为 `Succeeded`；Player 被确认保持运行后经稳定 process id 终止；Editor 通过公共 `workspace.exit` 退出且 discovery 清理完成。

## Clean Final Output

- clean worktree：`C:\Users\YuoHira\AppData\Local\Temp\pixelengine-auto001-clean-53074085`，detached HEAD `23b03b54a76e3da869ae7aada252ab72e288be82`，tracked status clean。
- submodule：Box2D `8c661469`、FreeType `0a0221a1`、dlg `395ccad2`、RmlUi `1b69207f`。
- 输出：Editor、ScriptReferenceAssemblies、Demo、CLI、Protocol/Client NuGet、Schema、能力矩阵、文档、Skill 和全部验证记录。
- 独立 verifier：`ok=True`，518 个 SHA256 条目，172 capabilities，329 UI commands，42 CLI processes。
- 编码审计：171 个 `.log/.json/.txt/.md/.ps1/.yaml` 文件扫描，0 个 replacement/mojibake 文件；Demo window log 的中文启动、内容加载、窗口接入和短跑完成 marker 4/4 保真。
- Demo 使用含中文组件的 `游戏Demo构建` 路径，RmlUi 为实际 active backend，`fallback=False`，`content_path_non_ascii=True`。

## 测试门禁

| 验证 | 当前提交结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release -p:TreatWarningsAsErrors=true` | 0 warning / 0 error |
| Automation 全量 | 125 passed / 0 skipped / 0 failed |
| Editor 全量 | 133 passed / 0 skipped / 0 failed |
| Hosting 全量 | 943 total；936 passed / 7 明确环境条件 skipped / 0 failed；TRX stderr 0 |
| Rendering 全量 | 197 passed / 27 显式 native GL 条件 skipped / 0 failed |
| Demo 全量 | 142 passed / 1 显式 native GL 条件 skipped / 0 failed |
| FinalOutput 定向 | 17 passed / 0 skipped / 0 failed |
| Demo 外部 headless UTF-8 probe | exit 0；中文 marker 保真；replacement 0 |

Hosting TRX：`artifacts/auto001-hosting-full-23b03b54/auto001-final-23b03b54.trx`，SHA256 `bd5f78db2521eac626a037fd86d79e3e5c5d43f1db194e3f0e41275fc2809114`。7 条 skip 是显式物理外部拖放、native GL/Ultralight 或缺失环境条件；AUTO-001 E2E 自身的 10 个必需 scope 没有任何 skip/not-executed。

## Skill 验证

- `$CODEX_HOME/skills/pixelengine-editor` 由 `$skill-creator` 规范生成，只有 `SKILL.md`、`agents/openai.yaml`、`references/workflows.md`、`scripts/invoke.ps1` 四个规定文件。
- 安装目录与 clean final-output 打包 Skill 为 4/4 文件 SHA256 一致。
- 官方 `quick_validate.py`：`Skill is valid!`。
- 设置 `PIXELENGINE_EDITOR_CLI` 指向 clean package 后，经 Skill wrapper 前向调用 `--version` 返回 `0.1.0`。
- Skill 只调用独立 CLI，不读取 credential、不直连 Named Pipe、不依赖 MCP 或坐标操作。

## 稳定哈希

| 文件 | Bytes | SHA256 |
|---|---:|---|
| `_验证记录/manifest.json` | 5,453 | `ee673726334fb6427f744c3283c93b6079f9f475712b5acfe96d101755fc43af` |
| `_验证记录/editor-automation-e2e/report.json` | 25,319 | `80101877df7f0960c0b2c756e1051507a4614343771514a74767ae3d7d019f2e` |
| `_验证记录/logs/demo-window.stdout.log` | 5,396 | `3fa32b70203858e34a4ac98f9961bb93616778a12f2f9159f902a82b4e66e4a6` |
| `_验证记录/logs/demo-build-player.stdout.log` | 13,980 | `ab6ec02d5fa13ef47c561dfee44f83db198da297999ce542f800ab9e665497d2` |
| `自动化/Schema/editor-automation-capabilities.v1.json` | 171,715 | `eca534b3bc692d345c022c3d0a0727f6f1bbc01b971a4e5810286fdad8755321` |
| `SHA256SUMS` | 62,414 | `b5f9a7c7ff633575af8c04a27a53a0262b629c0f4ce0f0d6c76034915b36b237` |

`最终输出/` 是可再生产物而不是唯一稳定证据；本报告由 Evidence Index 记录 SHA256。AUTO-001 完成状态提交后，正式输出必须从该完成提交的 detached clean worktree 再生成，并再次要求 manifest commit 与当前 HEAD 一致。
