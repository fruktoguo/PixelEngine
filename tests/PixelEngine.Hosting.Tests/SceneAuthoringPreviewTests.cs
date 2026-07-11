using Hexa.NET.ImGuizmo;
using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using System.Numerics;
using System.Reflection;
using Xunit;
using EditorUiMode = PixelEngine.Editor.EditorMode;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Scene View 独立 authoring 预览测试。
/// 不变式：Edit 模式不消费 runtime camera/viewport，且声明式世界信息始终可见、可定位。
/// </summary>
public sealed class SceneAuthoringPreviewTests
{
    /// <summary>
    /// 验证 Scene toolbar 的 active tool、grid 与 local/global 都映射到真实 authoring 状态。
    /// </summary>
    [Fact]
    public void SceneToolbarStateControlsGizmoAndGridBehaviour()
    {
        SceneViewPanel panel = new(EditorSceneModel.Empty(), new EditorUndoStack());

        Assert.Equal(ImGuizmoOperation.Translate, panel.Operation);
        Assert.Equal(ImGuizmoMode.Local, panel.GizmoMode);
        Assert.True(panel.ShowGrid);

        panel.SetOperation(ImGuizmoOperation.RotateZ);
        panel.ToggleGrid();
        panel.ToggleGizmoMode();

        Assert.Equal(ImGuizmoOperation.RotateZ, panel.Operation);
        Assert.Equal(ImGuizmoMode.World, panel.GizmoMode);
        Assert.False(panel.ShowGrid);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => panel.SetOperation(ImGuizmoOperation.Bounds));
    }

    /// <summary>
    /// 验证 lava-mine 的 LevelDirector 字段会生成非空世界边界与关键 marker。
    /// </summary>
    [Fact]
    public void LevelDirectorBuildsControlledProceduralPreview()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "lava-mine",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 7,
                    Name = "LevelDirector",
                    Transform = new EngineSceneTransformDocument { X = 12f, Y = 20f },
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = "PixelEngine.Demo.LevelDirector",
                            SerializedFields = new Dictionary<string, string>
                            {
                                ["LevelWidth"] = "640",
                                ["LevelHeight"] = "360",
                                ["PlayerSpawnX"] = "48",
                                ["PlayerSpawnY"] = "244",
                                ["GoalX"] = "570",
                                ["GoalY"] = "208",
                            },
                        },
                    ],
                },
            ],
        });

        SceneAuthoringPreview preview = SceneAuthoringPreviewBuilder.Build(scene);

        Assert.True(preview.HasProceduralWorld);
        Assert.False(preview.IsTestScene);
        Assert.False(preview.IsExplicitEmptyScene);
        Assert.Equal(new SceneAuthoringBounds(0f, 0f, 640f, 360f), preview.Bounds);
        Assert.Contains(preview.Markers, marker => marker is { StableId: 7, Name: "LevelDirector", Kind: SceneAuthoringMarkerKind.GameObject });
        Assert.Contains(preview.Markers, marker => marker is { Name: "Player Spawn", Position: { X: 48f, Y: 244f }, Kind: SceneAuthoringMarkerKind.PlayerSpawn });
        Assert.Contains(preview.Markers, marker => marker is { Name: "Goal", Position: { X: 570f, Y: 208f }, Kind: SceneAuthoringMarkerKind.Goal });
    }

    /// <summary>
    /// 验证 v2 Player/Goal anchor 是带 StableId 的真实 GameObject，可由 Scene picking 选择，
    /// 而不是停留在 Generated Markers 只读投影。
    /// </summary>
    [Fact]
    public void RealAuthoringAnchorsAreStableSelectableSceneObjects()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "authoring-anchors",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "LevelDirector",
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = "PixelEngine.Demo.LevelDirector",
                            SerializedFields = new Dictionary<string, string>
                            {
                                ["LevelWidth"] = "640",
                                ["LevelHeight"] = "360",
                                ["PlayerSpawnX"] = "12",
                                ["PlayerSpawnY"] = "12",
                                ["GoalX"] = "24",
                                ["GoalY"] = "24",
                            },
                        },
                    ],
                },
                new EngineSceneEntityDocument
                {
                    StableId = 2,
                    Name = "Player",
                    ParentId = 1,
                    Transform = new EngineSceneTransformDocument { X = 48f, Y = 244f },
                    Behaviours = [new EngineSceneBehaviourDocument { TypeName = "PixelEngine.Demo.PlayerSpawnPoint" }],
                },
                new EngineSceneEntityDocument
                {
                    StableId = 3,
                    Name = "Goal",
                    ParentId = 1,
                    Transform = new EngineSceneTransformDocument { X = 570f, Y = 208f },
                    Behaviours = [new EngineSceneBehaviourDocument { TypeName = "PixelEngine.Demo.GoalPoint" }],
                },
            ],
        });
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);

        SceneAuthoringPreview preview = panel.Preview;

        Assert.Contains(preview.Markers, marker => marker is { StableId: 2, Kind: SceneAuthoringMarkerKind.PlayerSpawn, Position: { X: 48f, Y: 244f } });
        Assert.Contains(preview.Markers, marker => marker is { StableId: 3, Kind: SceneAuthoringMarkerKind.Goal, Position: { X: 570f, Y: 208f } });
        Assert.DoesNotContain(preview.Markers, marker => !marker.StableId.HasValue && marker.Kind is SceneAuthoringMarkerKind.PlayerSpawn or SceneAuthoringMarkerKind.Goal);
        Assert.True(panel.TryPickWorld(new Vector2(48f, 244f), out int playerId));
        Assert.Equal(2, playerId);
        Assert.True(panel.TryPickWorld(new Vector2(570f, 208f), out int goalId));
        Assert.Equal(3, goalId);
    }

    /// <summary>
    /// 验证带父级旋转/缩放的 child gizmo 使用 world→local 转换，且完整拖动只形成一个 Undo 项。
    /// </summary>
    [Fact]
    public void GizmoGestureCommitsOneUndoAndPreservesParentLocalTransform()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "parented-gizmo",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Parent",
                    Transform = new EngineSceneTransformDocument
                    {
                        X = 20f,
                        Y = 30f,
                        RotationRadians = 0.4f,
                        ScaleX = 2f,
                        ScaleY = 2f,
                    },
                },
                new EngineSceneEntityDocument
                {
                    StableId = 2,
                    Name = "Child",
                    ParentId = 1,
                    Transform = new EngineSceneTransformDocument { X = 4f, Y = 5f, ScaleX = 1f, ScaleY = 1f },
                },
            ],
        });
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);
        EditorSceneTransform beforeLocal = scene.Get(2).Transform.Clone();
        EditorSceneTransform desiredWorld = scene.ComputeWorldTransform(2);
        desiredWorld.X += 80f;
        desiredWorld.Y -= 35f;

        Assert.True(panel.BeginGizmoTransform(2));
        Assert.True(panel.ApplyGizmoWorldTransform(2, desiredWorld));
        Assert.True(panel.ApplyGizmoWorldTransform(2, desiredWorld));
        Assert.True(panel.CommitGizmoTransform());
        EditorSceneTransform actualWorld = scene.ComputeWorldTransform(2);
        Assert.Equal(desiredWorld.X, actualWorld.X, 3);
        Assert.Equal(desiredWorld.Y, actualWorld.Y, 3);
        Assert.True(undo.CanUndo);

        Assert.True(undo.Undo(scene));
        Assert.False(undo.CanUndo);
        Assert.Equal(beforeLocal.X, scene.Get(2).Transform.X, 3);
        Assert.Equal(beforeLocal.Y, scene.Get(2).Transform.Y, 3);
        Assert.True(undo.Redo(scene));
        actualWorld = scene.ComputeWorldTransform(2);
        Assert.Equal(desiredWorld.X, actualWorld.X, 3);
        Assert.Equal(desiredWorld.Y, actualWorld.Y, 3);
    }

    /// <summary>
    /// 验证 selection 离开 gizmo 目标时提交当前连续编辑，且只形成一条 Undo。
    /// </summary>
    [Fact]
    public void GizmoSelectionChangeCommitsPendingTransform()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "selection-change",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Dragged",
                    Transform = new EngineSceneTransformDocument { X = 10f, Y = 20f },
                },
                new EngineSceneEntityDocument
                {
                    StableId = 2,
                    Name = "Next",
                    Transform = new EngineSceneTransformDocument { X = 80f, Y = 90f },
                },
            ],
        });
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);
        EditorSceneTransform desired = scene.ComputeWorldTransform(1);
        desired.X = 42f;

        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, desired));
        panel.PrepareFrame(selectedStableId: 2, EditorUiMode.Edit);

        Assert.True(undo.CanUndo);
        Assert.Equal(42f, scene.Get(1).Transform.X);
        Assert.True(undo.Undo(scene));
        Assert.Equal(10f, scene.Get(1).Transform.X);
        Assert.False(undo.CanUndo);
    }

    /// <summary>
    /// 验证整体替换场景后旧 gizmo 事务按 generation 丢弃；即使新场景复用 StableId，
    /// 也不会把旧 before 写入新对象的 Undo。
    /// </summary>
    [Fact]
    public void GizmoSceneReplacementCannotReuseStaleBeforeForSameStableId()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "old-scene",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Old",
                    Transform = new EngineSceneTransformDocument { X = 10f, Y = 20f },
                },
            ],
        });
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);
        EditorSceneTransform oldDesired = scene.ComputeWorldTransform(1);
        oldDesired.X = 99f;
        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, oldDesired));

        long oldGeneration = scene.SceneGeneration;
        EditorSceneModel replacement = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "new-scene",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "New",
                    Transform = new EngineSceneTransformDocument { X = 500f, Y = 600f },
                },
            ],
        });
        scene.ReplaceWith(replacement, markDirty: false);

        Assert.True(scene.SceneGeneration > oldGeneration);
        panel.PrepareFrame(selectedStableId: 1, EditorUiMode.Edit);
        Assert.False(panel.CommitGizmoTransform());
        Assert.False(undo.CanUndo);
        Assert.Equal(500f, scene.Get(1).Transform.X);

        EditorSceneTransform newDesired = scene.ComputeWorldTransform(1);
        newDesired.X = 520f;
        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, newDesired));
        Assert.True(panel.CommitGizmoTransform());
        Assert.True(undo.Undo(scene));
        Assert.Equal(500f, scene.Get(1).Transform.X);
    }

    /// <summary>
    /// 验证 gizmo 目标在连续编辑期间被删除时只清理事务，不读取已删除 StableId，也不产生 Undo。
    /// </summary>
    [Fact]
    public void GizmoDeletedTargetClearsWithoutThrowingOrCreatingUndo()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "delete-target",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Deleted",
                    Transform = new EngineSceneTransformDocument { X = 10f, Y = 20f },
                },
            ],
        });
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);
        EditorSceneTransform desired = scene.ComputeWorldTransform(1);
        desired.X = 75f;

        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, desired));
        _ = scene.DeleteSubtree(1);

        panel.PrepareFrame(selectedStableId: null, EditorUiMode.Edit);
        Assert.False(panel.CommitGizmoTransform());
        Assert.False(panel.CancelGizmoTransform());
        Assert.False(undo.CanUndo);
    }

    /// <summary>
    /// 验证从 Edit 进入 Play 时由每帧生命周期提交 gizmo 编辑，并禁止在非 Edit 模式开启新事务。
    /// </summary>
    [Fact]
    public void GizmoModeTransitionCommitsAndBlocksNewPlayModeTransaction()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "mode-transition",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Dragged",
                    Transform = new EngineSceneTransformDocument { X = 10f, Y = 20f },
                },
            ],
        });
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);
        EditorSceneTransform desired = scene.ComputeWorldTransform(1);
        desired.X = 45f;

        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, desired));
        panel.PrepareFrame(selectedStableId: 1, EditorUiMode.Play);

        Assert.True(undo.CanUndo);
        Assert.False(panel.BeginGizmoTransform(1));
        Assert.True(undo.Undo(scene));
        Assert.Equal(10f, scene.Get(1).Transform.X);
    }

    /// <summary>
    /// 验证 Scene View 被关闭时立即提交 live gizmo Transform，不依赖面板后续继续 Draw。
    /// </summary>
    [Fact]
    public void GizmoPanelCloseCommitsPendingTransform()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "panel-close",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Dragged",
                    Transform = new EngineSceneTransformDocument { X = 10f, Y = 20f },
                },
            ],
        });
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);
        EditorSceneTransform desired = scene.ComputeWorldTransform(1);
        desired.X = 55f;

        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, desired));
        panel.Visible = false;

        Assert.True(undo.CanUndo);
        Assert.False(panel.CommitGizmoTransform());
        Assert.True(undo.Undo(scene));
        Assert.Equal(10f, scene.Get(1).Transform.X);
    }

    /// <summary>
    /// 验证真实 Player/Goal Transform 修改进入 Undo/Redo，并经 .scene 保存重开保持坐标。
    /// </summary>
    [Fact]
    public void DemoAuthoringAnchorTransformUndoAndSceneSaveRoundTrip()
    {
        string root = FindRepositoryRoot();
        string sourcePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine.scene");
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"pixelengine-anchor-roundtrip-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDirectory);
        try
        {
            EditorSceneModel scene = EditorSceneModel.FromDocument(EngineSceneDocumentLoader.LoadDocument(sourcePath));
            EditorGameObject goal = scene.EnumerateDepthFirst().Single(item => item.Name == "Goal");
            EditorSceneTransform before = goal.Transform.Clone();
            EditorSceneTransform after = goal.Transform.Clone();
            after.X = 512f;
            after.Y = 176f;
            EditorUndoStack undo = new();

            undo.Execute(scene, new SetTransformCommand(goal.StableId, after));
            Assert.Equal(512f, goal.Transform.X);
            Assert.True(undo.Undo(scene));
            Assert.Equal(before.X, goal.Transform.X);
            Assert.True(undo.Redo(scene));

            string savedPath = Path.Combine(tempDirectory, "saved.scene");
            EngineSceneDocumentLoader.SaveDocument(scene.ToDocument(), savedPath);
            EditorSceneModel reopened = EditorSceneModel.FromDocument(EngineSceneDocumentLoader.LoadDocument(savedPath));
            EditorGameObject reopenedGoal = reopened.EnumerateDepthFirst().Single(item => item.Name == "Goal");
            Assert.Equal(512f, reopenedGoal.Transform.X);
            Assert.Equal(176f, reopenedGoal.Transform.Y);
            Assert.Equal(1, reopenedGoal.ParentId);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 验证空 probe 场景被明确标为测试与显式空场景，而非显示成加载失败。
    /// </summary>
    [Fact]
    public void EmptyProbeSceneHasExplicitTestIdentityAndFrameableBounds()
    {
        SceneAuthoringPreview preview = SceneAuthoringPreviewBuilder.Build(EditorSceneModel.Empty("empty-window-probe"));

        Assert.True(preview.IsTestScene);
        Assert.True(preview.IsExplicitEmptyScene);
        Assert.False(preview.HasProceduralWorld);
        Assert.Equal("测试场景 · 显式空场景", preview.StatusLabel);
        Assert.True(preview.Bounds.Width > 0f);
        Assert.True(preview.Bounds.Height > 0f);
    }

    /// <summary>
    /// 验证 authoring 相机 Frame/Zoom/Pan 坐标闭环，不依赖 runtime ScriptCameraApi。
    /// </summary>
    [Fact]
    public void AuthoringCameraFramesAndNavigatesIndependently()
    {
        SceneAuthoringCamera camera = new();
        camera.SetViewport(new Vector2(800f, 450f));
        camera.FrameBounds(new SceneAuthoringBounds(0f, 0f, 640f, 360f));

        Vector2 centerCanvas = camera.WorldToCanvas(new Vector2(320f, 180f));
        Assert.Equal(400f, centerCanvas.X, 3);
        Assert.Equal(225f, centerCanvas.Y, 3);

        Vector2 anchor = new(173f, 91f);
        Vector2 worldBeforeZoom = camera.CanvasToWorld(anchor);
        camera.ZoomAt(anchor, 3f);
        Vector2 worldAfterZoom = camera.CanvasToWorld(anchor);
        Assert.Equal(worldBeforeZoom.X, worldAfterZoom.X, 3);
        Assert.Equal(worldBeforeZoom.Y, worldAfterZoom.Y, 3);

        float previousCenterX = camera.CenterX;
        float previousCenterY = camera.CenterY;
        camera.PanPixels(new Vector2(10f, -8f));
        Assert.NotEqual(previousCenterX, camera.CenterX);
        Assert.NotEqual(previousCenterY, camera.CenterY);
    }

    /// <summary>
    /// 验证 Scene View 的 Frame All/Selected 可在无 runtime viewport 的 headless authoring 模型上工作。
    /// </summary>
    [Fact]
    public void SceneViewFramesWorldAndSelectionWithoutRuntimeViewport()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "authoring",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 3,
                    Name = "Marker",
                    Transform = new EngineSceneTransformDocument { X = 120f, Y = 80f },
                },
            ],
        });
        SceneViewPanel panel = new(scene, new EditorUndoStack());
        EditorSelection selection = new();

        Assert.True(panel.FrameAll());
        selection.SelectGameObject(3);
        Assert.True(panel.FrameSelected(selection));
        Assert.Equal(120f, panel.CameraSnapshot.CenterX);
        Assert.Equal(80f, panel.CameraSnapshot.CenterY);
    }

    /// <summary>
    /// 验证 dock 初建的 1×1 临时尺寸不会锁死首次 framing，真实尺寸到达后自动恢复完整世界。
    /// </summary>
    [Fact]
    public void SceneViewDefersInitialFrameUntilDockCanvasIsUsable()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "lava-mine",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "LevelDirector",
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = "PixelEngine.Demo.LevelDirector",
                            SerializedFields = new Dictionary<string, string>
                            {
                                ["LevelWidth"] = "640",
                                ["LevelHeight"] = "360",
                            },
                        },
                    ],
                },
            ],
        });
        SceneViewPanel panel = new(scene, new EditorUndoStack());

        Assert.False(panel.PrepareCanvas(new Vector2(1f, 1f)));
        Assert.True(panel.PrepareCanvas(new Vector2(800f, 450f)));
        Assert.InRange(panel.CameraSnapshot.CellsPerPixel, 0.8f, 1.1f);
        Assert.Equal(320f, panel.CameraSnapshot.CenterX);
        Assert.Equal(180f, panel.CameraSnapshot.CenterY);
    }

    /// <summary>
    /// 验证 Demo 的 Editor 热编译与 Player 静态编译消费同一 scripts 源码，并加载完整 LevelDirector 实现。
    /// </summary>
    [Fact]
    public void DemoEditorAndPlayerShareCompleteScriptSource()
    {
        string root = FindRepositoryRoot();
        string demoRoot = Path.Combine(root, "demo", "PixelEngine.Demo");
        string scriptRoot = Path.Combine(demoRoot, "scripts");
        string projectSource = File.ReadAllText(Path.Combine(demoRoot, "PixelEngine.Demo.csproj"));
        string levelDirectorSource = File.ReadAllText(Path.Combine(scriptRoot, "LevelDirector.cs"));

        RuntimeScriptAssemblyCompileResult result = RuntimeScriptAssemblyCompiler.CompileAndLoadFromDirectory(
            "PixelEngine.Demo.EditorSourceContract",
            scriptRoot);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.DoesNotContain("Compile Remove=\"scripts", projectSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("运行版 Demo 使用根目录下的完整实现", levelDirectorSource, StringComparison.Ordinal);
        Assert.NotNull(result.Assembly);
        Type levelDirector = result.Assembly.GetType("PixelEngine.Demo.LevelDirector", throwOnError: true)!;
        Assert.NotNull(levelDirector.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        Assert.NotNull(levelDirector.GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        Assert.NotNull(levelDirector.GetProperty("RigidStructureCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 PixelEngine 仓库根目录。");
    }
}
