# 2026-07-11 EDITOR-007 独立 Scene authoring 预览证据

taskIds: `EDITOR-007`
implementationCommit: `c3e08ba21b5818f071b41b843ca8c132796bce9c`
runSessionId: `local-20260711-editor007-scene-authoring`
evidenceState: `complete_local_scene_authoring_contract_and_window_smoke`

## 结论

实现 commit `c3e08ba2` 已将 Scene View 从 runtime viewport/camera 中分离。Scene View 现在由独立 `SceneAuthoringCamera` 驱动，显示声明式对象或由 `.scene` 中 `LevelDirector` 字段构造的受控 procedural preview，并叠加网格、场景边界、对象/出生点/终点 marker 与名称，提供 `Frame All` / `Frame Selected`。

Demo 的完整玩法 Behaviour 已迁入工程唯一 `scripts/` 源目录；Player 由同一目录静态编译，Editor 由 Roslyn 动态编译。旧 `DemoSceneAuthoringBehaviours.cs` 同全名空壳已删除。动态编译器对齐 SDK implicit usings 与 nullable 上下文；武器目录改为经 Hosting 路径门控读取并由同源脚本执行 AOT-safe 显式解析，避免 Editor 动态编译依赖 build-time JSON source generator。

## 自动化验证

| 命令 | 结果 |
|---|---|
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName!~PerformanceHardeningToolingDisciplineTests"` | 434 passed，4 个 native/GL 条件 smoke skipped |
| `dotnet test tests/PixelEngine.Scripting.Tests/PixelEngine.Scripting.Tests.csproj -c Release --no-restore` | 90/90 passed |
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release --no-restore` | 96/96 passed |
| `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore` | 132 passed，1 个 native/GL 条件 smoke skipped |
| `dotnet build apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release --no-restore` | 0 warning，0 error |
| `pwsh tools/validate-task-catalog.ps1` | canonical/legacy/source coverage 校验通过 |
| `git diff --check` | 通过 |

`SceneAuthoringPreviewTests` 直接覆盖：

- `lava-mine` 的 640×360 世界边界来自实际 `LevelDirector` serialized fields；
- `LevelDirector`、`Player Spawn`、`Goal` marker 的身份和坐标；
- `empty-window-probe` 的“测试场景 · 显式空场景”状态与可 framing 边界；
- authoring camera 的 Frame/Zoom/Pan 坐标闭环；
- 无 runtime viewport 的 Frame All/Selected；
- dock 初建 1×1 临时尺寸不会锁死最大缩放，真实画布到达后重新 framing；
- Demo `scripts/` 可由运行时编译器完整编译，动态程序集中的 `LevelDirector` 含真实 `OnStart`、`BuildWorld` 与刚体状态 API，而非空壳。

## 真实窗口短跑

本机环境：Windows 11 build 26100、AMD Ryzen 7 5800X、AMD Radeon RX 7900 XT、.NET SDK 10.0.108、win-x64、1280×720 EditorShell framebuffer。

```powershell
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release --no-build -- `
  --project demo/PixelEngine.Demo `
  --window-ticks 12 `
  --ephemeral-user-state `
  --capture-frame artifacts/editor-007-scene-authoring-framed.bmp
```

结构化输出为 `frame_samples=12`、`editor_enabled=True`、`editor_running=True`、`editor_panels=23`、`editor_bridge_frames=12`、`project_open=True`。framebuffer 人工复核显示：Scene 面板内 640×360 边界完整展开，洞穴、两段平台、地面和熔岩池示意可见；`LevelDirector` 位于原点，出生点与终点按 scene 字段分离显示；网格覆盖 authoring canvas。首次截图揭示 dock 初建 1×1 尺寸会导致最大缩放，随后修复并由 `SceneViewDefersInitialFrameUntilDockCanvasIsUsable` 锁定。

原始 BMP 位于可再生 `artifacts/`，不是本报告的唯一证据；长期证据由实现 commit、自动化测试、命令结果与本报告共同构成。

## 边界

本报告证明 EDITOR-007 的本地实现、自动化契约和真实窗口渲染入口，不替代 `EDITOR-001`–`EDITOR-003` 所要求的真实鼠标键盘完整路线、DPI/IME 环境或 reviewer 结论，也不升级为 UI/Editor 人工验收证据。
