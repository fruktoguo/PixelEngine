# Plan 19 — 独立编辑器应用（PixelEngine.Editor.Shell）

> 本文件定义 PixelEngine 的**独立编辑器应用**：一个拥有独立可执行文件（独立 EXE）、独立顶层窗口、单独进程的类 Unity/Godot 编辑器。它是一个顶层壳应用（shell），在**自身进程内 in-process 宿主一个引擎实例**，用于 Edit / Play 两种模式编辑场景；最终发布给玩家的是**另一个不含编辑器的独立 player 产物**。同时承载「编辑器内一键出包（Build Settings 面板）」这一节（§5，in-editor-build 集群落地家）。
> 里程碑归属：**M13「编辑器独立化与发行解耦」**。其**入口门 = §0 GUI 宿主中性化重构**（新增中性程序集 `PixelEngine.Gui`），plan/19 壳注入、plan/15 玩家包 player-only 审计、plan/20 `PixelEngine.UI` 字体/回退复用三者共用此前置。
> 权威依据：`../AGENTS.md`（§1 十条不变式含**新增 #10**、§2 一步到位无 MVP）、`00-conventions-and-techstack.md`（§5 依赖方向/解决方案结构）、`12-editor-tooling-ui.md`（编辑器面板层）、`18-hosting-runtime.md`（Engine/主循环/Play-Edit-Step/窗口所有权解耦 API）、`11-scripting-system.md`（GameObject/Behaviour/Scene 模型）、`08-rendering.md`（GL 上下文/离屏 FBO/UI 层注册接口）、`15-build-packaging-distribution.md`（§3.11 build-player 编排器 + player-only 审计）。
> 状态：`- [ ]` 未开始 / `- [x]` 完成并自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 0. 入口门：GUI 宿主中性化重构（M13 强前置，必须先行）

### 0.1 背景：为什么 plan/19 以一次重构开篇

plan/19（独立编辑器壳）、`plan/15`（玩家包 player-only 审计）、`plan/20`（`PixelEngine.UI` 复用字体/回退基线）三者有一个**共同的结构前置**：把「玩家 HUD 也需要的 ImGui 宿主」从 `PixelEngine.Editor` 中剥离、下沉到一个**中性程序集**。本节定义该重构，作为 **M13 的入口门**——壳注入（§4.4）、玩家包解耦（§4.12、§5.7）、UI 字体栈复用（`plan/20`）都以它为前提，必须先落地。

### 0.2 现状耦合事实（纠正版，已核对代码）

- `demo/PixelEngine.Demo` 对 `PixelEngine.Editor` **没有直接 `ProjectReference`**（`PixelEngine.Demo.csproj` 仅引用 `PixelEngine.Hosting` 与 `PixelEngine.Scripting`）。
- 真实耦合是**传递闭包**，共三条路径：
  - (a) `PixelEngine.Hosting.csproj:13` 硬 `ProjectReference PixelEngine.Editor`（默认传递给 Demo，使 `DemoProgram.cs:7` 的 `using PixelEngine.Editor` 可编译）；
  - (b) Hosting 内多处源文件（含 `Engine.cs` 的 `EnableEditor` / 编辑器装配路径）直接消费 Editor 类型；
  - (c) 玩家 HUD 经 `IGuiContext → ScriptGuiContext`（现位于 `PixelEngine.Editor`）`→ HexaImGuiBackend`（现位于 `PixelEngine.Editor`）取得 ImGui 渲染能力。
- 因此「玩家包不含编辑器」**不能**靠「删 Demo 的 Editor 项目引用」实现（它本就没有这条引用），必须切断上述传递闭包；同时**不能**顺手把玩家 HUD 依赖的 ImGui 核心一并删掉（早期「玩家包拒绝一切 ImGui」的表述不可满足，已按不变式 #10 撤销）。

### 0.3 新增中性程序集 `PixelEngine.Gui`

新增 `src/PixelEngine.Gui`，层级位于 **Rendering 之上、Editor 之下**。把「玩家 HUD 与编辑器共用、本质中性」的 ImGui 宿主设施从 `PixelEngine.Editor` 下沉进来：

- `HexaImGuiBackend`（ImGui 后端 + GL 绘制桥）——由 `src/PixelEngine.Editor/HexaImGuiBackend.cs`、`EditorRenderBridge.cs` 的中性部分迁入。
- `IGuiContext` 运行时适配，即现 `ScriptGuiContext`（玩家 HUD / 脚本 UI 走的即时上下文）——`ScriptGuiContext.cs` 迁入，归属改为 `PixelEngine.Gui`。
- `EditorRenderBridge` 的中性部分（相位 [10] present 前 ImGui 帧生命周期、`WantCaptureMouse` 输入桥中性壳）；编辑器专属绘制（停靠、面板遍历）留在 Editor。
- 字体栈：`EditorFontManager → GuiFontManager`（含 CJK 字形装载），供玩家 HUD 与 `plan/20` UI 回退基线复用。

留在 `PixelEngine.Editor` 的仍是**编辑器专属**：`EditorApp` / `IEditorPanel` / 停靠 / 各面板 / `ImGuizmo` / `ImPlot` 绑定。

### 0.4 Hosting 去 Editor 硬引用 + 抽象 GUI/相位[10] 钩子接口

- `PixelEngine.Hosting.csproj` **删除**对 `PixelEngine.Editor` 的 `ProjectReference`（现第 13 行），改为引用 `PixelEngine.Gui`；`Hosting→Gui` 成立，`Hosting↛Editor`。
- Hosting 内原直接使用 Editor 类型的路径改为面向**中性抽象**：Hosting 暴露 `IGuiHostHook` / 相位 [10] GUI 扩展点接口（在 Hosting 声明、由 Gui/Editor 分别提供运行时与编辑器实现），运行时 HUD 用 Gui 的 `IGuiContext`；编辑器面板宿主由外部注入，Hosting 不再静态知道 Editor。
- 编辑器实现（`EditorApp` + 默认面板注册，即原 `Engine` 私有的 `RegisterDefaultEditorPanels` 那条路径）保留在 `PixelEngine.Editor` 面板层；真正实现 `IEditorHostExtension` 的适配器放在 `apps/PixelEngine.Editor.Shell`，由编辑器壳（开发构建）在装配后注入 Hosting 的相位 [10] 钩子。玩家运行时永不注入，闭包中不出现 Editor。

### 0.5 玩家包解耦的真正动作（纠正版）

解耦 = (a) §0.3 中性化下沉 + (b) `Hosting.csproj` 删 Editor 引用改引 `Gui` + (c) `DemoProgram.cs` 去掉 `using PixelEngine.Editor` 与 `EnableEditor` 进程内编辑器路径、玩家 HUD 改用 `PixelEngine.Gui` 中性 host、编辑器托管职责整体迁往 shell。三步做完后，玩家闭包 `Demo→Hosting→{…,Gui}` **不再传递到 Editor**，player-only 成为**结构事实**而非打包期裁剪。原基于 Demo `EnableEditor` 的 editor-window 证据（`editor_enabled` / `editor_running` / `editor_panels` / `editor_bridge_frames`）等价迁移到 shell 的 `--window-ticks` / scripted-probe（见 §4.12、§7）。

### 0.6 作为三计划的共同前置（顺序约束）

- `plan/19`：shell 通过 §0.4 的 `IEditorHostExtension` 注入把 Editor 面板挂到 Hosting 相位 [10]（§4.4）；本重构不落地则 shell 无法在「Hosting 不引用 Editor」的前提下宿主编辑器。
- `plan/15`：玩家包审计「`app/` 内无 `PixelEngine.Editor.dll`，但**允许**玩家 HUD 所需 `Hexa.NET.ImGui` 核心」以本重构落地为**前置**（§5.7）。
- `plan/20`：`PixelEngine.UI` 复用 `GuiFontManager`（含 CJK）与 `IGuiContext` 回退基线（`ManagedFallbackBackend` 与 Gui 共栈）。

### 0.7 依赖方向定稿（同步写入 `plan/00 §5`、纠正 `plan/18` 文档方向）

`{Demo(玩家运行时), apps/PixelEngine.Editor.Shell(编辑器顶层)} → Hosting → { Scripting, Rendering, Audio, Physics, World, Serialization, Content, Simulation, UI, Gui } → Interop → Core`。

补边：`Editor→{Gui, 子系统}`；`EditorShell→{Hosting, Editor, Gui}`；`UI→{Gui, Rendering, Core}`；`Hosting→Gui`（**不再** `→Editor`）；`Demo→Hosting`（+可选 `UI`，**不含** Editor）。

### 0.8 §0 实现清单

