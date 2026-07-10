using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Scene View 独立 authoring 预览测试。
/// 不变式：Edit 模式不消费 runtime camera/viewport，且声明式世界信息始终可见、可定位。
/// </summary>
public sealed class SceneAuthoringPreviewTests
{
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
