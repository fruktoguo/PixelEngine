# 首个工程：从新建场景到玩家包

本教程创建一个空工程、添加可观察的 Behaviour、在 Editor 中 Play，并构建可独立运行的 `win-x64 / R2R` 玩家包。开始前请完成 [Getting Started](getting-started.md)。

## 1. 新建工程

从仓库根启动 Project Picker：

```powershell
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release -- --no-reopen-last-project
```

选择 **New Project**：

1. `Project Name` 输入 `FirstProject`。
2. `Project Directory` 选择最终工程根，例如 `C:\PixelEngineProjects\FirstProject`。Editor 不会再自动追加项目名。
3. 点击 `Create Project`。

生成结构如下：

```text
FirstProject/
├─ project.pixelproj
├─ content/
│  └─ scenes/
│     └─ main.scene
└─ scripts/
```

Project 窗口会把 `content/` 显示为 `Content`，把 `scripts/` 显示为 `ScriptSource`，并标注文件类型和用途。

## 2. 创建对象和脚本

1. 选择 `GameObject > Create Empty`，在 Hierarchy 中选中新对象并将它命名为 `Hello`。
2. 在 Project 窗口选择 `ScriptSource`。
3. 使用 `New` → `Script`，命名为 `FirstProjectBehaviour`。
4. 双击脚本；若没有打开编辑器，在 `Edit > Preferences... > External Tools` 配置外部工具。
5. 用以下完整内容替换模板：

```csharp
using System;
using PixelEngine.Scripting;

public sealed class FirstProjectBehaviour : Behaviour
{
    protected override void OnStart()
    {
        Console.WriteLine("FIRST_PROJECT_STARTED");
    }

    protected override void OnGui(IGuiContext gui)
    {
        gui.SetNextWindow(24, 24, 320, 120, GuiCondition.FirstUseEver);
        if (!gui.BeginWindow("first-project", "First Project"))
        {
            gui.EndWindow();
            return;
        }

        gui.Text("Hello from PixelEngine!");
        gui.EndWindow();
    }
}
```

保存文件，等待 Console 显示脚本编译/热重载成功。选中 `Hello`，在 Inspector 的 `Add Component` 中选择 `FirstProjectBehaviour`。

## 3. 保存并 Play

1. 选择 `File > Save Scene`，或按 `Ctrl+S`。
2. 点击顶部 `Play`（快捷键 `Ctrl+P`）。
3. Game View 应显示标题为 `First Project` 的窗口，Console 应出现 `FIRST_PROJECT_STARTED`。
4. 点击 `Stop` 回到 Edit 状态。

Play 期间的运行态更改会在退出后恢复；需要保留的 authoring 修改应在 Edit 状态保存。

Scene View 显示的是独立 authoring 预览，Game View 才是运行输出。空白背景上的网格、边界和 `Hello` 对象标记是正常的编辑态结果。

## 4. 检查启动场景

构建前同时确认三处：

- `File > Project Settings...`：默认场景为 `scenes/main.scene`。
- `File > Player Settings...`：Startup Scene 为 `scenes/main.scene`。
- `File > Build Settings...`：`main.scene` 已入包并标为启动场景。

打开一个 scene 不会自动修改 Project StartScene，所以这一步不能省略。

## 5. 在 Editor 中构建

打开 `File > Build Settings...`，选择：

- RID：`win-x64`
- Channel：`R2R`
- Configuration：`Release`
- Product Name：`First Project`
- Output：例如 `artifacts/first-project-player`
- Scenes：包含 `main.scene`，并设为 startup

先用 `Build` 检查日志；成功后可用 `Build And Run`。相对 Output 当前相对于仓库根，而不是工程根。输出玩家位于：

```text
artifacts/first-project-player/player/First Project.exe
```

## 6. 用命令行复验

假设工程位于 `C:\PixelEngineProjects\FirstProject`，在 PixelEngine 仓库根运行：

```powershell
pwsh -NoProfile -File tools/build-player.ps1 `
  -Rid win-x64 `
  -Channel r2r `
  -Configuration Release `
  -Output artifacts/first-project-player `
  -ContentRoot C:\PixelEngineProjects\FirstProject\content `
  -ProductName "First Project" `
  -StartScene scenes/main.scene

& "artifacts/first-project-player/player/First Project.exe"
```

`build-player` 会把工程根的默认 `scripts/` 复制到玩家包 `content/scripts/`。因此本教程保持新工程默认的 `content/` 与 `scripts/` 布局。

可用 headless 方式确认玩家包确实执行了自定义脚本：

```powershell
Set-Location artifacts/first-project-player/player/app
dotnet PixelEngine.Demo.dll --content ..\content --headless --ticks 2 --no-hot-reload
```

成功输出应包含：

```text
随包脚本程序集已注册：...\content\scripts
FIRST_PROJECT_STARTED
Engine frame: 2, scene: main
```

## 7. 下一步

- 在 Scene View 中继续创建对象，并使用 Inspector 暴露字段。
- 把 texture、scene 和配置放入 `Content`；把 Behaviour 源码放入 `ScriptSource`。
- 使用 Demo 工程了解材质、反应、玩家控制、HUD 和完整落沙世界的公开 API 用法。

