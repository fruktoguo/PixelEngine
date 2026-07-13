# 正式输出目录

`最终输出/` 是本仓库本机可运行产物的稳定入口，只放已经完整验证过的编辑器应用和游戏 Demo。

目录结构：

- `最终输出/编辑器/`：编辑器应用，入口为 `PixelEngine.Editor.Shell.exe`。
  - `ScriptReferenceAssemblies/`：供 VS Code / Visual Studio 等独立 IDE 工程使用的脚本开发产品 SDK。编辑器生成的脚本 `.csproj` 通过 `HintPath` 引用这里的程序集，不依赖源码仓库中的 `src/*.csproj`。
- `最终输出/游戏Demo/`：玩家 Demo 包，入口为 `PixelEngine Demo.exe`。
- `最终输出/_验证记录/`：本次替换前的验证日志、截图和 `manifest.json`。
  - `manifest.json` 会记录 `gitCommit`、`sourceWorktreePolicy=tracked-clean-required`、`sourceTrackedWorktreeClean=true`、脚本 SDK path/policy/primary assembly/managed dependency 清单、校验清单位置和验证日志路径。

更新规则：

- 不手工复制 `artifacts/`、`publish/`、`PROBE` 或临时修复目录里的产物到正式输出。
- 只通过 `tools/update-final-output.ps1` 更新正式输出。
- 脚本要求已跟踪工作树干净；如有未提交的源码/计划/工具改动，会拒绝更新正式输出。未跟踪或已忽略的 `最终输出/`、`artifacts/` 产物不会被这个门禁阻挡。
- 脚本先把所有中间产物写入 `artifacts/final-output-staging/<timestamp>/`。
- 编辑器默认工作台/出包探针、使用已发布 Editor 的 Game View 六场景矩阵（16:9、4:3、portrait、固定 1920×1080、Maximize On Play、360px 窄工具栏）和 Demo 窗口短跑全部通过后，会先对待发布目录运行 `tools/verify-final-output.ps1` 独立审计，审计通过后才原子替换 `最终输出/`。
- 任一步失败时保留旧的 `最终输出/`，不会把半成品发布成正式版。
- 编辑器运行目录默认清理 `.pdb` / `.xml` 开发元数据；需要诊断符号时显式传 `-IncludeEditorSymbols` 重新生成。
- `编辑器/ScriptReferenceAssemblies/` 是上述清理规则的产品 SDK 边界：固定保留 `PixelEngine.Audio/Content/Core/Gui/Hosting/Interop/Physics/Rendering/Scripting/Serialization/Simulation/UI/World` 的 managed DLL 与同名 XML IntelliSense 文档，并带齐编辑器 publish 中可识别的第三方 managed dependency DLL。它不包含 `PixelEngine.Editor*`、native DLL 或任何 PDB；第三方 dependency 不复制无关 XML 文档。
- `ScriptReferenceAssemblies/` 不是额外的运行时加载目录，也不是 NuGet 包替代品；用途是让发行版编辑器生成的独立脚本 solution/project 在安装 .NET 10 SDK 后可直接 restore/build，并获得 PixelEngine API 注释。

常用命令：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1
```

校验现有正式输出（不重新打包、不替换目录）：

```pwsh
pwsh -NoProfile -File tools/verify-final-output.ps1
```

该校验会读取 `最终输出/_验证记录/manifest.json` 与 `SHA256SUMS`，确认 manifest 绑定当前 `HEAD`、来源门禁为 `tracked-clean-required`、入口文件和验证记录存在、脚本 SDK primary DLL+XML 与 managed dependency 精确匹配 manifest、SDK 不含 PDB/Editor/native、SDK 外默认不泄漏 `.pdb` / `.xml`、编辑器默认工作台与 Demo probe stdout 含成功 marker、Game View 报告绑定同一 commit 且六场景逐项满足 UI stack `1→0→1`、presentation 同步、toolbar fit/overflow 和 framebuffer SHA256、Demo build-result 为 `ok=true`，再逐项重算根级 SHA256，并拒绝未登记的额外文件或登记了不存在文件的清单。若只审计历史提交生成的旧产物，可显式传 `-AllowCommitMismatch`。

可选参数：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1 -Rid win-x64 -DemoChannel r2r -Configuration Release
```

诊断符号：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1 -IncludeEditorSymbols
```

`最终输出/` 已加入 `.gitignore`，只作为本机正式产物目录，不提交到仓库。
