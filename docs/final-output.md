# 正式输出目录

`最终输出/` 是本仓库本机可运行产物的稳定入口，只放已经完整验证过的编辑器应用和游戏 Demo。

目录结构：

- `最终输出/编辑器/`：编辑器应用，入口为 `PixelEngine.Editor.Shell.exe`。
- `最终输出/游戏Demo/`：玩家 Demo 包，入口为 `PixelEngine Demo.exe`。
- `最终输出/_验证记录/`：本次替换前的验证日志、截图和 `manifest.json`。
  - `manifest.json` 会记录 `gitCommit`、`sourceWorktreePolicy=tracked-clean-required`、`sourceTrackedWorktreeClean=true`、校验清单位置和验证日志路径。

更新规则：

- 不手工复制 `artifacts/`、`publish/`、`PROBE` 或临时修复目录里的产物到正式输出。
- 只通过 `tools/update-final-output.ps1` 更新正式输出。
- 脚本要求已跟踪工作树干净；如有未提交的源码/计划/工具改动，会拒绝更新正式输出。未跟踪或已忽略的 `最终输出/`、`artifacts/` 产物不会被这个门禁阻挡。
- 脚本先把所有中间产物写入 `artifacts/final-output-staging/<timestamp>/`。
- 编辑器默认工作台探针、编辑器出包探针和 Demo 窗口短跑全部通过后，会先对待发布目录运行 `tools/verify-final-output.ps1` 独立审计，审计通过后才原子替换 `最终输出/`。
- 任一步失败时保留旧的 `最终输出/`，不会把半成品发布成正式版。
- 编辑器正式输出默认清理 `.pdb` / `.xml` 开发元数据；需要诊断符号时显式传 `-IncludeEditorSymbols` 重新生成。

常用命令：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1
```

校验现有正式输出（不重新打包、不替换目录）：

```pwsh
pwsh -NoProfile -File tools/verify-final-output.ps1
```

该校验会读取 `最终输出/_验证记录/manifest.json` 与 `SHA256SUMS`，确认 manifest 绑定当前 `HEAD`、来源门禁为 `tracked-clean-required`、入口文件和验证记录存在、编辑器 / Demo probe stdout 含成功 marker、Demo build-result 为 `ok=true`，逐项重算 SHA256，并拒绝 `SHA256SUMS` 未登记的额外文件或登记了不存在文件的清单。若只审计历史提交生成的旧产物，可显式传 `-AllowCommitMismatch`。

可选参数：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1 -Rid win-x64 -DemoChannel r2r -Configuration Release
```

诊断符号：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1 -IncludeEditorSymbols
```

`最终输出/` 已加入 `.gitignore`，只作为本机正式产物目录，不提交到仓库。