- [x] 新增 `src/PixelEngine.Gui` 中性程序集（位于 Rendering 之上、Editor 之下），加入 `.sln` 与 `plan/00 §5` 结构图（§0.3、§0.7）
- [x] 迁移 `HexaImGuiBackend` + `EditorRenderBridge` 中性部分到 `PixelEngine.Gui`，编辑器专属绘制留 Editor（§0.3）
- [x] 迁移 `ScriptGuiContext`（`IGuiContext` 运行时适配）到 `PixelEngine.Gui`，玩家 HUD/脚本 UI 引用改指 Gui（§0.3）
- [x] `EditorFontManager → GuiFontManager`（含 CJK），玩家 HUD 与 `plan/20` UI 复用（§0.3）
- [x] `PixelEngine.Hosting.csproj` 删 `PixelEngine.Editor` 引用、改引 `PixelEngine.Gui`；Hosting 内 Editor 类型消费改为面向 `IEditorHostExtension`/相位[10] 抽象（§0.4，配合 `plan/18` 修订）
- [x] `IEditorHostExtension`：Shell 侧 adapter 封装 `EditorApp` + 默认面板注册（原 `RegisterDefaultEditorPanels`），由 shell 开发构建注入 Hosting 相位[10] 钩子（§0.4）
- [x] `DemoProgram.cs` 去 `using PixelEngine.Editor` 与 `EnableEditor` 路径，玩家 HUD 改用 `PixelEngine.Gui` 中性 host（§0.5、`plan/13` 修订）
- [x] 验证玩家闭包（`Demo→Hosting→{…,Gui}`）不再传递到 `PixelEngine.Editor`（§0.5、`plan/00 §8` 复核）

---

## 1. 目标与范围

交付一个像 Unity Editor / Godot Editor 一样、双击即打开的**独立编辑器应用**。用户打开后进入一个独立编辑器窗口，在其中以类 Unity 的方式操作 GameObject、编辑场景，并使用场景层级、Inspector、资源浏览、脚本、材质、世界等图形化面板，以及**编辑器内 Build Settings 一键出包**（§5）。

范围内：

- 新增独立可执行项目 `apps/PixelEngine.Editor.Shell`（新增顶层目录 `apps/`）：拥有 `Main`、窗口与进程生命周期、编辑器专属主菜单栏与默认 dock 布局。
- Project（工程）模型：新建工程 / 打开工程 / 最近工程列表（持久化）/ 工程根与内容根解析。
- 在编辑器进程内 in-process 宿主引擎（复用 `PixelEngine.Hosting` 的 `Engine`/`EngineBuilder`/Play-Edit-Step），Edit 模式默认暂停 sim、可编辑；Play 模式在同一进程、同一窗口、同一 GL 上下文内运行游戏。
- 类 Unity 的 GameObject 操作：层级面板（创建/删除/重命名/复制/拖拽重父）、GameObject Inspector（名称/启用位/Transform TRS/组件增删改）、场景视图内变换 gizmo（`Hexa.NET.ImGuizmo`）与点选拾取。
- 场景保存格式与序列化往返（`.scene` 读写闭环、schema 升版容纳层级与 Transform，`FormatVersion 1→2` 且保 v1 兼容）。
- Prefab（预制体）**完整实现**：资产 + 实例 + override + **嵌套** + override 传播。
- 编辑器内构建与发布（§5）：`BuildSettingsPanel` 一键出**不含编辑器的玩家包**，起子进程调 `plan/15 §3.11` 的 build-player 管线。
- 复用 `plan/12` 既有面板：世界画刷、世界检视器、调试叠层、性能 HUD、材质+反应实时编辑器、资源浏览器、sim 控制条、存读档、子系统调参、Edit/Play 模式切换。
- 玩家包与编辑器解耦：发布产物是不含 `PixelEngine.Editor` 的独立 player（真正动作见 §0.5）。

**运行时层级语义边界（刻意取舍，收口「类 Unity GameObject 逻辑」的预期）**：parent/child 为**纯 authoring 概念**；物化时把父链复合**烘焙为世界 TRS**，运行时 `Scripting.Scene` 保持**扁平 DOD、无 live 父子变换传播**（不引父子层级污染 `plan/11` 热路径）。层级、命名、启用位、prefab 全部是编辑器/文档层语义，运行时只见展开后的扁平实体。

范围外（仅消费其公开 API，不在此实现）：ImGui 面板实现本体（`plan/12`）；ImGui 中性宿主（§0，`PixelEngine.Gui`）；Engine 主循环/相位编排/子系统装配（`plan/18`）；脚本编译与 Behaviour 生命周期（`plan/11`）；渲染管线与 GL 上下文（`plan/08`）；打包脚本管线本体（`plan/15`，本文件只提出解耦契约并**消费** build-player）。

---

## 2. 与 plan/12 锁定决策的调和（reconcile）

`plan/12 §1` 锁定：「编辑器 = 引擎内嵌 Dear ImGui 停靠式面板，与 `plan/08` 共享同一 OpenGL 上下文，**绝不另起进程 / 另开窗口 / 另建 GL 上下文**」。本需求要求「独立 EXE、独立窗口、单独进程」，表面直接冲突。采用 **Unity 式模型**后冲突消解：

采用模型：独立编辑器 EXE = 顶层壳应用（`apps/PixelEngine.Editor.Shell`），它在**自身进程内 in-process 宿主一个引擎实例**用于 Edit/Play；被编辑的游戏并不运行在另一个进程/另一个窗口/另一个 GL 上下文里，而是运行在**编辑器进程自己的那个窗口、那个 GL 上下文**里，ImGui 面板依旧在世界渲染（相位 [10]）之后、present 之前叠加绘制——与 `plan/12` 的集成方式逐字一致。

因此 `plan/12` 的「绝不另起进程/另开窗口/另建 GL 上下文」这一约束的**真正语义是**：编辑器与其所编辑的游戏共享同一进程/同一窗口/同一 GL 上下文，**绝不为运行游戏而 spawn 第二个进程或第二个 GL 上下文**。这一语义在编辑器进程内**仍然完全成立**：shell 只创建一个 `RenderWindow`、一个 GL 上下文；Edit 与 Play 都在其上；不 fork 子进程去跑游戏，不开第二个渲染窗口。（编辑器内出包 §5 起的 `build-player` 子进程是**外部工具编排**，不是「为运行被编辑游戏而 spawn 第二 GL 上下文」，不违反本约束。）

真正需要改的只有一点：`plan/12` 的隐含前提是「编辑器是 `demo/PixelEngine.Demo` 里的一个 `EnableEditor` 开关」——即编辑器寄生在 Demo 进程里、随 Demo 启动。本需求把它**升级为一个独立顶层可执行项目**（有自己的 `Main`、窗口、菜单、项目管理），并把**发布产物与编辑器解耦**（玩家包不含编辑器，§0）。这两点都不触碰「共享同一进程/窗口/GL 上下文」这条真约束，只是把「谁是宿主进程」从 Demo 换成 shell。

结论：reconcile 成立。落地动作是（a）`plan/12 §1` 措辞修订（把「Demo 里的开关」替换为「面板层内嵌、宿主进程由独立 shell 承载」，并显式声明该约束是相对被编辑游戏而言）；（b）§0 GUI 宿主中性化，使 shell 可在 `Hosting↛Editor` 前提下注入编辑器；（c）玩家包解耦。不存在需要放弃 ImGui 内嵌模型、改用外部 IPC/多进程 UI 的情形。

（补充：若未来要做「编辑器进程外挂、游戏进程独立运行」的 attach-debugger 式远程编辑，那才会真正违反 `plan/12`；本需求不包含该形态，明确不采纳，以守住单进程单 GL 上下文的性能与简单性。）

---

## 3. 技术栈与依赖

- 与 `plan/00 §4` 一致，不另立选型：窗口/输入/GL 用 `Silk.NET`（复用 `plan/08` 的 `RenderWindow`）；ImGui 宿主用 `PixelEngine.Gui` 中性 host（§0，其内 `HexaImGuiBackend`）；编辑器面板/停靠用 `PixelEngine.Editor`（`Hexa.NET.ImGui` + Backends）；变换 gizmo 用 `Hexa.NET.ImGuizmo`（`plan/00`/`plan/12` 已登记为备用，本文件转正为**必需**）；序列化用 `System.Text.Json` 源生成（复用 `EngineSceneJsonContext`，扩展 prefab/最近列表/构建设置上下文）。
- 目标框架/语言：`.NET 10 / C# 14`，`Nullable enable`，file-scoped namespace，命名空间根 `PixelEngine.Editor.Shell`。
- 依赖方向（§0.7，绝不反向）：`apps/PixelEngine.Editor.Shell → { PixelEngine.Hosting, PixelEngine.Editor, PixelEngine.Gui } → 各子系统 → Interop → Core`。EditorShell 与 `demo/PixelEngine.Demo` 同层（均为顶层 app），只依赖引擎公开 API；被依赖方绝不反向依赖 shell。
- native 依赖：shell **不新增任何 native 依赖**，Box2D 仍是唯一 dual-build native 依赖（不变式 #10），经引擎间接使用；`ImGuizmo`/`ImPlot` 为托管绑定的编辑器专属项，只入编辑器闭包、不入玩家包。
- 解决方案结构：新增顶层 solution 文件夹 `apps/`，其下 `apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj`（`OutputType=Exe`）。

---

## 4. 详细设计

### 4.1 顶层壳应用与进程/窗口生命周期（`EditorShellApp`）

`apps/PixelEngine.Editor.Shell/Program.cs` 的 `Main` 是编辑器唯一进程入口：解析命令行（可选 `--project <dir>` 直接打开工程、`--scene <path>`、`--window-ticks <n>`/scripted-probe 用于机器验收，见 §7）、构造 `EditorShellApp`、进入主循环、异常写崩溃日志后返回退出码。

**锁定启动时序（纠正版，消除「可能尚未创建 GL 窗口」的二选一）**：

