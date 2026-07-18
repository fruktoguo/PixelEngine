using Hexa.NET.ImGuizmo;
using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
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
    /// 验证 Scene 的 W/E/R/B 只接受无 modifier 裸键，不与构建等全局命令重叠。
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(false, false, false, false, true, false)]
    public void SceneToolShortcutsRequireUnmodifiedNonTextInput(
        bool wantTextInput,
        bool keyCtrl,
        bool keyShift,
        bool keyAlt,
        bool keySuper,
        bool expected)
    {
        Assert.Equal(
            expected,
            SceneToolShortcutPolicy.IsAllowed(
                wantTextInput,
                keyCtrl,
                keyShift,
                keyAlt,
                keySuper));
    }

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
        Assert.Equal(SceneGizmoSnapSettings.Default, panel.SnapSettings);

        panel.SetOperation(ImGuizmoOperation.RotateZ);
        panel.ToggleGrid();
        panel.ToggleGizmoMode();

        Assert.Equal(ImGuizmoOperation.RotateZ, panel.Operation);
        Assert.Equal(ImGuizmoMode.World, panel.GizmoMode);
        Assert.False(panel.ShowGrid);
        panel.SetSnapSettings(new SceneGizmoSnapSettings(true, 2f, 30f, 0.25f));
        Assert.True(panel.SnapSettings.Enabled);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => panel.SetOperation(ImGuizmoOperation.Bounds));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            panel.SetSnapSettings(new SceneGizmoSnapSettings(true, 0f, 30f, 0.25f)));
    }

    /// <summary>
    /// 验证 2D gizmo 的 Move/Rotate/Scale handle 在屏幕空间可见且命中区互不冒充。
    /// </summary>
    [Fact]
    public void SceneGizmoGeometryExposesVisibleHitTargetsForEveryTool()
    {
        EditorSceneTransform transform = new()
        {
            X = 10f,
            Y = 20f,
            RotationRadians = 0f,
            ScaleX = 1f,
            ScaleY = 1f,
        };
        Vector2 center = new(100f, 100f);
        SceneGizmoGeometry geometry = SceneViewPanel.BuildGizmoGeometry(transform, center, ImGuizmoMode.World);

        Assert.Equal(SceneGizmoHandle.Both, SceneViewPanel.ResolveGizmoHandle(in geometry, ImGuizmoOperation.Translate, center));
        Assert.Equal(SceneGizmoHandle.AxisX, SceneViewPanel.ResolveGizmoHandle(in geometry, ImGuizmoOperation.Translate, geometry.AxisXEnd));
        Assert.Equal(SceneGizmoHandle.AxisY, SceneViewPanel.ResolveGizmoHandle(in geometry, ImGuizmoOperation.Translate, geometry.AxisYEnd));
        Assert.Equal(SceneGizmoHandle.Rotate, SceneViewPanel.ResolveGizmoHandle(
            in geometry,
            ImGuizmoOperation.RotateZ,
            center + new Vector2(geometry.RotationRadius, 0f)));
        Assert.Equal(SceneGizmoHandle.Uniform, SceneViewPanel.ResolveGizmoHandle(in geometry, ImGuizmoOperation.Scale, geometry.UniformEnd));
        Assert.Equal(SceneGizmoHandle.None, SceneViewPanel.ResolveGizmoHandle(
            in geometry,
            ImGuizmoOperation.Translate,
            center + new Vector2(-40f, -40f)));
    }

    /// <summary>
    /// 验证 2D gizmo drag 对世界平移、旋转与缩放产生确定结果，供真实指针路径与 Undo 共用。
    /// </summary>
    [Fact]
    public void SceneGizmoDragAppliesMoveRotateAndScaleInWorldSpace()
    {
        EditorSceneTransform start = new()
        {
            X = 10f,
            Y = 20f,
            RotationRadians = 0f,
            ScaleX = 2f,
            ScaleY = 3f,
        };
        Vector2 center = new(100f, 100f);

        EditorSceneTransform moved = SceneViewPanel.ApplyGizmoDrag(
            start,
            SceneGizmoHandle.Both,
            ImGuizmoOperation.Translate,
            ImGuizmoMode.World,
            Vector2.Zero,
            new Vector2(5f, -3f),
            center,
            center + new Vector2(5f, -3f),
            center);
        Assert.Equal(15f, moved.X, precision: 3);
        Assert.Equal(17f, moved.Y, precision: 3);

        EditorSceneTransform rotated = SceneViewPanel.ApplyGizmoDrag(
            start,
            SceneGizmoHandle.Rotate,
            ImGuizmoOperation.RotateZ,
            ImGuizmoMode.World,
            Vector2.Zero,
            Vector2.Zero,
            center + new Vector2(38f, 0f),
            center + new Vector2(0f, 38f),
            center);
        Assert.Equal(MathF.PI / 2f, rotated.RotationRadians, precision: 3);

        EditorSceneTransform scaled = SceneViewPanel.ApplyGizmoDrag(
            start,
            SceneGizmoHandle.AxisX,
            ImGuizmoOperation.Scale,
            ImGuizmoMode.World,
            Vector2.Zero,
            Vector2.Zero,
            center + new Vector2(48f, 0f),
            center + new Vector2(72f, 0f),
            center);
        Assert.Equal(3f, scaled.ScaleX, precision: 3);
        Assert.Equal(3f, scaled.ScaleY, precision: 3);
    }

    /// <summary>Move/Rotate/Scale snapping 必须基于拖动 before-image，且 Local 轴向不能退化为世界轴。</summary>
    [Fact]
    public void SceneGizmoSnapQuantizesLocalMoveRotationAndScaleDeltas()
    {
        EditorSceneTransform start = new()
        {
            X = 10f,
            Y = 20f,
            RotationRadians = MathF.PI / 2f,
            ScaleX = 2f,
            ScaleY = 3f,
        };
        SceneGizmoSnapSettings settings = new(true, 2f, 15f, 0.25f);

        EditorSceneTransform moveCandidate = start.Clone();
        moveCandidate.Y += 2.6f;
        EditorSceneTransform moved = SceneViewPanel.ApplyGizmoSnap(
            start,
            moveCandidate,
            SceneGizmoHandle.AxisX,
            ImGuizmoOperation.Translate,
            ImGuizmoMode.Local,
            settings);
        Assert.Equal(10f, moved.X, precision: 3);
        Assert.Equal(22f, moved.Y, precision: 3);

        EditorSceneTransform rotateCandidate = start.Clone();
        rotateCandidate.RotationRadians += 20f * (MathF.PI / 180f);
        EditorSceneTransform rotated = SceneViewPanel.ApplyGizmoSnap(
            start,
            rotateCandidate,
            SceneGizmoHandle.Rotate,
            ImGuizmoOperation.RotateZ,
            ImGuizmoMode.World,
            settings);
        Assert.Equal(
            start.RotationRadians + (15f * (MathF.PI / 180f)),
            rotated.RotationRadians,
            precision: 3);

        EditorSceneTransform scaleCandidate = start.Clone();
        scaleCandidate.ScaleX += 0.38f;
        scaleCandidate.ScaleY += 0.38f;
        EditorSceneTransform scaled = SceneViewPanel.ApplyGizmoSnap(
            start,
            scaleCandidate,
            SceneGizmoHandle.Uniform,
            ImGuizmoOperation.Scale,
            ImGuizmoMode.World,
            settings);
        Assert.Equal(2.5f, scaled.ScaleX, precision: 3);
        Assert.Equal(3.5f, scaled.ScaleY, precision: 3);
    }

    /// <summary>
    /// 验证世界画刷默认关闭、与对象工具互斥，并在 Edit/Play 间持续写入同一个当前 world。
    /// </summary>
    [Fact]
    public void SceneMaterialBrushIsExplicitMutuallyExclusiveAndAvailableInPlay()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "brush-routing",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Selected",
                    Transform = new EngineSceneTransformDocument { X = 10f, Y = 10f },
                },
            ],
        });
        RecordingEditApi edit = new();
        MaterialBrushPalettePanel brush = new(CreateBrushMaterials(), edit);
        brush.HostInSceneView();
        brush.Visible = false;
        RecordingWorldTexture worldTexture = new();
        int worldChangedCount = 0;
        SceneViewPanel panel = new(
            scene,
            new EditorUndoStack(),
            brush,
            worldTexture,
            worldChanged: () => worldChangedCount++);
        EditorSelection selection = new();
        scene.Select(1);
        selection.SelectGameObject(1);
        _ = panel.PrepareCanvas(new Vector2(800f, 450f));

        panel.HandleScenePointer(selection, new Vector2(799f, 449f), clicked: true, dragging: false);

        Assert.False(panel.MaterialBrushActive);
        Assert.Empty(edit.Painted);
        Assert.Equal(0, worldTexture.InvalidationCount);
        Assert.Null(selection.GameObjectStableId);

        scene.Select(1);
        selection.SelectGameObject(1);
        Assert.True(panel.SetMaterialBrushActive(true));
        Assert.True(brush.Visible);
        panel.HandleScenePointer(selection, new Vector2(400f, 225f), clicked: true, dragging: false);

        Assert.True(panel.MaterialBrushActive);
        Assert.NotEmpty(edit.Painted);
        Assert.Equal(1, worldTexture.InvalidationCount);
        Assert.Equal(1, worldChangedCount);
        Assert.Equal(1, selection.GameObjectStableId);
        Assert.False(panel.BeginGizmoTransform(1));

        panel.SetOperation(ImGuizmoOperation.RotateZ);
        Assert.False(panel.MaterialBrushActive);
        Assert.Equal(ImGuizmoOperation.RotateZ, panel.Operation);

        Assert.True(panel.SetMaterialBrushActive(true));
        panel.PrepareFrame(selectedStableId: 1, EditorUiMode.Play);
        Assert.True(panel.MaterialBrushActive);
        panel.HandleScenePointer(selection, new Vector2(400f, 225f), clicked: true, dragging: false);
        Assert.True(panel.SetMaterialBrushActive(true));
        Assert.Equal(2, worldTexture.InvalidationCount);
        Assert.Equal(2, worldChangedCount);
    }

    /// <summary>
    /// 验证 Play/Paused 的 Scene View 从 runtime hierarchy 拾取、聚焦并临时修改同一实体，
    /// Stop 路径使用的数据源恢复后不污染 authoring baseline。
    /// </summary>
    [Fact]
    public void SceneRuntimeSelectionAndGizmoEditSameLiveEntityAndRestore()
    {
        PixelEngine.Scripting.Scene runtimeScene = new();
        Entity entity = runtimeScene.CreateEntity();
        Transform transform = entity.AddComponent<Transform>();
        transform.SetPosition(120f, 72f);
        transform.RotationRadians = 0.25f;
        transform.ScaleX = 1.5f;
        transform.ScaleY = 2f;
        RuntimeSceneHierarchyDataSource runtime = RuntimeSceneHierarchyDataSource.CreateDynamic(
            () => runtimeScene,
            displayNameProvider: id => id == entity.Id ? "Player" : null);
        int worldChangedCount = 0;
        SceneViewPanel panel = new(
            EditorSceneModel.Empty("runtime-world"),
            new EditorUndoStack(),
            runtimeSource: runtime,
            worldChanged: () => worldChangedCount++);
        string handle = $"script:{entity.Id}";
        panel.PrepareFrame(null, EditorUiMode.Play, handle);

        Assert.True(panel.TryPickRuntimeWorld(new Vector2(120f, 72f), out string? pickedEntity, out int? pickedBody));
        Assert.Equal(handle, pickedEntity);
        Assert.Null(pickedBody);

        EditorSelection selection = new();
        selection.SelectEntity(handle);
        Assert.True(panel.FrameSelected(selection));
        Assert.Equal(120f, panel.CameraSnapshot.CenterX, precision: 3);
        Assert.Equal(72f, panel.CameraSnapshot.CenterY, precision: 3);

        EditorSceneTransform desired = new()
        {
            X = 180f,
            Y = 96f,
            RotationRadians = 0.75f,
            ScaleX = 2.5f,
            ScaleY = 3f,
        };
        Assert.True(panel.ApplyRuntimeGizmoWorldTransform(handle, desired));
        Assert.Equal(180f, transform.X);
        Assert.Equal(96f, transform.Y);
        Assert.Equal(0.75f, transform.RotationRadians);
        Assert.Equal(2.5f, transform.ScaleX);
        Assert.Equal(3f, transform.ScaleY);
        Assert.Equal(1, worldChangedCount);

        runtime.RestoreTemporaryEdits();
        Assert.Equal(120f, transform.X);
        Assert.Equal(72f, transform.Y);
        Assert.Equal(0.25f, transform.RotationRadians);
        Assert.Equal(1.5f, transform.ScaleX);
        Assert.Equal(2f, transform.ScaleY);
    }

    /// <summary>
    /// 验证 Scene 内工具浮层在左右停靠、浮动和窄视口下始终留在画布范围内。
    /// </summary>
    [Fact]
    public void SceneToolOverlayLayoutDocksAndClampsInsideCanvas()
    {
        Vector2 canvasMin = new(100f, 50f);
        Vector2 canvasSize = new(800f, 450f);
        Vector2 desiredSize = new(300f, 390f);

        SceneToolOverlayLayout left = SceneViewPanel.ResolveToolOverlayLayout(
            canvasMin,
            canvasSize,
            desiredSize,
            SceneToolOverlayDock.Left,
            Vector2.Zero);
        SceneToolOverlayLayout right = SceneViewPanel.ResolveToolOverlayLayout(
            canvasMin,
            canvasSize,
            desiredSize,
            SceneToolOverlayDock.Right,
            Vector2.Zero);
        SceneToolOverlayLayout floated = SceneViewPanel.ResolveToolOverlayLayout(
            canvasMin,
            canvasSize,
            desiredSize,
            SceneToolOverlayDock.Floating,
            new Vector2(999f, 999f));
        SceneToolOverlayLayout narrow = SceneViewPanel.ResolveToolOverlayLayout(
            canvasMin,
            new Vector2(180f, 120f),
            desiredSize,
            SceneToolOverlayDock.Right,
            Vector2.Zero);

        Assert.Equal(new Vector2(108f, 58f), left.Position);
        Assert.Equal(new Vector2(592f, 58f), right.Position);
        Assert.Equal(new Vector2(592f, 102f), floated.Position);
        Assert.Equal(new Vector2(164f, 104f), narrow.Size);
        Assert.Equal(new Vector2(108f, 58f), narrow.Position);
    }

    /// <summary>
    /// 验证浮层仅在接近 Scene 左右边缘时吸附，画布中央保持浮动。
    /// </summary>
    [Theory]
    [InlineData(110f, 300f, 1)]
    [InlineData(590f, 300f, 2)]
    [InlineData(300f, 300f, 0)]
    public void SceneToolOverlayDockingOnlySnapsNearCanvasEdges(
        float positionX,
        float width,
        int expected)
    {
        SceneToolOverlayDock actual = SceneViewPanel.ResolveToolOverlayDock(
            new Vector2(positionX, 80f),
            new Vector2(width, 200f),
            new Vector2(100f, 50f),
            new Vector2(800f, 450f),
            snapDistance: 24f);

        Assert.Equal((SceneToolOverlayDock)expected, actual);
    }

    /// <summary>
    /// 验证 lava-mine 的 LevelDirector 字段会生成非空世界边界与关键 marker。
    /// </summary>
    [Fact]
    public void ExplicitProviderSnapshotBuildsAuthoritativeWorldPreview()
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

        AuthoringWorldPreviewSnapshot world = new(
            Version: 1,
            HasWorld: true,
            new SceneAuthoringBounds(0f, 0f, 640f, 360f),
            WorldOwnerStableId: 7);
        SceneAuthoringPreview preview = SceneAuthoringPreviewBuilder.Build(scene, world);

        Assert.True(preview.HasAuthoritativeWorld);
        Assert.False(preview.IsTestScene);
        Assert.False(preview.IsExplicitEmptyScene);
        Assert.Equal(new SceneAuthoringBounds(0f, 0f, 640f, 360f), preview.Bounds);
        Assert.Contains(preview.Markers, marker => marker is { StableId: 7, Name: "LevelDirector", Kind: SceneAuthoringMarkerKind.GameObject });
        Assert.Contains(preview.Markers, marker => marker is { Name: "Player Spawn", Position: { X: 48f, Y: 244f }, Kind: SceneAuthoringMarkerKind.PlayerSpawn });
        Assert.Contains(preview.Markers, marker => marker is { Name: "Goal", Position: { X: 570f, Y: 208f }, Kind: SceneAuthoringMarkerKind.Goal });
    }

    /// <summary>
    /// 验证组件类名本身不能让项目隐式进入 authoring world 预览。
    /// </summary>
    [Fact]
    public void LevelDirectorNameWithoutProviderSnapshotDoesNotOptIn()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "name-is-not-contract",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 7,
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

        SceneAuthoringPreview preview = SceneAuthoringPreviewBuilder.Build(scene);

        Assert.False(preview.HasAuthoritativeWorld);
        Assert.Null(preview.WorldOwnerStableId);
        Assert.DoesNotContain(preview.Markers, marker => !marker.StableId.HasValue);
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
    /// 验证真实 lava-mine Player/Goal 都走 Scene gizmo 连续事务，Move/Rotate/Scale
    /// 会同步 marker 与 Inspector 共用的 Transform，并且宿主 flush 重入后仍只有一条 Undo。
    /// </summary>
    [Theory]
    [InlineData("Player")]
    [InlineData("Goal")]
    public void DemoAuthoringAnchorsUseSceneGizmoWithOneUndo(string objectName)
    {
        string root = FindRepositoryRoot();
        string sourcePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine.scene");
        EditorSceneModel scene = EditorSceneModel.FromDocument(EngineSceneDocumentLoader.LoadDocument(sourcePath));
        EditorGameObject target = scene.EnumerateDepthFirst().Single(item => item.Name == objectName);
        EditorSceneTransform before = target.Transform.Clone();
        EditorSceneTransform desiredWorld = scene.ComputeWorldTransform(target.StableId);
        desiredWorld.X += 24f;
        desiredWorld.Y -= 12f;
        desiredWorld.RotationRadians += MathF.PI / 4f;
        desiredWorld.ScaleX = 1.75f;
        desiredWorld.ScaleY = 0.6f;
        EditorUndoStack undo = new();
        SceneViewPanel panel = new(scene, undo);
        undo.BeforeOperation = () => _ = panel.CommitGizmoTransform();
        panel.PrepareFrame(target.StableId, EditorUiMode.Edit);

        Assert.True(panel.BeginGizmoTransform(target.StableId));
        Assert.True(panel.ApplyGizmoWorldTransform(target.StableId, desiredWorld));

        SceneAuthoringMarker marker = Assert.Single(
            panel.Preview.Markers,
            item => item.StableId == target.StableId);
        Assert.Equal(desiredWorld.X, marker.Position.X, 3);
        Assert.Equal(desiredWorld.Y, marker.Position.Y, 3);
        Assert.Equal(desiredWorld.RotationRadians, marker.RotationRadians, 3);
        Assert.Equal(desiredWorld.ScaleX, marker.ScaleX, 3);
        Assert.Equal(desiredWorld.ScaleY, marker.ScaleY, 3);
        Assert.True(panel.CommitGizmoTransform());

        Assert.True(undo.Undo(scene));
        Assert.False(undo.CanUndo);
        Assert.Equal(before.X, target.Transform.X, 3);
        Assert.Equal(before.Y, target.Transform.Y, 3);
        Assert.Equal(before.RotationRadians, target.Transform.RotationRadians, 3);
        Assert.Equal(before.ScaleX, target.Transform.ScaleX, 3);
        Assert.Equal(before.ScaleY, target.Transform.ScaleY, 3);

        Assert.True(undo.Redo(scene));
        EditorSceneTransform actualWorld = scene.ComputeWorldTransform(target.StableId);
        Assert.Equal(desiredWorld.X, actualWorld.X, 3);
        Assert.Equal(desiredWorld.Y, actualWorld.Y, 3);
        Assert.Equal(desiredWorld.RotationRadians, actualWorld.RotationRadians, 3);
        Assert.Equal(desiredWorld.ScaleX, actualWorld.ScaleX, 3);
        Assert.Equal(desiredWorld.ScaleY, actualWorld.ScaleY, 3);
    }

    /// <summary>
    /// 验证 Unity 式 Scene Visibility / Picking 是非落盘编辑器状态，父级状态递归影响子级，
    /// 并分别真实控制 preview 绘制、鼠标命中与 gizmo。
    /// </summary>
    [Fact]
    public void SceneVisibilityAndPickingAreEditorOnlyRecursiveControls()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "scene-state",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Parent",
                    Transform = new EngineSceneTransformDocument { X = 0f, Y = 0f },
                },
                new EngineSceneEntityDocument
                {
                    StableId = 2,
                    Name = "Child",
                    ParentId = 1,
                    Transform = new EngineSceneTransformDocument { X = 100f, Y = 50f },
                },
            ],
        });
        SceneViewPanel panel = new(scene, new EditorUndoStack());
        int contentVersion = scene.Version;
        Assert.False(scene.IsDirty);
        Assert.Equal(2, panel.Preview.Markers.Length);

        scene.SetSceneVisible(1, visible: false);

        Assert.False(scene.IsSceneVisible(1));
        Assert.False(scene.IsSceneVisible(2));
        Assert.Empty(panel.Preview.Markers);
        Assert.False(panel.TryPickWorld(new Vector2(100f, 50f), out _));
        Assert.False(panel.BeginGizmoTransform(2));
        Assert.False(scene.IsDirty);
        Assert.Equal(contentVersion, scene.Version);

        scene.SetSceneVisible(1, visible: true);
        scene.SetScenePickable(1, pickable: false);

        Assert.Equal(2, panel.Preview.Markers.Length);
        Assert.False(scene.IsScenePickable(2));
        Assert.False(panel.TryPickWorld(new Vector2(100f, 50f), out _));
        Assert.False(panel.BeginGizmoTransform(2));
        Assert.True(Assert.Single(scene.ToDocument().Entities!, entity => entity.StableId == 1).Enabled!.Value);
        Assert.True(Assert.Single(scene.ToDocument().Entities!, entity => entity.StableId == 2).Enabled!.Value);
        Assert.Equal(contentVersion, scene.Version);

        scene.SetAllScenePickable(pickable: true);

        Assert.True(panel.TryPickWorld(new Vector2(100f, 50f), out int picked));
        Assert.Equal(2, picked);
        Assert.True(panel.BeginGizmoTransform(2));
        Assert.False(panel.CommitGizmoTransform());
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
    /// 验证取消或改回原值的 gizmo 手势会恢复进入事务前的 clean/dirty 状态，
    /// 但内容 Version 仍保持单调，避免已观察者误判为旧快照。
    /// </summary>
    [Fact]
    public void GizmoCancelAndNoOpCommitRestoreDirtyState()
    {
        EditorSceneModel scene = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "cancel-clean-state",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "Anchor",
                    Transform = new EngineSceneTransformDocument { X = 10f, Y = 20f },
                },
            ],
        });
        SceneViewPanel panel = new(scene, new EditorUndoStack());
        EditorSceneTransform before = scene.ComputeWorldTransform(1);
        EditorSceneTransform changed = before.Clone();
        changed.X = 64f;
        int originalVersion = scene.Version;

        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, changed));
        Assert.True(scene.IsDirty);
        Assert.True(panel.CancelGizmoTransform());
        Assert.False(scene.IsDirty);
        Assert.Equal(before.X, scene.Get(1).Transform.X);
        Assert.True(scene.Version > originalVersion);

        int cancelVersion = scene.Version;
        Assert.True(panel.BeginGizmoTransform(1));
        Assert.True(panel.ApplyGizmoWorldTransform(1, changed));
        Assert.True(panel.ApplyGizmoWorldTransform(1, before));
        Assert.False(panel.CommitGizmoTransform());
        Assert.False(scene.IsDirty);
        Assert.Equal(before.X, scene.Get(1).Transform.X);
        Assert.True(scene.Version > cancelVersion);
    }

    /// <summary>
    /// 验证 prefab gizmo live drag 会立刻写 provisional Transform overrides，
    /// Cancel 同时恢复原 Transform、prefab link 与事务开始前的 clean 状态。
    /// </summary>
    [Fact]
    public void PrefabGizmoLiveDragPersistsUntilCancelRestoresBaseline()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("prefab-gizmo");
        EditorGameObject target = scene.Create("Prefab Anchor");
        target.PrefabLink = new EditorPrefabLink
        {
            AssetId = "prefab-anchor",
            AssetPath = "prefabs/anchor.prefab",
            SourceStableId = "1",
        };
        scene.MarkSaved();
        SceneViewPanel panel = new(scene, new EditorUndoStack());
        EditorSceneTransform desired = scene.ComputeWorldTransform(target.StableId);
        desired.X = 88f;
        desired.Y = 44f;

        Assert.True(panel.BeginGizmoTransform(target.StableId));
        Assert.True(panel.ApplyGizmoWorldTransform(target.StableId, desired));
        Assert.NotNull(target.PrefabLink);
        Assert.Equal(5, target.PrefabLink.Overrides.Count);
        Assert.Contains(target.PrefabLink.Overrides, item => item.PropertyPath == "Transform.X" && item.Value == "88");
        Assert.Contains(target.PrefabLink.Overrides, item => item.PropertyPath == "Transform.Y" && item.Value == "44");

        Assert.True(panel.CancelGizmoTransform());
        Assert.False(scene.IsDirty);
        Assert.Equal(0f, target.Transform.X);
        Assert.Equal(0f, target.Transform.Y);
        Assert.NotNull(target.PrefabLink);
        Assert.Empty(target.PrefabLink.Overrides);
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
        Assert.False(preview.HasAuthoritativeWorld);
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
    /// 验证 Brush footprint 使用 cell 半径构造，并随 Scene camera 缩放而不是保持固定屏幕大小。
    /// </summary>
    [Fact]
    public void BrushFootprintScalesWithAuthoringCameraAndPreservesIndependentAxes()
    {
        SceneBrushFootprintGeometry zoomedIn = SceneViewPanel.BuildBrushFootprintGeometry(
            new Vector2(120f, 80f),
            cellsPerPixel: 0.25f,
            EditorBrushShape.Circle,
            radiusX: 2,
            radiusY: 5);
        SceneBrushFootprintGeometry zoomedOut = SceneViewPanel.BuildBrushFootprintGeometry(
            new Vector2(120f, 80f),
            cellsPerPixel: 1f,
            EditorBrushShape.Circle,
            radiusX: 2,
            radiusY: 5);
        SceneBrushFootprintGeometry point = SceneViewPanel.BuildBrushFootprintGeometry(
            Vector2.Zero,
            cellsPerPixel: 0.5f,
            EditorBrushShape.Point,
            radiusX: 128,
            radiusY: 128);

        Assert.Equal(new Vector2(10f, 22f), zoomedIn.HalfSize);
        Assert.Equal(new Vector2(2.5f, 5.5f), zoomedOut.HalfSize);
        Assert.Equal(new Vector2(1f, 1f), point.HalfSize);
        Assert.Equal(EditorBrushShape.Circle, zoomedIn.Shape);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneViewPanel.BuildBrushFootprintGeometry(Vector2.Zero, 0f, EditorBrushShape.Circle, 1, 1));
    }

    /// <summary>
    /// 验证外部自动化可直接恢复 Scene authoring camera，并严格拒绝非有限值与越界 zoom。
    /// </summary>
    [Fact]
    public void AuthoringCameraAcceptsBoundedSemanticViewState()
    {
        SceneAuthoringCamera camera = new();

        camera.SetView(12.5f, -4.25f, 0.5f);

        Assert.Equal(12.5f, camera.CenterX);
        Assert.Equal(-4.25f, camera.CenterY);
        Assert.Equal(0.5f, camera.CellsPerPixel);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            camera.SetView(float.NaN, 0f, 1f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            camera.SetView(0f, 0f, SceneAuthoringCamera.MinCellsPerPixel / 2f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            camera.SetView(0f, 0f, SceneAuthoringCamera.MaxCellsPerPixel * 2f));
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
        AuthoringWorldPreviewSnapshot world = new(
            Version: 1,
            HasWorld: true,
            new SceneAuthoringBounds(0f, 0f, 640f, 360f),
            WorldOwnerStableId: 1);
        SceneViewPanel panel = new(
            scene,
            new EditorUndoStack(),
            authoringWorldSnapshot: () => world);

        Assert.False(panel.PrepareCanvas(new Vector2(1f, 1f)));
        Assert.True(panel.PrepareCanvas(new Vector2(800f, 450f)));
        Assert.InRange(panel.CameraSnapshot.CellsPerPixel, 0.8f, 1.1f);
        Assert.Equal(320f, panel.CameraSnapshot.CenterX);
        Assert.Equal(180f, panel.CameraSnapshot.CenterY);
    }

    /// <summary>
    /// 验证 dock 从初始全宽布局收敛到实际窄面板时，未被用户调整的相机会按最终 viewport 重新 Frame All；
    /// 一旦用户 Frame Selected，后续 resize 则保留其主动取景。
    /// </summary>
    [Fact]
    public void SceneViewRefitsDockResizeUntilUserFramesSelection()
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
                },
                new EngineSceneEntityDocument
                {
                    StableId = 2,
                    Name = "Player",
                    ParentId = 1,
                    Transform = new EngineSceneTransformDocument { X = 48f, Y = 244f },
                },
            ],
        });
        AuthoringWorldPreviewSnapshot world = new(
            Version: 1,
            HasWorld: true,
            new SceneAuthoringBounds(0f, 0f, 640f, 360f),
            WorldOwnerStableId: 1);
        SceneViewPanel panel = new(
            scene,
            new EditorUndoStack(),
            authoringWorldSnapshot: () => world);

        Assert.True(panel.PrepareCanvas(new Vector2(960f, 540f)));
        float wideCellsPerPixel = panel.CameraSnapshot.CellsPerPixel;

        Assert.True(panel.PrepareCanvas(new Vector2(515f, 487f)));
        Assert.True(panel.CameraSnapshot.CellsPerPixel > wideCellsPerPixel);
        Assert.Equal(320f, panel.CameraSnapshot.CenterX);
        Assert.Equal(180f, panel.CameraSnapshot.CenterY);

        EditorSelection selection = new();
        selection.SelectGameObject(2);
        Assert.True(panel.FrameSelected(selection));
        float selectedCellsPerPixel = panel.CameraSnapshot.CellsPerPixel;

        Assert.False(panel.PrepareCanvas(new Vector2(700f, 487f)));
        Assert.Equal(48f, panel.CameraSnapshot.CenterX);
        Assert.Equal(244f, panel.CameraSnapshot.CenterY);
        Assert.Equal(selectedCellsPerPixel, panel.CameraSnapshot.CellsPerPixel);
    }

    /// <summary>
    /// 验证空 GameObject marker 的可视几何真实编码 world rotation 与非均匀 scale。
    /// </summary>
    [Fact]
    public void EmptyGameObjectMarkerGeometryReflectsRotationAndScale()
    {
        SceneAuthoringMarker marker = new(
            StableId: 9,
            Name: "Empty",
            Position: Vector2.Zero,
            SceneAuthoringMarkerKind.GameObject,
            RotationRadians: MathF.PI / 2f,
            ScaleX: 2f,
            ScaleY: 0.5f);

        SceneGameObjectMarkerGeometry geometry = SceneViewPanel.BuildGameObjectMarkerGeometry(marker);

        Assert.Equal(0f, geometry.AxisX.X, 3);
        Assert.Equal(14f, geometry.AxisX.Y, 3);
        Assert.Equal(-3.5f, geometry.AxisY.X, 3);
        Assert.Equal(0f, geometry.AxisY.Y, 3);
        Assert.Equal(14f, geometry.Radius, 3);
    }

    /// <summary>
    /// 验证 Player/Goal 专用 marker 不再是固定屏幕矩形，而会真实显示 gizmo 写入的旋转与非均匀缩放。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AuthoringAnchorMarkerGeometryReflectsRotationAndScale(int kindValue)
    {
        SceneAuthoringMarkerKind kind = (SceneAuthoringMarkerKind)kindValue;
        SceneAuthoringMarker marker = new(
            StableId: 2,
            Name: kind.ToString(),
            Position: Vector2.Zero,
            kind,
            RotationRadians: MathF.PI / 2f,
            ScaleX: 2f,
            ScaleY: 0.5f);

        SceneAnchorMarkerGeometry geometry = SceneViewPanel.BuildAnchorMarkerGeometry(marker);

        Assert.Equal(0f, geometry.AxisX.X, 3);
        Assert.Equal(20f, geometry.AxisX.Y, 3);
        Assert.Equal(-5f, geometry.AxisY.X, 3);
        Assert.Equal(0f, geometry.AxisY.Y, 3);

        marker = marker with { ScaleX = -2f };
        geometry = SceneViewPanel.BuildAnchorMarkerGeometry(marker);
        Assert.Equal(0f, geometry.AxisX.X, 3);
        Assert.Equal(-20f, geometry.AxisX.Y, 3);
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

    private static MaterialTable CreateBrushMaterials()
    {
        return new MaterialTable(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1f, TextureId = -1 },
            new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder, HeatCapacity = 1f, TextureId = -1 },
        ]);
    }

    private sealed class RecordingEditApi : ISimulationEditApi
    {
        public List<(int X, int Y, ushort Material)> Painted { get; } = [];

        public void PaintCell(int worldX, int worldY, ushort material)
        {
            Painted.Add((worldX, worldY, material));
        }

        public int PaintRect(int minX, int minY, int maxX, int maxY, ushort material)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Painted.Add((x, y, material));
                }
            }

            return (maxX - minX + 1) * (maxY - minY + 1);
        }

        public void ClearCell(int worldX, int worldY)
        {
        }

        public int ClearRect(int minX, int minY, int maxX, int maxY)
        {
            return (maxX - minX + 1) * (maxY - minY + 1);
        }

        public void AddTemperature(int worldX, int worldY, float deltaCelsius)
        {
        }

        public void SetTemperature(int worldX, int worldY, float targetCelsius)
        {
        }
    }

    private sealed class RecordingWorldTexture : IAuthoringWorldTexture
    {
        public int InvalidationCount { get; private set; }

        public long Revision => InvalidationCount;

        public SceneWorldTextureSnapshot GetTexture(SceneAuthoringBounds requestedBounds)
        {
            throw new InvalidOperationException("此测试不执行 ImGui 绘制。");
        }

        public void Invalidate()
        {
            InvalidationCount++;
        }

        public void Dispose()
        {
        }
    }
}
