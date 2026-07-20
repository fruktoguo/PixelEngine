# Getting Started

本文覆盖 Windows x64 的安装版与源码工作流：安装并启动独立 Editor、准备开发环境、打开 Demo，并定位常见问题。

## 1. 前置条件

- Windows 10/11 x64；当前主要验证环境为 Windows 11。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，不是仅安装 Runtime。仓库 `global.json` 基线为 `10.0.108`，允许同 feature band 的更新版本。
- Git，并递归初始化 `native/box2d`、`native/rmlui`、`native/freetype` submodule。
- PowerShell 7，命令名为 `pwsh`。
- Visual Studio 2022 Build Tools：MSVC v143 C++ Build Tools、Windows SDK、CMake、Ninja。
- 支持 OpenGL 3.3 Core 的显卡与较新驱动。
- 首次 restore/publish 所需的 NuGet 网络访问。

OpenAL 音频设备不是 Editor 启动的硬前置；不可用时音频后端可降级。

## 2. 安装并启动 PixelEngine

手动测试首选安装包位于 `最终输出/安装器/PixelEngine-Setup-<version>-win-x64.msi`。双击 MSI 后可在安装向导中修改安装目录；默认安装到当前用户的 `%LocalAppData%\Programs\PixelEngine`，不需要管理员权限，也不要求目标机器预装 .NET。安装完成后可直接从桌面或开始菜单的 `PixelEngine` 快捷方式启动，不需要再进入源码目录查找 EXE。

卸载或修改安装可使用 Windows `设置 > 应用 > 已安装的应用 > PixelEngine`。卸载只删除安装 payload、产品快捷方式与安装登记，不删除用户工程或 Editor preferences。

仓库开发者可生成新的本机测试安装包：

```powershell
pwsh -NoProfile -File tools/build-windows-installer.ps1 -Configuration Release -OutputRoot 最终输出/安装器
```

本机构建的 MSI 未做 Authenticode 签名，Windows 可能显示未知发布者；它用于当前源码的本机开发测试，不等同于正式签名 Release。

## 3. 获取并构建源码

```powershell
git clone --recurse-submodules <repository-url> PixelEngine
Set-Location PixelEngine

pwsh -NoProfile -File tools/build-native.ps1 -Rid win-x64 -Configuration Release
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln -c Release
```

如果仓库已经 clone，但 submodule 不完整：

```powershell
git submodule update --init --recursive
```

RmlUi 固定在项目 fork 上可直接获取的 base commit；PixelEngine 自有的 framebuffer 集成修复由
`native/ui_native/rmlui-patches.json` 记录 base 文件与 patch SHA256，并由 CMake 在临时 overlay 中
校验后应用。不要把 `native/rmlui` 改指向只存在于本机的提交；新增集成改动应更新该清单与受控 patch。

## 4. 从源码启动 Editor

首次启动或希望明确进入 Project Picker：

```powershell
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release -- --no-reopen-last-project
```

直接打开 Demo：

```powershell
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release -- --project demo/PixelEngine.Demo/project.pixelproj
```

安装版用户入口始终是 `PixelEngine.exe`；`apps/PixelEngine.Editor.Shell` 只保留为源码项目与 namespace。旧式向 Demo 传 `--editor` 的方式不再使用。成功打开工程后，路径会进入最近工程列表，下一次普通启动会自动恢复上一个工程。

## 5. Editor 窗口心智模型

- **Hierarchy**：当前场景的 GameObject 层级。
- **Scene**：编辑态 authoring 预览；网格、世界边界、对象标记和名称不依赖 Play 状态。空场景只显示这些辅助内容，不代表渲染失败。
- **Game**：Play 后的真实运行画面。
- **Inspector**：选中对象或资产的属性，以及 `Add Component`。
- **Project**：两棵逻辑根。`Content` 保存 scene/config/texture 等运行内容；`ScriptSource` 映射工程的 `scripts/`，只有这里的 `.cs` 参与编译和 hot reload。
- **Console**：脚本编译、热重载、构建阶段和错误诊断。
- **Project Settings / Player Settings / Build Settings**：分别管理工程默认场景、玩家启动参数和入包场景/目标平台。

## 6. 运行 Demo 玩家包

初次使用建议 `win-x64 + R2R + Release`：

```powershell
pwsh -NoProfile -File tools/build-player.ps1 `
  -Rid win-x64 `
  -Channel r2r `
  -Configuration Release `
  -Output artifacts/demo-player `
  -ContentRoot demo/PixelEngine.Demo/content `
  -ProductName "PixelEngine Demo" `
  -StartScene scenes/lava-mine.scene

& "artifacts/demo-player/player/PixelEngine Demo.exe"
```

有限帧窗口冒烟可追加 `--window-ticks 120`。自动化 headless 冒烟使用 `--headless --ticks 2 --no-hot-reload`。

## 7. 常见问题

### native 配置找不到工具

确认安装了 MSVC v143、Windows SDK、CMake 和 Ninja，并从新的 PowerShell 会话重试。native 预检通过只表示工具入口可见，具体 toolchain 问题仍可能在 CMake 阶段出现。

### Editor 打开后没有实例场景

确认打开的是 `demo/PixelEngine.Demo/project.pixelproj`，而不是仓库根。Scene View 是编辑预览，Game View 需要进入 Play。可用 Scene 工具栏的 `Frame All` 或选中 Hierarchy 对象后使用 `Frame Selected`。

### Script 创建了但 Add Component 找不到

脚本必须位于 Project 窗口的 `ScriptSource` 根。等待 Console 报告编译/热重载成功；双击脚本无法打开时，在 `Edit > Preferences... > External Tools` 配置外部编辑器。

### publish/ILLink 报文件被映射或占用

不要并行启动同一 RID/channel 的多个 publish，也不要强制终止正在裁剪程序集的构建。同步盘或杀毒软件可能短暂锁定 `obj/.../linked`；确认残留 `dotnet publish` 已退出后重试。持续发生时把仓库放到非同步目录。

### 为什么自定义脚本教程只选 R2R

R2R 玩家包可在启动时编译随包的 `content/scripts`。NativeAOT 不支持动态代码，外部工程脚本需要独立静态编译链后才能使用；当前不要把 AOT 当作首个自定义工程的默认渠道。

下一步：[完成首个工程](tutorial-first-project.md)。