1. `Main → EditorShellApp.Run()` 启动即**立起唯一的 `RenderWindow` + GL 上下文 + `PixelEngine.Gui` 中性 ImGui host**（`EditorShellWindow` 拥有并管理其生命周期）。这一步**独立于 Engine 装配**——窗口/上下文归 shell 所有，而非归某个 `Engine` 实例。
2. 未打开工程时，主循环在**该已存在的 GL 上下文内**用中性 ImGui host 绘制**项目选择器**（`ProjectPickerWindow`，§4.2）。不存在「空窗口 vs 无窗口」的含糊：窗口此时已实实在在存在。
3. 用户选定/新建工程后，`EditorProjectSession` 装配 **Edit 模式 `Engine`**，并调用 `plan/18` 新增的**窗口所有权解耦 API** 让 Engine **attach 到 shell 已拥有的既有窗口**（Engine **不 own** 该窗口，`Engine.Dispose()` **不销毁**该窗口）。随后 shell 经 `IEditorHostExtension`（§0.4）把 Editor 面板注入 Hosting 相位 [10] 钩子。
4. 关闭工程/切换工程：逆序释放 `Engine`（复用 Hosting `Engine.Dispose()` 逆序关闭），**窗口与 GL 上下文保留**、`Engine.Dispose` 因不 own 窗口而不触碰它；shell 回到项目选择器或重建 session。
5. 进程退出时由 shell（`EditorShellWindow`）释放窗口与全部 native 资源。

`EditorShellApp`（shell 顶层门面）持有：`EditorShellWindow`（唯一 `RenderWindow` + GL 上下文 + 中性 ImGui host 的封装，设置窗口标题 `PixelEngine Editor — <工程名> — <场景名>[*]`，星号表示未保存）、`EditorProjectSession`（当前打开工程 + in-process `Engine`）、`EditorMainMenuBar`、`RecentProjectsStore`、`EditorShellLayout`（默认 dock 布局 + 布局持久化）。

**单进程单窗口不变量**：整个 shell 生命周期内**至多一个 `RenderWindow`、一个 GL 上下文**；切换工程只重建 `Engine` 逻辑对象与其 attach，绝不重建 GL 上下文（守住 §2 的真约束）。

### 4.2 Project（工程）模型：新建 / 打开 / 最近列表（`EditorProject` / `RecentProjectsStore`）

`EditorProject` = 工程根目录 + `content/`（materials/reactions/纹理/音效/`scenes/`）+ 脚本源目录（供 `plan/11` 热重载）+ 起始场景引用。物理落地：工程根下一个 `project.pixelproj`（JSON：`FormatVersion`、`Name`、`ContentRoot`（默认 `content`）、`ScriptSourceDir`、`StartScene`、`Scenes[]`）。`EditorProject` 与 Hosting 的 `EngineProject`/`SceneDescriptor` 对齐：打开工程时把 `EditorProject` 转成 `EngineProject` 喂 `EngineBuilder.WithProject(...)`。

> 事实校准：默认启动场景是 **`playable-world.scene`**（Procedural 生成器键 `PlayableWorldDirector`），`EngineProject.Scenes` 现仅含单场景。因此 §4/§5 一切「场景清单」的数据源以**扫描 `content/scenes/`** 为准（与 `EngineProject.Scenes` 声明合并去重），不假设 `EngineProject.Scenes` 已列全。熔岩矿洞逃生（`lava-mine`）是 `plan/13` 本轮**新增**内容，不是默认启动场景。

`RecentProjectsStore`：把最近打开工程路径（含名称、最后打开时间）持久化到用户配置目录（`%APPDATA%/PixelEngine/recent-projects.json` 等平台等价路径），启动项目选择器读取展示。

项目选择器（`ProjectPickerWindow`，在 §4.1 步骤 2 的中性 ImGui host 上下文内绘制）：三块——「新建工程」（选目录 + 工程名，生成 `project.pixelproj` + 空 `content/` 骨架含空 `content/scenes/main.scene`）、「打开工程」（目录选择，校验 `project.pixelproj`）、「最近工程」（列表，双击打开，缺失项标灰）。数据流：选择 → `EditorShellApp.OpenProject(EditorProject)` → 构造 `EditorProjectSession`（触发 §4.1 步骤 3 的 Engine attach）。

### 4.3 编辑器主菜单栏与默认 dock 布局（`EditorMainMenuBar` / `EditorShellLayout`）

`EditorMainMenuBar`（`ImGui.BeginMainMenuBar`，在 `EditorDockSpace` 之上）：

- File：New Project / Open Project / Open Recent ▸、New Scene / Open Scene / Save Scene (Ctrl+S) / Save Scene As、**Build Settings…**（打开 §5 面板）、Exit。
- Edit：Undo / Redo（作用于 GameObject authoring 命令栈，见 §4.5）、Delete、Duplicate。
- GameObject：Create Empty、Create Empty Child、Create with Component ▸（列出已注册 Behaviour 类型）、Rename、Delete。
- Window：各面板可见性开关（复用 `EditorApp.TryShowPanel(title)`，含「构建与发布」）、Reset Layout（恢复默认 dock 布局）。
- Play：Play / Pause / Step（复用 `SimulationControlToolbar` 语义，绑定同一 `EngineSimulationControlService`）。
- Help：关于、快捷键。

`EditorShellLayout`：定义编辑器默认停靠布局——左「Hierarchy」；中「Scene View（世界视口）」；右「Inspector」上、「材质/反应编辑器 + 世界画刷调色板」下；底「Project（资源浏览）+ Console + 性能 HUD + 构建与发布」。布局保存/恢复复用 `EditorAppOptions.LayoutPath`（`imgui.ini`），Reset Layout 重建默认停靠。

### 4.4 in-process 宿主引擎（Edit / Play 两模式）（`EditorProjectSession`）

`EditorProjectSession` 用 `EngineBuilder` 装配引擎：`.WithProject(engineProject).UseVSync(true).Build()`，随后调用 `engine.AttachWindowRuntime(shellWindow)`（**attach 到 shell 既有窗口、Engine 不 own**，见 §4.1 步骤 3 与 `plan/18` 窗口所有权解耦 API），再由 shell 经 `IEditorHostExtension`（§0.4）注入 Editor 面板宿主到相位 [10]，默认进入 **Edit 模式**（sim 暂停）。与 Demo 玩家路径的差别：宿主是 shell、窗口归 shell、Editor 面板经外部注入（Hosting 本身不再引用 Editor）、额外注册 GameObject 编辑面板（§4.6–4.8）。

复用点（均为 `plan/18`/`plan/12` 既有能力，本文件只调用不重造）：Play/Edit/Step 三态与快照回滚经 `EngineEditorPlaySessionService` + `EngineWorldSnapshotStore`；sim 控制经 `EngineSimulationControlService`；世界画刷/检视器/叠层/HUD/材质编辑/存读档/调参面板经 `IEditorHostExtension` 默认面板注册（原 `RegisterDefaultEditorPanels`，§0.4 迁至 Editor）注册的既有面板。

Hosting 侧须补的公开 API（列入 `plan/18` 修订，见 §8）：(1) **窗口/GL 上下文所有权解耦**——`AttachWindowRuntime(externalWindow)` 路径，Engine 复用外部窗口/上下文且 `Engine.Dispose()` 不销毁它；(2) **公开编辑态 bootstrap**——`EditorHostBootstrap` 在 Engine 前立起中性窗口/Gui host，`IEditorHostExtension` 注入点由 shell adapter 承接默认面板注册；(3) **`.scene` 文档保存 API**（`SaveSceneDocument(EngineSceneDocument, path)`，§4.9）；(4) `EngineSceneDocument`→运行时物化边界 API。上述 (2) 相关的 Editor 耦合已由 §0 GUI 中性化处理，Hosting 不再静态依赖 Editor。

Edit 模式语义：sim 暂停（相位 [3]–[8] 跳过，[9]–[10] 继续出帧，复用 `plan/12 §3.12` 语义），世界画刷/gizmo/拾取生效；Behaviour 的 `OnUpdate` 不派发（`ScriptRuntime` 仅在 Play 的相位 1 派发）。进入 Play：`EngineEditorPlaySessionService.EnterPlay()` 快照 world+script → 物化 authoring 模型到运行时 Scene（§4.5）→ 运行；退出 Play：回滚到 Edit 态，运行期对 GameObject 的改动被丢弃（类 Unity）。

### 4.5 Unity 式 GameObject / authoring 场景模型（`EditorSceneModel` / `EditorGameObject`）

核心设计判断：**编辑态的真相源是一个 authoring 模型，而非运行时 `Scripting.Scene`**（与 Unity 一致——编辑器编辑场景资产，进入 Play 才实例化）。运行时 `Scripting.Scene` 保持扁平 DOD、不引入父子层级（不污染 `plan/11` 热路径，见 §1 层级语义边界）；层级/命名/Transform 默认挂载等「编辑器语义」全部落在 authoring 模型里。

`EditorSceneModel` 持有一棵 `EditorGameObject` 树：

- `EditorGameObject`：`StableId`（int，稳定，对齐 `EngineSceneEntityDocument.StableId`）、`Name`、`Enabled`、`ParentId`（0=根）、`Children`、`Transform`（TRS：位置 X/Y、旋转、缩放，对齐 `Scripting.Transform` 字段）、`Components`（`EditorComponentModel` 列表）、可选 `PrefabLink`（§4.10）。
- `EditorComponentModel`：`TypeName`（Behaviour 全名）、`SerializedFields`（字段名→字符串值，对齐 `EngineSceneBehaviourDocument.SerializedFields`）。每个 GameObject 默认含一个 `Transform` 组件（不可删），对齐「Unity GameObject 恒有 Transform」。

