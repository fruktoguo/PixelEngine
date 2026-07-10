# PixelEngine

PixelEngine 是一个面向 Noita 式世界模拟的高性能 2D 像素游戏引擎。仓库包含可复用引擎、独立 Editor，以及只通过公开 API 使用引擎的落沙游戏 Demo。

项目当前处于 **Windows-first 1.0 开发阶段**。核心模拟、独立 Editor、脚本热重载和 `win-x64` 玩家构建链已经可用；跨平台真机矩阵、发布签名和最终人工验收仍以 [canonical task catalog](plan/tasks/README.md) 为准。

## Windows 快速启动

需要 .NET 10 SDK、PowerShell 7、Git，以及带 MSVC v143、Windows SDK、CMake、Ninja 的 Visual Studio 2022 Build Tools。完整说明见 [Getting Started](docs/getting-started.md)。

```powershell
git submodule update --init --recursive
pwsh -NoProfile -File tools/build-native.ps1 -Rid win-x64 -Configuration Release
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln -c Release
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release -- --no-reopen-last-project
```

直接打开实例工程：

```powershell
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release -- --project demo/PixelEngine.Demo/project.pixelproj
```

Editor 会记住最近一次成功打开的工程；普通无参数启动会自动恢复它。`--no-reopen-last-project` 用于强制显示 Project Picker。

## 从哪里开始

- [安装、构建与 Editor 导览](docs/getting-started.md)
- [首个工程：从新建场景到 R2R 玩家包](docs/tutorial-first-project.md)
- [架构与需求设计](docs/PixelEngine-架构与需求设计.md)
- [产品定位](docs/PixelEngine-核心目标与产品定位.md)
- [任务状态](plan/tasks/README.md)
- [许可证](LICENSE)

