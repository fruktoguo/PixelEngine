# 正式输出目录

`最终输出/` 是本仓库本机可运行产物的稳定入口。完整发布模式放置已经验证过的编辑器、Windows 安装器、游戏
Demo 与外部编辑器自动化公共 API；开发者快速输出模式放当前源码构建的编辑器、win-x64 安装器与可玩 Demo，
并以 `_快速构建/manifest.json` 明确标记未运行测试、产品探针或 verifier。

目录结构：

- `最终输出/编辑器/`：编辑器应用，入口为 `PixelEngine.exe`。
  - `ScriptReferenceAssemblies/`：供 VS Code / Visual Studio 等独立 IDE 工程使用的脚本开发产品 SDK。编辑器生成的脚本 `.csproj` 通过 `HintPath` 引用这里的程序集，不依赖源码仓库中的 `src/*.csproj`。
- `最终输出/安装器/`：当前用户安装包 `PixelEngine-Setup-<version>-win-x64.msi`、独立 `manifest.json`、静态验证报告与 `SHA256SUMS`。MSI 默认安装到 `%LocalAppData%\Programs\PixelEngine`，安装向导允许修改路径，并创建桌面与开始菜单快捷方式。
- `最终输出/游戏Demo/`：玩家 Demo 包，入口为 `PixelEngine Demo.exe`。
- `最终输出/自动化/`：与本次 Editor 同源构建并通过真实外部进程验收的公共自动化产品面。
  - `CLI/pixelengine-editor.exe`：独立 CLI，不需要源码工程、MCP、屏幕坐标或 Computer Use。
  - `SDK/`：`PixelEngine.Editor.Automation.Protocol` 与
    `PixelEngine.Editor.Automation.Client` 的版本化 NuGet 包。
  - `Schema/`：wire Schema、capability Schema 与包含 canonical SHA256 的完整能力矩阵。
  - `文档/editor-automation-api.md`：发行版 API/CLI/.NET Client 使用合同。
  - `Skill/pixelengine-editor/`：经 `$skill-creator` validator 和真实 CLI forward test 验证的
    Codex Skill。
- `最终输出/_验证记录/`：本次替换前的验证日志、截图和 `manifest.json`。
  - `editor-automation-e2e/report.json`：每个外部 CLI 进程的 PID、退出码、日志路径与 SHA256，
    以及完整 author→play→debug→stop→rerun→modify→build→launch→terminate 结果。
  - `manifest.json` 会记录 `gitCommit`、`sourceWorktreePolicy=tracked-clean-required`、
    `sourceTrackedWorktreeClean=true`、脚本 SDK、自动化 CLI/SDK/Schema/Skill、能力矩阵 digest、
    校验清单位置和验证日志路径。

更新规则：

- 不手工复制 `artifacts/`、`publish/`、`PROBE` 或临时修复目录里的产物到正式输出。
- 只通过 `tools/update-final-output.ps1` 更新正式输出。
- 脚本要求已跟踪工作树干净；如有未提交的源码/计划/工具改动，会拒绝更新正式输出。未跟踪或已忽略的 `最终输出/`、`artifacts/` 产物不会被这个门禁阻挡。
- 脚本先把所有中间产物写入 `artifacts/final-output-staging/<timestamp>/`。
- win-x64 MSI 从同一提交和同一次 native build 单独生成自包含 ReadyToRun Editor payload，目标机器不需要预装 .NET；它不会改变 `编辑器/ScriptReferenceAssemblies` 的产品 SDK 边界。安装器 manifest 和正式输出 manifest 必须绑定同一 `gitCommit`。
- 编辑器默认工作台/出包探针、使用已发布 Editor 的 Game View 六场景矩阵（16:9、4:3、
  portrait、固定 1920×1080、Maximize On Play、360px 窄工具栏）、无 skipped 的外部 CLI
  自动化 E2E 和 Demo 窗口短跑全部通过后，会先对待发布目录运行
  `tools/verify-final-output.ps1` 独立审计，审计通过后才原子替换 `最终输出/`。
- 自动化 E2E 启动全新 Editor OS 进程；每次 API 动作都启动发行版
  `pixelengine-editor.exe`，runner 不引用 .NET Client、Named Pipe、credential、MCP、Computer
  Use 或坐标旁路。事务使用单 CLI 连接的 `transaction execute`，并真实验证共享 Undo/Redo。