**authoring StableId → 运行时 Entity.Id 映射策略**：物化时对每个 `EditorGameObject.StableId` 在 `Scripting.Scene` 建实体，维护一张 `StableId → Entity.Id` 的显式映射表（不假设二者数值相等）。拾取联动（Scene View 命中运行时投影 → 反查 StableId → 选中 authoring 节点）与 Play 快照回滚（回滚后按同一映射表重建关系）都走该表，避免错位。

authoring↔文档：`EditorSceneModel ⇄ EngineSceneDocument`（§4.9 扩展 schema：`EngineSceneEntityDocument` 增 `ParentId` 与 `Transform` TRS 块；本文件选「实体级 Transform 块 + ParentId」以显式表达层级）。

authoring→运行时物化（进入 Play 或 Edit 态实时投影）：遍历 `EditorSceneModel`，对每个 `EditorGameObject` 在 `Scripting.Scene` 上 `CreateEntity()`，为其 `AddComponent(typeof(Transform))` 并写入 TRS，再按 `Components` 逐个 `AddComponent(type)` 并绑定字段（复用 `EngineSceneDocumentLoader.BindSerializedFields` 等价逻辑，扩展支持 Transform TRS 与 `Vector2`）。父子层级在运行时以「子的 Transform 世界坐标 = 父链复合」在物化时**烘焙为世界 TRS**（运行时不保留 parent 指针，保持扁平）；编辑器内则保留 parent 供层级/gizmo 使用。

Edit 模式实时投影：为让 Scene View 的 gizmo/拾取显示「活的」GameObject，Edit 态维护一个与 authoring 模型同步的只读运行时 `Scripting.Scene` 投影（结构性编辑同时改 authoring 模型 + 重建投影；Behaviour 不 tick）。source of truth 始终是 authoring 模型；保存/Play 都从它出发。

命令栈（Undo/Redo）：所有结构性/属性编辑（创建/删除/重父/重命名/加删组件/改字段/改 TRS）走 `EditorCommand` 命令对象入 `EditorUndoStack`，支持撤销/重做，脏标记驱动窗口标题星号与保存提示。

### 4.6 层级面板（`GameObjectHierarchyPanel`）

替代/升级 `plan/12` 的 `SceneHierarchyPanel`（后者只读展示运行时实体+刚体，实体名合成为 `script:{id}`）。新面板以 `EditorSceneModel` 为数据源，渲染 Unity 式可展开 GameObject 树：

- 显示 `Name` + 启用位复选框；折叠三角展开子节点；选中 → 写 `EditorSelection`（联动 Inspector 与 Scene View）。
- 右键上下文菜单：Create Empty（作为选中项子节点或根）、Create with Component ▸、Rename（就地编辑）、Duplicate（深拷贝子树 + 重分配 StableId）、Delete（删子树）。
- 拖拽重父：拖一个节点到另一个节点上 → `ReparentCommand`（校验不成环）；拖到空白 → 提为根。
- 与刚体/仿真实体的关系：`plan/12` 的运行时刚体/仿真实体列表作为**只读诊断分区**保留在同面板下方（Play 模式可见活跃刚体），与 authoring GameObject 树分区并存，互不混淆。

### 4.7 GameObject Inspector（`GameObjectInspectorPanel`）

升级 `plan/12` 的 `ScriptInspectorPanel`（后者只反射编辑 Behaviour 公开字段）。新面板对当前选中 `EditorGameObject` 渲染 Unity 式 Inspector：

- Header：Name（可编辑）、Enabled 复选、StableId（只读）、Prefab 链信息（若为 prefab 实例，§4.10）。
- Transform 块：X/Y、Rotation、ScaleX/ScaleY 数值编辑（对齐 `Scripting.Transform`），与 Scene View gizmo 双向联动。
- 组件列表：逐个 Behaviour 组件显示为可折叠区，内部复用 `plan/12` 的字段反射编辑（基础类型/向量/枚举/材质引用/范围滑条）；每组件有移除按钮与（可选）上下移序。底部「Add Component」按钮：弹出已注册 Behaviour 类型搜索列表（来源 `ScriptAssemblyRegistry`），选中 → `AddComponentCommand`。
- 字段编辑写回 authoring 模型的 `SerializedFields`（字符串规范化），并同步 Edit 投影；类型转换复用/扩展 `EngineSceneDocumentLoader.ConvertValue`（新增 `Vector2` 支持）。

### 4.8 场景视图与变换 gizmo + 拾取（`SceneViewPanel` + `SceneGizmoController`）

复用 `plan/12` 的 `ViewportPanel`（世界经 Rendering 离屏 FBO 纹理显示）作为 Scene View 基底，叠加编辑器交互层：

- 相机：Scene View 内平移（中键/空格拖拽）、缩放（滚轮）经 `ScriptCameraApi`/`ScriptCameraSynchronizer`（Edit 模式相机独立于 Play 相机）。
- gizmo：`SceneGizmoController` 用 `Hexa.NET.ImGuizmo`，对选中 `EditorGameObject.Transform` 绘制 平移/旋转/缩放 gizmo（快捷键 W/E/R 切换），拖拽结果经 `TransformCommand` 写回 authoring 模型并与 Inspector 联动。gizmo 的 world→screen 变换用 Scene View 相机矩阵（cell 坐标为世界权威，`plan/00 §7`）。
- 拾取：在 Scene View 内点击 → 对 authoring GameObject 的 Transform 包围盒/图标做屏幕空间命中测试，选中最近者写 `EditorSelection`；**空 GameObject 以 gizmo 图标 billboard 作命中目标**（无网格也可点选）。未命中则可落到世界画刷（Edit 模式画沙，经 `MaterialBrushPalettePanel` 既有路径，写相位 [1]、标 dirty + KeepAlive，遵守不变式 #1/#2/#4）。gizmo 悬停/拖拽时优先于画刷（复用 `ImGuiInputBridge` 的 `WantCaptureMouse` 仲裁）。

### 4.9 场景保存格式与序列化往返（`.scene` writer + schema 升版）

现状：`EngineSceneDocumentLoader` 只有读（`Load`/`Build`），无写；`EngineSceneDocument` 缺 parent 与 Transform。本文件补齐往返：

- schema 升版到 `FormatVersion=2`：`EngineSceneEntityDocument` 增 `int ParentId`（0=根）与 `EngineSceneTransformDocument Transform`（X/Y/RotationRadians/ScaleX/ScaleY，字段用 `Vector2` 表达位置/缩放）；`Behaviours` 语义不变。加载器**兼容 v1**（无 ParentId 视为根、无 Transform 用默认）并可升级另存为 v2。
- 新增 writer：Hosting 侧 `SaveSceneDocument(EngineSceneDocument, path)`（§4.4 API (3)），把 shell 已映射好的 `EngineSceneDocument` 序列化为 `.scene` JSON（`System.Text.Json` 源生成，扩展 `EngineSceneJsonContext`），稳定排序（按 StableId 升序）、往返等价（读→写→读逐字段一致，供 `plan/14` 性质测试）。`EngineSceneDocument`→运行时物化 API 由 Hosting 公开；authoring↔文档映射归 shell。
- 保存流程：File ▸ Save Scene → writer 落盘 → 清脏标记 → 窗口标题去星号。Save As → 选路径另存并更新 `EditorProject.StartScene`/`Scenes`。
- 材质引用稳定性：GameObject 字段里的 `MaterialId` 入盘仍走「材质稳定 Name」策略由 `plan/04`/`plan/12` 负责（不变式 #8），场景文档只存字段字符串值，不引入数值 id 依赖。

### 4.10 Prefab（预制体）——完整实现（`EditorPrefab` / `PrefabInstance`）

一步到位实现完整 prefab（资产 + 实例 + override + 嵌套 + override 传播），不做 MVP 缩减：

- **预制体资产**：`.prefab` JSON，schema 复用「一个 GameObject 子树」（根 `EditorGameObject` + 后代 + 组件 + 字段），存于 `content/prefabs/`。从层级面板把一个 GameObject 拖到 Project 面板即「创建 prefab 资产」（子树深拷贝、重分配资产内局部 StableId）。
- **实例化**：从 Project 面板把 `.prefab` 拖入 Hierarchy/Scene View → 在 authoring 模型创建一个 `PrefabInstance` 节点，记录 `PrefabAssetPath` + `Overrides`（按「GameObject 资产内稳定路径 + 组件 TypeName + 字段名」寻址的覆盖列表）。默认实例继承 prefab 全部属性。
- **override**：在实例上改属性记为 override（Inspector 中被改字段/组件显示**加粗**，可 **Revert** 单项或全部）；新增/移除组件、新增子对象也作为结构性 override 记录（add/remove 条目）。
- **嵌套 prefab**：prefab 资产内部可含其它 `PrefabInstance`（嵌套）。物化/加载时按「外层实例 override 叠加于（内层实例 override 叠加于内层 prefab 基线）」的**递归基线+override**规则展开，override 寻址路径贯穿嵌套层级。
- **override 传播**：编辑 prefab 资产（双击进入 prefab 编辑模式，或直接编辑 `.prefab`）→ 所有实例的**非 override** 属性随之更新（物化/加载时以 prefab 资产为基线叠加 override）；嵌套情形下内层资产变更向外层所有实例递归传播。
- **物化**：进入 Play/保存时，`PrefabInstance` 展开为具体 GameObject 子树（prefab 基线 + override，递归展开嵌套），再走 §4.5 物化。
- **与运行时解耦**：prefab 是纯 authoring/编辑器概念，运行时 `Scripting.Scene` 只见展开后的扁平实体，不知道 prefab 存在（不污染 `plan/11`）。

