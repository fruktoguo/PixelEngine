# 2026-07-11 DOC-001 入门文档与首个工程链路证据

taskIds: `DOC-001`
implementationCommits: `dfa27719`, `a324c5fc`
runSessionId: `local-20260711-doc001-clean-project-r2r`
evidenceState: `documentation_skeleton_and_local_clean_project_complete_pending_dependencies`

## 结论

根 `README.md`、`docs/getting-started.md` 与 `docs/tutorial-first-project.md` 已覆盖 Windows 前置、native/managed build、独立 Editor、最近工程恢复、Project 双逻辑根、创建 Behaviour、Play、场景设置和 `win-x64 / R2R` 玩家包。

文档编写期间发现并修复两项会使教程失真的产品问题：外部工程脚本曾被 Demo 静态程序集错误跳过，且空内容工程未挂载脚本运行时；随后真实玩家验证又发现 scene 会在脚本注册前物化。现在 PowerShell/Bash 打包均分发外部工程 `scripts/`，Demo 内建脚本不会重复分发，玩家在 scene 物化前加载 `content/scripts`，空工程也接入脚本上下文。

## 干净工程复验

本地创建仅含 `project.pixelproj`、`content/startup.json`、`content/scenes/main.scene` 与 `scripts/FirstProjectBehaviour.cs` 的干净工程。Behaviour 的 `OnStart` 输出 `DOC001_CUSTOM_SCRIPT_STARTED`；scene 通过 `typeName` 挂载该 Behaviour，不含 `materials.json` / `reactions.json`，因此覆盖新建工程的真实 minimal-world 分支。

```powershell
pwsh -NoProfile -File tools/build-player.ps1 `
  -Rid win-x64 `
  -Channel r2r `
  -Configuration Release `
  -Output artifacts/doc001-player `
  -ContentRoot artifacts/doc001-clean-project/content `
  -ProductName "DOC001 Clean Project" `
  -StartScene scenes/main.scene
```

结果：native、publish、verify、package、audit 全部成功；生成 `artifacts/doc001-player/player/DOC001 Clean Project.exe` 和确定性 zip，包内存在 `content/scripts/FirstProjectBehaviour.cs`。

为可靠捕获 WinExe 玩家输出，在 `player/app` 执行同一 R2R payload：

```powershell
dotnet PixelEngine.Demo.dll --content ..\content --headless --ticks 2 --no-hot-reload
```

关键输出：

```text
随包脚本程序集已注册：...\content\scripts
脚本运行时已接入 Hosting/Simulation 后端。
DOC001_CUSTOM_SCRIPT_STARTED
Engine frame: 2, scene: main
```

教程中的 `OnGui` 完整示例另经 `RuntimeScriptAssemblyCompiler` 实际编译并 headless 执行，输出 `FIRST_PROJECT_STARTED` 且进入 `main` 场景。

## 自动化验证

| 命令 | 结果 |
|---|---|
| `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --filter "FullyQualifiedName~RegisterPackagedScriptAssemblies|FullyQualifiedName~MinimalSmokeWorld"` | 3/3 passed；覆盖外部程序集注册、scene 物化和 minimal scripting context |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --filter "FullyQualifiedName~PackageScriptPlacesRuntimeFilesUnderAppAndAuditRejectsRootClutter"` | 1/1 passed；覆盖展开包、player、archive checksum 的 script 路径 |
| `bash -n tools/package.sh` | passed |
| `pwsh tools/validate-task-catalog.ps1` | passed |
| `git diff --check` | passed |

`GettingStartedDocumentsTrackStandaloneEditorAndFirstProjectWorkflow` 还会锁定 README 链接、独立 Editor 入口、`--no-reopen-last-project`、`ScriptSource`、R2R、headless 命令和脚本观察标记。

## 尚未解除的依赖

DOC-001 依赖 `EDITOR-003` 与 `REL-001` 的最终稳定界面/命令。目前文档骨架、实现修复和本机干净工程复验已完成，但不能在以下条件满足前标 `[x]`：

- `EDITOR-003` 取得目标 DPI/IME/真实鼠标键盘路线的独立人工 reviewer 结论；
- `REL-001` 形成冻结 release candidate，并确认最终安装前置、Build Settings 与 build-player 命令没有变化；
- 使用该 candidate 从干净目录按教程完整人工复走 Project Picker → Script → Add Component → Play → Build And Run。

