# EDITOR-010 VS Code 默认脚本工程工作流证据

Evidence Index: `editor-010-vscode-project-workflow-20260711`

## 实现结论

- Editor Preferences 升级到 v2：新安装与 v1 空值安全迁移均默认使用 VS Code；Visual Studio、JetBrains Rider、System Default 与 Custom Command 使用稳定 sentinel，不再混淆“空值默认程序”和“默认 VS Code”。工程文件中的旧自定义 executable 不会被静默执行。
- Windows IDE locator 覆盖 PATH、VS Code User/System Installer、注册表、`vswhere` 与 Rider/Toolbox；自动路径只接受真实 executable。`code.cmd` 会收敛到相邻 `Code.exe`，Rider Toolbox shim 会继续寻找 `rider64.exe`，自定义裸命令先从 PATH 解析成绝对路径，拒绝依赖工程工作目录的相对目录劫持。
- Project Window 双击脚本与 Console source location 共用工程上下文：VS Code 使用 `--reuse-window <folder/workspace> --goto <file>:<line>:<column>`；Visual Studio/Rider 打开包含当前项目的 solution 后定位脚本。Roslyn 热重载诊断保留文件、行、列，Console 双击可准确跳转。
- `Assets > Open C# Project` 已接入主菜单、本地化与 Console。VS Code 打开工程根或根级 `.code-workspace`；Visual Studio/Rider 打开真正包含当前 `.csproj` 的当前根/祖先 `.sln/.slnx`。Demo 直接复用 `PixelEngine.Demo.csproj` 与仓库 `PixelEngine.sln`。
- standalone 工程无 IDE 文件时生成带 ownership marker 的 Library `.csproj/.sln`，显式包含 `ScriptSource/**/*.cs`、使用稳定 GUID、内容不变不重写。任何用户维护的 `.csproj/.sln/.slnx` 均只读；损坏 solution 会安全回退且不覆盖原文件。
- 正式编辑器包新增 `编辑器/ScriptReferenceAssemblies/` 产品 SDK：13 个 PixelEngine runtime primary DLL 各带 XML IntelliSense 文档，并带齐 editor publish 的 managed dependency。SDK 不含 Editor/Shell、native 或 PDB；verifier 从编辑器运行目录独立推导 dependency，并要求 runtime、manifest、SDK 三方精确一致。

## 真实 IDE 与工程证据

- 当前 Windows 环境的默认 locator 实际解析到：
  - VS Code：`C:\Users\YuoHira\AppData\Local\Programs\Microsoft VS Code\Code.exe`（PATH 入口为 `bin\code.cmd`）。
  - JetBrains Rider：`C:\Users\YuoHira\AppData\Local\Programs\Rider\bin\rider64.exe`（PATH 入口为 Toolbox `Rider.cmd`）。
  - Visual Studio 2022 Professional：`C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe`。
- `StandaloneProjectGeneratesStableBuildableIdeModel` 在仓库外临时工程中真实执行 `dotnet build`，结果 0 warning / 0 error；生成工程只通过发行 SDK `HintPath` 引用引擎，不依赖源码树 `src/*.csproj`。
- `DemoProjectReusesExistingProjectAndContainingAncestorSolution` 验证 Demo 不生成平行工程文件，并复用祖先 solution。
- 自定义命令覆盖带空格 executable、`{file}/{line}/{column}/{project}/{solution}/{workspace}`、无 file placeholder 自动追加，以及 script-only 命令转 Open C# Project 时移除定位参数并追加工程根。

## 自动化与审查

- EDITOR-010、Console、Preferences 与 FinalOutput focused：77/77 passed；发布门禁加固后 `FinalOutput*`：11/11 passed。
- `PixelEngine.Hosting.Tests` 最新全量：624 passed / 4 个 native GL 条件 skipped / 0 failed。
- `dotnet build PixelEngine.sln -c Release --no-restore -p:TreatWarningsAsErrors=true`：0 warning / 0 error。
- `tools/validate-task-catalog.ps1`：Task catalog valid；79 个 canonical task，本任务收口前 1 个 active。
- 两路独立审查最终均为 GO：IDE/工程审查真实探测三种 IDE 并复核用户文件只读、owned 文件原子幂等；打包审查先真实复现“manifest 与 SDK 同删 dependency”及缺失 symbol-policy 绕过，修复后通过 mutation 回归与二次复核。
- `git diff --check`：实现范围通过；用户自有 `project.pixelproj` 与 `lava-mine-copy.scene` 未暂存、未覆盖。

## 正式输出

- 实现提交 `ab0a57e788e7de86b29b71419f8bf313cd916f34` 在 detached clean worktree 中完成首轮官方发行验证：`win-x64`、Release、R2R、RmlUi。
- Editor 默认工作台 probe：`completed=True`、`succeeded=True`、22 个必需面板、脚本创建/热重载/Behaviour 挂载、Scene 保存、Play 进出和 Build Player 全部成功。
- Demo 正式窗口 probe：`PixelEngine Demo.exe` 完成 80/80 tick，输出 `window_frame_probe`，正式 build-result 为 `ok=true`，无 warning。
- 脚本 SDK：13 primary、22 managed dependencies，共 35 DLL / 13 XML / 0 PDB；独立 verifier 返回 `ok=True`。
- `SHA256SUMS` 首轮覆盖 319 个文件。任务完成提交后按同一官方流程重新生成正式输出，最终 `_验证记录/manifest.json.gitCommit` 必须与最终 HEAD 精确一致，不使用 `AllowCommitMismatch`。