### 4.11 复用 plan/12 面板（资源浏览 / 脚本 / 材质 / 世界）

以下面板直接复用 `plan/12` 既有实现，shell 只负责在编辑器 dock 布局中注册与摆位，不重造：

- 资源浏览（`AssetBrowserPanel` + `FileSystemAssetBrowserDataSource`）：浏览工程 `content/`（材质纹理缩略图、音效试听、场景、`.prefab`、materials/reactions.json），选中联动。作为 Unity 式「Project 面板」。
- 脚本 Inspector 字段编辑：并入 §4.7 GameObject Inspector 的组件区（复用其反射字段编辑），并保留「触发脚本热重载」按钮（`plan/11` Roslyn+ALC）。
- 材质 + 反应实时编辑器（`MaterialReactionEditorPanel`）：id 稳定热重载（不变式 #8）原样复用；本轮 demo-playability 新增材质字段的编辑由 `plan/12 §3.8` 修订承担。
- 世界画刷/检视器/调试叠层/性能 HUD/存读档/子系统调参：原样复用。

### 4.12 玩家包与编辑器解耦（player EXE 不含编辑器，纠正版）

- 发行产物分两类：**玩家包**（`demo/PixelEngine.Demo` 作为玩家运行时/游戏；或后续独立 `apps/PixelEngine.Player`，见 open question），**编辑器工具包**（`apps/PixelEngine.Editor.Shell`，开发/内测分发）。
- 关键动作（**纠正版**，见 §0.5）：Demo **没有**对 `PixelEngine.Editor` 的直接项目引用，故解耦不是「删 Demo 的 Editor 引用」，而是——(a) §0 GUI 中性化把 `HexaImGuiBackend`/`ScriptGuiContext`/字体栈下沉 `PixelEngine.Gui`；(b) `Hosting.csproj` 删对 Editor 的引用改引 `Gui`；(c) `DemoProgram.cs` 去 `using PixelEngine.Editor` 与 `EnableEditor` 进程内编辑器路径、玩家 HUD 改用 `PixelEngine.Gui` 中性 host。编辑器托管职责整体迁往 shell。
- editor-window 证据迁移：原基于 Demo `EnableEditor` 的 editor-window 人工验收/preflight 证据（`editor_enabled`/`editor_running`/`editor_panels`/`editor_bridge_frames`，见 `plan/18 §5`、`tools/demo-manual-acceptance-preflight.ps1` 的 `hudMenuEditorVideo` scope、`PerformanceHardeningToolingDisciplineTests` 锁定断言）等价迁移到 shell 的 `--window-ticks`/scripted-probe 入口（§7），逐项保绿。
- `plan/15` 修订：玩家包发行审计新增「拒绝 `app/` 内出现 `PixelEngine.Editor.dll` 及编辑器专属面板闭包（含 `ImGuizmo`/`ImPlot` 编辑器专属绑定）；**允许**玩家 HUD 所需的 `Hexa.NET.ImGui` 核心（经 `PixelEngine.Gui` 中性 host 引入）」；该断言以 §0 GUI 中性化落地为**前置**（标 blocked-on §0）。编辑器 shell 的发行 RID 与玩家包矩阵解耦（编辑器是开发工具，不受玩家包发行矩阵约束）。

---

## 5. 编辑器内构建与发布（Build Settings 面板 + 打包管线调用）

> 归属：plan/19（独立编辑器 app）拥有本面板；打包逻辑一律复用 `plan/15`，本章只定义「面板 UI + 子进程编排 + 与 `plan/15` 契约的消费」。遵守 `AGENTS.md §2`「一步到位、无 MVP、无 stub」：日志、进度、取消、失败诊断、产物校验一次做全。

### 5.1 设计原则与边界

编辑器绝不重复实现任何 `dotnet publish` / apphost 重写 / 布局装配 / 校验逻辑；这些全部由 `plan/15` 的脚本管线拥有。编辑器只做三件事：把面板设置翻译成 `plan/15` 编排入口的参数、在编辑器进程内起子进程运行该入口、把子进程的结构化 stdout 与 exit code 回灌到面板。构建子系统是编辑器新增的「外部工具编排」职能，不读任何引擎子系统内部类型、不触碰 sim/physics/render 热数据，因此不违反 `plan/12 §1`——它读取的是 Hosting 的只读 `EngineProject`（场景清单）与文件系统/子进程，与引擎运行态解耦。构建产物是 player-only（不含编辑器）：见 §5.7。

### 5.2 模块与类型（命名空间 `PixelEngine.Editor.Shell.Build`，**位于 `apps/PixelEngine.Editor.Shell` 程序集**）

> 归属校正（消循环）：`BuildSettingsPanel` 需读 Hosting 的 `EngineProject`。§0 后 `Editor↛Hosting`（Editor 只依赖 `Gui`+子系统），若把面板放 `PixelEngine.Editor` 则形成 `Editor→Hosting` 循环。故本面板落在 **shell 应用程序集**（`EditorShell→{Hosting,Editor,Gui}`，位于 Hosting 之上无循环）。它 `implements IEditorPanel`（`IEditorPanel` 来自 Editor，shell 依赖 Editor 合法），经 `EditorApp.AddPanel` 注册。

- `BuildSettingsPanel : IEditorPanel`——ImGui 停靠窗口，`Title="构建与发布"`。持有 `IPlayerBuildService`、`BuildTargetSettings`（当前编辑值）、`BuildLog`（滚动日志缓冲）、`BuildRunView`（进度/结果快照）。`Draw(in EditorContext)` 内只读 `EditorContext`，构建状态来自服务。
- `BuildTargetSettings`（`sealed record`，可序列化）——目标平台、通道、配置、输出目录、产物名、图标、场景清单、符号开关、内容选项、Build-And-Run 开关。`Normalize()` 校验并规范化（非空、路径合法、唯一启动场景）。
- `SceneBuildEntry`（`sealed record`）——`{ string SceneName; bool Included; bool IsStartup; SceneSourceKind SourceKind; string? Source }`，由**扫描 `content/scenes/`**（与 `EngineProject.Scenes` 声明合并去重）映射构造。
- `IPlayerBuildService` / `PlayerBuildService`——子进程编排后端。`Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildProgressEvent> progress, CancellationToken ct)`；`Task<BuildPreflight> PreflightAsync()` 探测 dotnet SDK / pwsh。
- `BuildRequest`——由 `BuildTargetSettings` 投影出的、面向 `tools/build-player` 的纯参数 DTO（Rid/Channel/Configuration/Output/Version/InformationalVersion/ProductName/IconPath/IncludeSymbols/StartScene/IncludedScenes/RunAfterBuild）。
- `BuildProgressEvent`——`{ BuildEventKind Kind; BuildPhase Phase; float Percent; BuildLogLevel Level; string Message; DateTimeOffset Timestamp }`，由 NDJSON 行解析而来（`schema=pixelengine.build/v1`）。
- `BuildPhase`（enum）——`Native, Publish, Verify, Package, Audit, Done`（与 `plan/15` 五阶段对应）。
- `BuildResult`——`{ bool Ok; string Rid; string Channel; string Configuration; string Version; string InformationalVersion; string? PackageArchive; string? PackageDir; string? PlayerDir; string? LauncherExe; string? Sha256; long SizeBytes; IReadOnlyDictionary<BuildPhase,double> PhaseTimingsMs; IReadOnlyList<string> Warnings; string? Error; int ExitCode }`，由 `build-result.json` + exit code 组合。
- `BuildLog`——线程安全环形缓冲（预分配，避免逐帧托管 churn），落盘 `<output>/build.log`。
- `BuildToolLocator`——定位仓库根、`tools/build-player.(ps1|sh)`、`dotnet`、`pwsh`/`powershell.exe`、`sh`。

`BuildTargetSettings` 的 JSON 读写走新增源生成上下文 `PixelEngineEditorShellBuildJsonContext`（`System.Text.Json` 源生成，AOT/trim 友好，符合 `plan/12 §2`、`plan/15 §3.6` 零反射根），持久化到 `<项目根>/BuildSettings.json`（项目根 = `EngineProject.ContentRoot` 的父目录）。

### 5.3 面板 UI（ImGui 布局）

面板分「设置区（可编辑）/操作区/进度区/日志区/结果区」五段，构建进行中除「取消」外的设置控件禁用。

设置区：目标平台下拉（`win-x64` 默认、`win-arm64`；当前 Windows 优先激活，见需求 3 与 `plan/15 §2.1`，其余 RID 灰显注「由 CI/CLI 出」）；通道单选（R2R 主发行 / NativeAOT 次发行——AOT 仅宿主 RID 可选，跨架构灰显提示）；配置单选（Debug / Release）；输出目录文本框 + 「打开输出目录」（`explorer.exe`）；产物名文本框（默认 `PixelEngine Demo`）；图标路径文本框（`.ico`）+ 缩略图预览（经中性 ImGui host 的 GL 纹理上传，可选）；「含调试符号」勾选（默认关）；内容资产选项（「打包整个 `content/`」默认，与 `plan/15 §3.9` 单一真相源一致 / 「按入包场景过滤 `scenes/` 子目录，materials/reactions 恒含」保 id 稳定 #8）。