- E2E 工作工程与 credential 位于当前用户 `%TEMP%` 的随机 ACL 根，成功或失败都清理；构建与
  验证报告留在仓库 staging 供失败诊断。
- 任一步失败时保留旧的 `最终输出/`，不会把半成品发布成正式版。
- 编辑器运行目录默认清理 `.pdb` / `.xml` 开发元数据；需要诊断符号时显式传 `-IncludeEditorSymbols` 重新生成。
- `编辑器/ScriptReferenceAssemblies/` 是上述清理规则的产品 SDK 边界：固定保留 `PixelEngine.Audio/Content/Core/Gui/Hosting/Interop/Physics/Rendering/Scripting/Serialization/Simulation/UI/World` 的 managed DLL 与同名 XML IntelliSense 文档，并带齐编辑器 publish 中可识别的第三方 managed dependency DLL。它不包含产品入口 `PixelEngine.dll`、`PixelEngine.Editor*`、native DLL 或任何 PDB；第三方 dependency 不复制无关 XML 文档。
- `ScriptReferenceAssemblies/` 不是额外的运行时加载目录，也不是 NuGet 包替代品；用途是让发行版编辑器生成的独立脚本 solution/project 在安装 .NET 10 SDK 后可直接 restore/build，并获得 PixelEngine API 注释。

常用命令：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1
```

Windows 下也可直接双击仓库根目录的 `一键更新最终输出.bat`。BAT 只保留 codepage 无关的
ASCII/CRLF 启动逻辑，实际编排由 `tools/run-final-output-one-click.ps1` 执行：先运行同一正式输出
更新脚本，成功替换后再对根 `最终输出/` 运行一次独立 verifier；两步都成功才打开输出目录。
失败时控制台会保留错误与退出码，发布器在替换前失败仍保留旧的正式输出。

若只需要尽快得到可运行产物，可双击仓库根目录的 `一键构建.bat`。它默认构建 win-x64 Release
native、发布 Editor、构建自包含 win-x64 MSI，并把 Demo 组装为可玩目录，成功后原子替换 `最终输出/编辑器`、
`最终输出/安装器` 与 `最终输出/游戏Demo`；不会运行 test、产品 probe 或正式输出 verifier（MSI 仍执行自身静态结构校验）。快速模式会替换此前的完整验证版，
且不能作为正式验收证据；需要恢复完整验证版时重新双击 `一键更新最终输出.bat`。命令行可传
`-Configuration Debug` 改为 Debug 构建。

校验现有正式输出（不重新打包、不替换目录）：

```pwsh
pwsh -NoProfile -File tools/verify-final-output.ps1
```

该校验会读取 `最终输出/_验证记录/manifest.json` 与 `SHA256SUMS`，确认 manifest 绑定当前
`HEAD`、来源门禁为 `tracked-clean-required`、入口文件和验证记录存在、脚本 SDK primary
DLL+XML 与 managed dependency 精确匹配 manifest、SDK 不含 PDB/Editor/native、SDK 外默认
不泄漏 `.pdb` / `.xml`；同时独立检查自动化 CLI runtime、两个 NuGet archive 的必需 entry、
四文件 Skill、协议 Schema 引用、至少 150 capability / 300 UI command 的排序与双向闭包，并
从矩阵数组原文重算三组 canonical SHA256。E2E 报告必须绑定同一 Editor/CLI hash、精确 10 个
通过 scope、零 skipped、至少 35 个外部 CLI operation；每条 operation 的序号、PID、允许退出
码、受限日志路径和 stdout/stderr SHA256 都会复验。验证器还确认两次 Play session 不复用、
transaction Undo/Redo、Stop 后修改保存、Build Succeeded、Player 保持 Running 后被终止、Editor
退出码 0 且 descriptor 已删除。之后才检查默认工作台、Game View 六场景、Demo build/window
probe 和根级 SHA256，并拒绝未登记的额外文件或登记了不存在文件的清单。若只审计历史提交
生成的旧产物，可显式传 `-AllowCommitMismatch`。

可选参数：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1 -Rid win-x64 -DemoChannel r2r -Configuration Release
```

诊断符号：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1 -IncludeEditorSymbols
```

`最终输出/` 已加入 `.gitignore`，只作为本机正式产物目录，不提交到仓库。
