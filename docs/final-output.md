# 正式输出目录

`最终输出/` 是本仓库本机可运行产物的稳定入口，只放已经完整验证过的编辑器应用和游戏 Demo。

目录结构：

- `最终输出/编辑器/`：编辑器应用，入口为 `PixelEngine.Editor.Shell.exe`。
- `最终输出/游戏Demo/`：玩家 Demo 包，入口为 `PixelEngine Demo.exe`。
- `最终输出/_验证记录/`：本次替换前的验证日志、截图和 `manifest.json`。

更新规则：

- 不手工复制 `artifacts/`、`publish/`、`PROBE` 或临时修复目录里的产物到正式输出。
- 只通过 `tools/update-final-output.ps1` 更新正式输出。
- 脚本先把所有中间产物写入 `artifacts/final-output-staging/<timestamp>/`。
- 编辑器默认工作台探针、编辑器出包探针和 Demo 窗口短跑全部通过后，才原子替换 `最终输出/`。
- 任一步失败时保留旧的 `最终输出/`，不会把半成品发布成正式版。

常用命令：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1
```

可选参数：

```pwsh
pwsh -NoProfile -File tools/update-final-output.ps1 -Rid win-x64 -DemoChannel r2r -Configuration Release
```

`最终输出/` 已加入 `.gitignore`，只作为本机正式产物目录，不提交到仓库。