场景清单表（ImGui table，源 = 扫描 `content/scenes/` ∪ `EngineProject.Scenes`）：列 [场景名 | 入包(checkbox) | 启动(radio)]。约束：至少一个入包；恰一个启动；启动场景必须入包。违反时「Build」置灰并红字提示。

操作区：`[Build]`、`[Build And Run]`、`[取消]`。`[Build And Run]` = 构建成功后以 detached `Process`（`UseShellExecute=false`、工作目录=产物 `PlayerDir`）启动 `LauncherExe`。

进度区：进度条（NDJSON `percent`）、当前阶段中文标签、已用时。

日志区：滚动子窗口，按 `BuildLogLevel` 着色（info/warn 黄/error 红），自动滚动开关、「复制日志」、「打开 build.log」。非 NDJSON 原始行归入当前阶段作 info 行。

结果区：成功 → 绿条 + `PackageArchive`/`PlayerDir`、大小、`Sha256`、各阶段耗时、`[打开产物目录]` `[运行]`；失败 → 红条 + 失败阶段 + `Error` + 末尾若干错误行 + `[重试]`。

### 5.4 子进程编排机制（进程边界）

编辑器进程内经 `System.Diagnostics.Process` 起单一子进程运行 `plan/15` 的编排入口 `tools/build-player.(ps1|sh)`（见 `plan/15 §3.11`）：Windows 用 `pwsh -NoProfile -File tools/build-player.ps1 <args>`（缺 pwsh 回落 `powershell.exe`），非 Windows 用 `sh tools/build-player.sh <args>`。`RedirectStandardOutput/Error=true`、`UseShellExecute=false`、工作目录=仓库根。

线程模型：`RunAsync` 在后台线程读子进程输出，不阻塞 UI。子进程 stdout 逐行为 NDJSON 事件，解析成 `BuildProgressEvent` 压入线程安全 `ConcurrentQueue`；`BuildSettingsPanel.Draw` 每帧 drain 队列刷新进度/日志（ImGui 单线程即时模式，只在 UI 线程读快照）。stderr 归入 error 级日志。

结束语义：exit 0=成功、非 0=失败。子进程在输出目录写 `build-result.json`，`RunAsync` 结束时读入并与 exit code 合成 `BuildResult`；若非 0 且无 `build-result.json`，回退用末尾 stderr/stdout 行 + exit code 报错。

取消：`CancellationToken` 触发 `Process.Kill(entireProcessTree: true)`，杀 dotnet/publish 子树。`plan/15` 的 publish 脚本每次运行先清理本 RID/配置的 publish 输出与 `src/bin·obj`，故被取消留下的半成品在下次运行时被清理，保证可重复性。

预检：`PreflightAsync` 先跑 `dotnet --version`（缺 SDK → 明确报「未找到 .NET SDK，编辑器内构建需要开发机安装 SDK」，绝不静默）与 pwsh 探测；预检失败直接给可执行诊断，不起构建。

### 5.5 与 plan/15 的契约消费

`build-player` 内部按 `plan/15 §3.11` 五阶段顺序调用既有脚本：build-native → publish-r2r|publish-aot → verify-publish → package → audit-release-artifacts（单 RID，非 RequireAll）。编辑器只关心其 NDJSON（`schema=pixelengine.build/v1`）与 `build-result.json`，不感知内部脚本细节，从而「不重复实现打包逻辑」。参数映射：`Rid→-Rid`、`Channel(R2R/Aot)→-Channel r2r|aot`、`Configuration→-Configuration`、`Output→-Output`、`Version/InformationalVersion→-Version/-InformationalVersion`（后者默认嵌 `git rev-parse --short HEAD`）、`ProductName→-ProductName`、`IconPath→-p:ApplicationIcon`、`IncludeSymbols→-IncludeSymbols`、`StartScene/IncludedScenes→-StartScene/-IncludeScene`。产物落位、命名（`app/` 子目录布局，`plan/15 §3.7.1`）、apphost 重写、确定性打包、SHA256SUMS 全由 `plan/15` 既有实现产出，`BuildResult` 直接引用其路径与 hash。

### 5.6 场景清单落地

入包场景过滤：`build-player` 依 `-IncludeScene` 清单在 package 阶段只拷 `content/scenes/` 被选场景（materials.json/reactions.json/纹理/音效恒拷，保 #8/`§3.9`）。启动场景烘焙：`build-player` 在 staging 的 `content/` 写 `startup.json`（`{ "startScene": "scenes/<name>.scene" }`），player 的 `DemoStartupOptions` 默认起始场景改为「优先读 `content/startup.json`，缺省回落 `scenes/playable-world.scene`」（**默认场景是 playable-world，非 lava-mine**），从而不靠 CLI `--scene` 即可用面板选定启动场景。audit 的必含场景断言相应放宽为「必含被声明的启动场景文件」。

### 5.7 player-only（不含编辑器）保障

需求 1（editor/player 解耦）由 §0 GUI 宿主中性化落地后，player 闭包（`Demo→Hosting→{…,Gui}`）结构上不含 `PixelEngine.Editor`（Hosting 不再引用 Editor）。本面板的 `build-player` 出的即为该 player。`plan/15` 的 audit 新增不变式：player 包 `app/` 内**不得**出现 `PixelEngine.Editor.dll` 与编辑器专属面板闭包（含 `ImGuizmo*`/`ImPlot*`），但**允许**玩家 HUD 所需的 `Hexa.NET.ImGui` 核心（经 `PixelEngine.Gui` 引入）——撤销早期「拒绝一切 ImGui」的不可满足表述。此断言把「编辑器内构建产出无编辑器 player」锁死在打包校验层，且以 §0 落地为前置（标 blocked-on §0）。

### 5.8 开发布局 vs 发行审计布局

「含调试符号」为开发向：符号/Debug 构建产物落「开发布局」（保留 pdb），走宽松 dev-audit 校验（结构存在性 + player-only 断言），不进 `plan/15` 严格 release audit（后者强拒 pdb，见 `audit-release-artifacts` 的 `Test-DisallowedPlayerPackageFile`）。Release + 无符号构建走完整 `audit-release-artifacts`，保发行不变式不被削弱。面板明示当前布局类型。

### 5.9 §5 实现清单

面板与设置模型（shell 程序集）
- [ ] `BuildTargetSettings`/`SceneBuildEntry`/`BuildRequest`/`BuildResult`/`BuildProgressEvent`/`BuildPhase`/`BuildLog` 类型与 `PixelEngineEditorShellBuildJsonContext` 源生成序列化（§5.2）
- [ ] `BuildTargetSettings.Normalize()` 校验：唯一启动场景、启动∈入包、至少一入包、路径/产物名非空（§5.2/§5.3）
- [ ] `BuildSettingsPanel : IEditorPanel`（位于 shell 程序集）五段 UI（设置/操作/进度/日志/结果），构建中禁用设置控件（§5.2/§5.3）
- [ ] 场景清单表由「扫描 `content/scenes/` ∪ `EngineProject.Scenes`」只读映射，入包/启动交互与约束校验（§5.3/§5.6）
- [ ] 图标 `.ico` 缩略图经中性 ImGui host 上传预览（可选，缺图标不阻断）（§5.3）
- [ ] 设置持久化 `<项目根>/BuildSettings.json` 读写，面板初始化加载、变更保存（§5.2）
- [ ] 面板经 `EditorApp.AddPanel` 注册，`EditorContext` 仅只读消费 + Hosting `EngineProject` 只读，无引擎内部访问、无 `Editor→Hosting` 循环（§5.1/§5.2）

子进程编排（shell 程序集）
- [ ] `BuildToolLocator` 定位仓库根/`build-player`/`dotnet`/`pwsh`↔`powershell.exe`/`sh`（§5.4）
- [ ] `PlayerBuildService.PreflightAsync`：dotnet SDK 与 pwsh/sh 探测，缺失给可执行诊断（§5.4）
- [ ] `PlayerBuildService.RunAsync`：起子进程、后台读 stdout 逐行解析 NDJSON、stderr→error 级、`ConcurrentQueue` 回灌、`Draw` 每帧 drain（§5.4）
- [ ] exit code + `build-result.json` 合成 `BuildResult`；无结果清单时回退末尾输出 + exit code（§5.4）
- [ ] 取消 = `Process.Kill(entireProcessTree:true)`，半成品由 `plan/15` 脚本下次清理（§5.4）
- [ ] `[Build And Run]`：成功后 detached 启动 `LauncherExe`（工作目录=`PlayerDir`）（§5.3）
- [ ] 日志落盘 `<output>/build.log`，「复制日志」「打开日志」「打开产物目录」（§5.3）

契约消费（`plan/15`，见其 §3.11 清单）
- [ ] `BuildRequest`→`build-player` 参数映射（Rid/Channel/Configuration/Output/Version/InformationalVersion/ProductName/Icon/IncludeSymbols/StartScene/IncludeScene）（§5.5）
- [ ] AOT 通道仅宿主 RID 可选，跨架构灰显提示由 CI/CLI 出（§5.3/§5.5）

player-only 与布局
- [!] player 发布结构上排除 Editor+编辑器专属 ImGui 闭包（`ImGuizmo`/`ImPlot`）、保留 `Hexa.NET.ImGui` 核心——阻塞：前置 §0 GUI 宿主中性化落地（§5.7）
- [ ] 开发(含符号)dev-audit 布局 vs 发行 audit-release-artifacts 布局分流，Release+无符号走完整 audit（§5.8）

### 5.10 §5 验收标准

- [ ] 在编辑器内点「Build」（win-x64 / R2R / Release）能起子进程跑通 native→publish→verify→package→audit，进度条随阶段推进，日志实时滚动，成功后结果区给出 zip 路径、大小、SHA256 与各阶段耗时；产物与 `tools/*` 手工出包**同等参数下字节级一致**（复用同一管线）（§5.4/§5.5）
- [ ] 「Build And Run」成功后自动启动产出的 `PixelEngine Demo.exe` 并正常进入游戏（默认起始场景 playable-world 或面板选定场景）（§5.3/§5.6）
- [ ] 产出 player 包 `app/` 内不含 `PixelEngine.Editor.dll` 与 `ImGuizmo*`/`ImPlot*`，但含玩家 HUD 所需 `Hexa.NET.ImGui` 核心，audit 校验通过（§5.7）
- [ ] 场景清单：仅入包所选场景，启动场景经 `content/startup.json` 生效，player 不加 `--scene` 直接进选定启动场景（§5.6）
- [ ] 失败诊断：故意造 publish/audit 失败时，面板高亮失败阶段并回显脚本断言原文与 exit code；缺 SDK/pwsh 时预检给出明确可执行提示，绝不静默（§5.4）
- [ ] 取消运行中的构建能杀掉 dotnet/publish 子树，随后重跑构建成功（无残留污染）（§5.4）
- [ ] 「含调试符号」开发构建保留 pdb 走 dev-audit 宽松校验；Release+无符号走严格 audit，二者产物布局符合各自规则（§5.8）
- [ ] 构建全程 UI 不卡顿（后台线程 + 每帧 drain 队列），设置持久化重启后恢复（§5.2/§5.4）
- [ ] 面板仅消费 `EditorContext` 只读 + `EngineProject` 只读 + 子进程，无引擎内部后门、无反向依赖/无循环（§5.1/§5.2）

---

## 6. 实现清单（壳与 authoring）

壳与项目：
- [x] 新增 `apps/PixelEngine.Editor.Shell` 可执行项目（`OutputType=Exe`），引用 `PixelEngine.Hosting` + `PixelEngine.Editor` + `PixelEngine.Gui`，依赖方向符合 §0.7（与 Demo 同层、无反向依赖）（§3）
- [x] `.sln` 增 `apps/` 顶层 solution 文件夹与 shell 项目；`plan/00 §5` 结构与依赖图更新（§3、§0.7）
- [x] `Program.cs`/`EditorShellApp`：进程入口、命令行解析（`--project`/`--scene`/`--window-ticks`/scripted-probe）、主循环、崩溃日志、退出码（§4.1）
- [x] `EditorShellWindow`：**启动即立起**单一 `RenderWindow`/单一 GL 上下文/`PixelEngine.Gui` 中性 ImGui host（独立于 Engine 装配），编辑器窗口标题 `PixelEngine Editor — 工程 — 场景[*]`，全程绝不创建第二窗口/上下文（§4.1、§2）
- [x] `EditorProject` + `project.pixelproj` 读写；`EditorProject→EngineProject` 转换（§4.2）
- [x] `RecentProjectsStore`：最近工程持久化到用户配置目录（§4.2）
- [x] `ProjectPickerWindow`：在中性 ImGui host 上下文内绘制；新建/打开/最近工程；新建生成 `project.pixelproj` + `content/` 骨架 + 空 `content/scenes/main.scene`（§4.1、§4.2）
- [x] `EditorMainMenuBar`：File/Edit/GameObject/Window/Play/Help 全菜单（含 Build Settings…）+ 快捷键（§4.3）
- [x] `EditorShellLayout`：编辑器默认 dock 布局 + 保存/恢复 + Reset Layout（§4.3）

in-process 宿主：
- [x] `EditorProjectSession`：用 `EngineBuilder` 装配引擎（Edit 模式默认暂停）、`engine.AttachWindowRuntime(shellWindow)` attach 既有窗口（Engine 不 own、Dispose 不销毁窗口）（§4.1、§4.4，前置 `plan/18` 窗口所有权解耦 API）
- [x] shell 经 `IEditorHostExtension`（§0.4）注入 Editor 面板宿主到 Hosting 相位[10]（前置 §0，Hosting 不再引用 Editor）（§4.4）
- [x] 复用 `EngineEditorPlaySessionService`/`EngineWorldSnapshotStore` 的 Play/Edit/Step 与快照回滚，绑定菜单/工具条（§4.4）
- [ ] 切换/关闭工程逆序释放 `Engine`、保留窗口/上下文、重建 session（§4.1、§4.4）

GameObject authoring：
- [ ] `EditorSceneModel` + `EditorGameObject`（StableId/Name/Enabled/ParentId/Children/Transform/Components/PrefabLink）+ `EditorComponentModel`（§4.5）
- [ ] authoring↔`EngineSceneDocument` 双向映射；每 GameObject 默认含不可删 Transform（§4.5）
- [ ] authoring StableId→运行时 Entity.Id 显式映射表，供拾取联动与快照回滚不错位（§4.5）
- [ ] authoring→运行时 `Scripting.Scene` 物化（父链复合烘焙为世界 TRS、字段绑定扩展 `Vector2`、运行时保持扁平）（§4.5、§1）
- [ ] Edit 模式 authoring→运行时投影（结构编辑同步、Behaviour 不 tick）（§4.5）
- [ ] `EditorCommand` + `EditorUndoStack`：创建/删除/重父/重命名/复制/加删组件/改字段/改 TRS 全走命令栈 Undo/Redo + 脏标记（§4.5）
- [ ] `GameObjectHierarchyPanel`：Unity 式树、启用位、右键创建/删除/重命名/复制、拖拽重父（防环）、选中联动；刚体/仿真实体作只读诊断分区（§4.6）
- [ ] `GameObjectInspectorPanel`：Header(Name/Enabled/StableId/Prefab) + Transform 块 + 组件列表(复用字段反射编辑)+移除/排序 + Add Component 搜索列表（来源 `ScriptAssemblyRegistry`）（§4.7）
- [ ] `SceneViewPanel`：复用 `ViewportPanel`，Edit 相机平移/缩放（§4.8）
- [ ] `SceneGizmoController`：`Hexa.NET.ImGuizmo` 平移/旋转/缩放 gizmo（W/E/R），写回 authoring + 与 Inspector 双向联动（§4.8）
- [ ] Scene View 点选拾取 GameObject（屏幕空间命中，空对象 gizmo 图标 billboard 作命中目标）+ 与世界画刷/gizmo 输入仲裁（`WantCaptureMouse`）（§4.8）

场景保存与 prefab：
- [ ] `.scene` schema 升版 `FormatVersion=2`（ParentId + Transform 块 + `Vector2`），加载器兼容 v1 并可升级另存 v2（§4.9）
- [ ] Hosting `SaveSceneDocument(EngineSceneDocument, path)` writer：authoring→`.scene` JSON 稳定排序、往返等价（源生成上下文扩展）（§4.9，前置 `plan/18` API）
- [ ] File ▸ Save/Save As 流程：落盘、清脏、标题去星号、更新工程场景引用（§4.9）
- [ ] `EditorPrefab`/`.prefab` 资产：从 GameObject 创建、存 `content/prefabs/`（§4.10）
- [ ] `PrefabInstance` + `Overrides`：实例化、override 记录/加粗/Revert、**嵌套** prefab 递归展开、prefab 编辑向所有实例（含嵌套）override 传播、物化正确（§4.10）

复用面板与解耦：
- [ ] 在编辑器 dock 注册复用 `plan/12` 面板：AssetBrowser(Project)、材质+反应编辑器、世界画刷/检视器、调试叠层、性能 HUD、存读档、子系统调参、sim 控制条、Edit/Play 模式（§4.11）
- [ ] 玩家包解耦（纠正版）：§0 中性化 + `Hosting.csproj` 去 Editor 引用 + `DemoProgram.cs` 去 `using PixelEngine.Editor`/`EnableEditor` 路径、改用 `PixelEngine.Gui` 中性 host；编辑器职责迁移 shell（§0.5、§4.12、`plan/13` 修订）
- [ ] `plan/15` 玩家包审计新增「拒绝 `PixelEngine.Editor.dll`/`ImGuizmo*`/`ImPlot*`、允许 `Hexa.NET.ImGui` 核心」；editor-window 证据入口迁移到 shell（§4.12、§8）

测试（配合 `plan/14`）：
- [ ] `.scene` v1/v2 读写往返等价、schema 升级、字段类型（含 `Vector2`/MaterialId）转换测试
- [ ] authoring→运行时物化正确性（层级世界 TRS 烘焙、StableId→Entity.Id 映射、组件/字段还原）与 Undo/Redo 命令栈测试
- [ ] prefab 完整性：实例化/override/Revert/嵌套展开/传播 测试
- [ ] shell 短跑冒烟：打开工程→Edit 装配（attach 既有窗口）→进入 Play→退出回滚→保存场景（`--window-ticks` 有限 tick，产出 editor-window 证据 `editor_enabled`/`editor_running`/`editor_panels`/`editor_bridge_frames`）

---

## 7. 验收标准

- [x] 独立 EXE 存在：`apps/PixelEngine.Editor.Shell` 可独立构建产出可执行文件，双击/命令行启动进入独立编辑器窗口，单独进程、独立顶层窗口（§4.1）
- [x] 启动时序正确：启动即立起唯一 `RenderWindow`+GL+中性 ImGui host（独立于 Engine），项目选择器在该上下文内绘制；选定工程后 Engine attach 到既有窗口且不 own（`Engine.Dispose` 不销毁窗口）（§4.1）
- [x] Shell 启动与项目选择阶段单进程、单 `RenderWindow`、单 GL 上下文；Edit/Play 同窗口宿主仍由 §4.4 后续节点闭合（守 `plan/12` 真约束，§2）
- [x] 项目选择器可新建/打开工程、展示并打开最近工程；新建工程生成合法 `project.pixelproj` + `content/` 骨架 + 空场景（§4.2）
- [ ] 主菜单栏 File/Edit/GameObject/Window/Play/Help 全部可用（含 Build Settings…）；默认 dock 布局呈现 Hierarchy/Scene View/Inspector/Project/Console/HUD/构建与发布，可保存恢复与 Reset（§4.3）
- [ ] Edit 模式 sim 暂停可编辑、Play 模式同窗口运行游戏、退出 Play 回滚到编辑态（复用既有快照，类 Unity），切换不破坏帧节奏（#6，§4.4）
- [ ] 层级面板可创建/删除/重命名/复制 GameObject、拖拽重父（防环）、选中联动 Inspector 与 Scene View（§4.6）
- [ ] Inspector 显示 Name/Enabled/Transform TRS/组件列表，可 Add/Remove 组件、编辑组件公开字段，改动经命令栈可 Undo/Redo（§4.5、§4.7）
- [ ] Scene View 内 gizmo 可平移/旋转/缩放选中 GameObject 并与 Inspector 双向联动；可点选拾取 GameObject（含空对象 billboard）；gizmo 与世界画刷输入正确仲裁（§4.8）
- [ ] 场景可 Save/Save As 为 `.scene`（v2），读→写→读逐字段等价；v1 旧场景可加载并升级（§4.9）
- [ ] prefab 完整可用：创建资产、实例化、记录/Revert override、编辑资产传播到实例、**嵌套** prefab 递归展开物化正确（§4.10）
- [ ] 资源浏览/材质反应编辑/世界画刷/调试叠层/性能 HUD/存读档/调参面板在 shell 中复用可用，无重复实现（§4.11）
- [ ] 玩家包解耦（纠正版）：§0 落地后玩家闭包 `Demo→Hosting→{…,Gui}` 不含 `PixelEngine.Editor`；玩家包发行审计拒绝 `PixelEngine.Editor.dll`/`ImGuizmo*`/`ImPlot*`、允许 `Hexa.NET.ImGui` 核心（§0.5、§4.12、`plan/15`）
- [ ] editor-window 证据迁移：shell `--window-ticks`/scripted-probe 产出与原 Demo `EnableEditor` 等价的 `editor_enabled`/`editor_running`/`editor_panels`/`editor_bridge_frames`，`plan/18 §5` 与相关 preflight/锁定测试保绿（§4.12）
- [ ] 编辑器内构建：§5.10 全部验收通过（一键出 player-only 包、进度/日志/取消/失败诊断/Build-And-Run/开发 vs 发行布局）
- [ ] shell 仅消费 Hosting/Editor/Gui/各子系统公开 API，无对内部类型直接访问、无反向依赖/循环；不新增 native 依赖（不变式 #10，§3）

---

## 8. 依赖关系

前置（须具备其公开 API）：
- **§0 GUI 宿主中性化重构（`PixelEngine.Gui`）**：M13 入口门，plan/19 壳注入的**硬前置**——不落地则 shell 无法在 `Hosting↛Editor` 前提下宿主编辑器、玩家包无法结构解耦。
- `plan/18`（Hosting）：`Engine`/`EngineBuilder`/Play-Edit-Step/`EngineEditorPlaySessionService`/`EngineWorldSnapshotStore`；已新增窗口/GL 所有权解耦 API（attach 既有窗口不 own、Dispose 不销毁）、公开编辑态 bootstrap（`EditorHostBootstrap` + `IEditorHostExtension` 注入点）、`SaveSceneDocument` writer、`EngineSceneDocument`→运行时物化 API；剩余 editor-window 证据入口迁移随 shell 落地。**顺序约束**：这些 API 先于 §4.1/§4.4 壳落地。
- `plan/12`（编辑器面板层）：全套 `IEditorPanel` 面板、`EditorApp`/`EditorDockSpace`/`ImGuizmo` 接入；须修订 §1 锁定措辞（见 fileActions）。
- `plan/11`（脚本）：`Scripting.Scene`/`Entity`/`Behaviour`/`Transform`/`ScriptAssemblyRegistry`、Roslyn+ALC 热重载、Add Component 类型来源。
- `plan/08`（Rendering）：`RenderWindow`/GL 上下文/离屏 FBO 视口纹理/相机同步/UI 层注册接口。
- `plan/15`（打包）：**§3.11 build-player 编排器 + NDJSON(`pixelengine.build/v1`)/`build-result.json` 契约 + player-only audit 不变式**（`§5` 消费之，**顺序约束**：build-player 先于 `BuildSettingsPanel`）；`§2.1` RID 激活门控（Windows 优先）；`§3.7.1` `app/` 子目录布局。
- `plan/00`：技术栈与依赖方向（新增 `apps/`、`src/PixelEngine.Gui`、`src/PixelEngine.UI`，§0.7）。

协同/下游：
- `plan/13`（Demo）：`DemoProgram.cs` 去 Editor 使用/`EnableEditor` 路径、改用 Gui 中性 host；editor-window 证据迁移登记。
- `plan/20`（`PixelEngine.UI`）：复用 `PixelEngine.Gui` 字体栈（含 CJK）与 `IGuiContext` 回退基线。
- `plan/14`（测试）：`.scene` 往返/物化/Undo/prefab（含嵌套）/shell 冒烟/PlayerBuildService NDJSON 解析·取消/player-only audit 断言测试。
- `plan/17`（路线图）：新增 **M13「编辑器独立化与发行解耦」**，置于 M12 之后；plan/19 壳置于 `plan/12` 之后（11→12→13 链上补 12→19）。

风险/阻塞：若 §0 或 `plan/18` 未及时暴露所需公开 API，按 `AGENTS.md §2` 标 `- [!] 阻塞：原因` 并由对应计划补 API，不在 shell 内访问 Engine 内部私有装配绕过。

---

## 9. 提交节点

按 `AGENTS.md §6`，每个节点完成即用中文 git 提交：
- [x] 节点 0：`refactor(gui): 新增 PixelEngine.Gui 中性 ImGui 宿主 + Hosting 去 Editor 硬引用（M13 入口门）`（§0，scope=gui/hosting）
- [x] 节点 1：`feat(editor-shell): 独立编辑器可执行壳 + 启动即立窗口/单进程单上下文生命周期`（§4.1、apps 项目 + `.sln`/`plan/00` 结构）
- [x] 节点 2：`feat(editor-shell): 工程模型/项目选择器/最近列表 + 主菜单栏与默认布局`（§4.2–§4.3）
- [x] 节点 3：`feat(editor-shell): in-process 宿主引擎 Edit/Play 接入（attach 既有窗口 + IEditorHostExtension 注入）`（§4.4，前置 `plan/18` API）
- [ ] 节点 4：`feat(editor-shell): GameObject authoring 模型 + StableId 映射 + 层级面板 + 命令栈 Undo/Redo`（§4.5–§4.6）
- [ ] 节点 5：`feat(editor-shell): GameObject Inspector（Transform/组件增删改/Add Component）`（§4.7）
- [ ] 节点 6：`feat(editor-shell): Scene View 变换 gizmo 与拾取`（§4.8）
- [ ] 节点 7：`feat(editor-shell): .scene 保存往返（schema v2）+ 完整 prefab（含嵌套/传播）`（§4.9–§4.10）
- [ ] 节点 8：`feat(editor): Build 面板设置模型与 UI（平台/输出/产物名/图标/场景清单/配置/符号/内容选项）`（§5.2–§5.3，shell 程序集）
- [ ] 节点 9：`feat(editor): PlayerBuildService 子进程编排（起 build-player、NDJSON 回灌、exit code、取消、Build-And-Run）`（§5.4–§5.6）
- [ ] 节点 10：`build(build): tools/build-player 编排器 + NDJSON/build-result 契约 + player-only audit 不变式`（`plan/15 §3.11`，scope=build）
- [ ] 节点 11：`refactor(demo): 玩家包与编辑器解耦（Demo 去 Editor 使用/EnableEditor 路径，改用 Gui host）+ 证据迁移`（§0.5、§4.12）
- [ ] 节点 12：`docs(plan): 落地 plan/19 并修订 plan/00/12/13/15/18/README 交叉引用`（§8）
