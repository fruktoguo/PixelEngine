using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using PixelEngine.UI;
using System.Numerics;
using Xunit;
using EditorSurfaceMode = PixelEngine.Editor.EditorMode;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// GameObject Inspector 面板测试。
/// 不变式：Inspector 跟随当前选择态展示 GameObject、Component 或 Project Window 资产。
/// </summary>
public sealed class GameObjectInspectorPanelTests
{
    /// <summary>
    /// 验证 Play/Paused 会在 Undo 栈边界阻止所有 authoring 命令，同时保留既有历史，
    /// 回到 Edit 后才能继续 Undo/Redo。
    /// </summary>
    [Fact]
    public void UndoStackBlocksAuthoringWritesOutsideEditMode()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("play-mode-write-gate");
        EditorGameObject gameObject = scene.Create("Original");
        EditorUndoStack undo = new();
        EditorSurfaceMode mode = EditorSurfaceMode.Play;
        undo.CanModifyScene = () => mode == EditorSurfaceMode.Edit;
        scene.MarkSaved();

        undo.Execute(scene, new RenameGameObjectCommand(gameObject.StableId, "Blocked"));

        Assert.Equal("Original", gameObject.Name);
        Assert.False(scene.IsDirty);
        Assert.False(undo.CanUndo);

        mode = EditorSurfaceMode.Edit;
        undo.Execute(scene, new RenameGameObjectCommand(gameObject.StableId, "Editable"));
        Assert.Equal("Editable", gameObject.Name);
        Assert.True(undo.CanUndo);

        mode = EditorSurfaceMode.Paused;
        Assert.False(undo.CanUndo);
        Assert.False(undo.Undo(scene));
        undo.Execute(scene, new SetGameObjectEnabledCommand(gameObject.StableId, enabled: false));
        Assert.True(gameObject.Enabled);
        Assert.Equal("Editable", gameObject.Name);

        mode = EditorSurfaceMode.Edit;
        Assert.True(undo.Undo(scene));
        Assert.Equal("Original", gameObject.Name);
        Assert.True(undo.CanRedo);

        mode = EditorSurfaceMode.Play;
        Assert.False(undo.CanRedo);
        Assert.False(undo.Redo(scene));
        Assert.Equal("Original", gameObject.Name);

        mode = EditorSurfaceMode.Edit;
        Assert.True(undo.Redo(scene));
        Assert.Equal("Editable", gameObject.Name);
    }

    /// <summary>
    /// 验证 Inspector 的连续编辑入口自身也遵守 Play/Paused 只读策略，
    /// 避免测试钩子或未来非 ImGui 调用绕过 disabled UI。
    /// </summary>
    [Fact]
    public void InspectorRejectsAuthoringEditsOutsideEditMode()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("inspector-play-read-only");
        EditorGameObject gameObject = scene.Create("Player");
        EditorComponentModel component = new("Game.PlayerController");
        component.SerializedFields["MoveSpeed"] = "1";
        gameObject.Components.Add(component);
        EditorSurfaceMode mode = EditorSurfaceMode.Play;
        using GameObjectInspectorPanel panel = new(
            scene,
            new EditorUndoStack(),
            new ScriptAssemblyRegistry(),
            modeProvider: () => mode);
        scene.MarkSaved();
        EditorSceneTransform blockedTransform = gameObject.Transform.Clone();
        blockedTransform.X = 24f;

        Assert.False(panel.BeginNameEdit(gameObject.StableId));
        Assert.False(panel.ApplyTransformEdit(gameObject.StableId, blockedTransform));
        Assert.False(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "8"));

        mode = EditorSurfaceMode.Paused;
        Assert.False(panel.ApplyNameEdit(gameObject.StableId, "Blocked"));
        Assert.False(panel.ApplyTransformEdit(gameObject.StableId, blockedTransform));
        Assert.False(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "12"));
        Assert.Equal("Player", gameObject.Name);
        Assert.Equal(0f, gameObject.Transform.X);
        Assert.Equal("1", component.SerializedFields["MoveSpeed"]);
        Assert.False(scene.IsDirty);

        mode = EditorSurfaceMode.Edit;
        Assert.True(panel.ApplyTransformEdit(gameObject.StableId, blockedTransform));
        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "8"));
        Assert.True(panel.ApplyNameEdit(gameObject.StableId, "Editable"));
        panel.CommitPendingEdits();

        Assert.Equal("Editable", gameObject.Name);
        Assert.Equal(24f, gameObject.Transform.X);
        Assert.Equal("8", component.SerializedFields["MoveSpeed"]);
        Assert.True(scene.IsDirty);
    }

    /// <summary>
    /// 验证 Unity 式 Hierarchy 搜索会保留命中节点的祖先路径，并隐藏无关分支。
    /// </summary>
    [Fact]
    public void HierarchySearchKeepsMatchingAncestorPath()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("hierarchy-search");
        EditorGameObject root = scene.Create("World");
        EditorGameObject player = scene.Create("Player Camera", root.StableId);
        EditorGameObject unrelated = scene.Create("Lighting");

        Assert.True(GameObjectHierarchyPanel.MatchesSearch(scene, root.StableId, "camera"));
        Assert.True(GameObjectHierarchyPanel.MatchesSearch(scene, player.StableId, "CAMERA"));
        Assert.False(GameObjectHierarchyPanel.MatchesSearch(scene, unrelated.StableId, "camera"));
        Assert.True(GameObjectHierarchyPanel.MatchesSearch(scene, unrelated.StableId, "  "));
    }

    /// <summary>
    /// 验证 Inspector 使用短组件名并以角度显示 2D Rotation，同时保持弧度存储往返。
    /// </summary>
    [Fact]
    public void InspectorFormatsUnityLikeComponentAndRotationLabels()
    {
        Assert.Equal("PlayerController", GameObjectInspectorPanel.GetComponentDisplayName("Game.Scripts.PlayerController"));
        Assert.Equal("Nested", GameObjectInspectorPanel.GetComponentDisplayName("Game.Outer+Nested"));
        Assert.Equal(180f, GameObjectInspectorPanel.RadiansToDegrees(MathF.PI), precision: 3);
        Assert.Equal(MathF.PI / 2f, GameObjectInspectorPanel.DegreesToRadians(90f), precision: 3);

        EditorComponentModel component = new("Game.Scripts.PlayerController");
        Assert.True(GameObjectInspectorPanel.IsComponentEnabled(component));
        component.SerializedFields[nameof(Behaviour.Enabled)] = bool.FalseString;
        Assert.False(GameObjectInspectorPanel.IsComponentEnabled(component));
    }

    /// <summary>
    /// 验证组件 enable 复选框从折叠箭头命中区之后开始，标题也不会覆盖复选框。
    /// </summary>
    [Fact]
    public void ComponentHeaderLayoutSeparatesArrowCheckboxAndLabel()
    {
        ComponentHeaderLayout layout = GameObjectInspectorPanel.ResolveComponentHeaderLayout(
            new Vector2(100f, 20f),
            new Vector2(420f, 44f),
            frameHeight: 20f,
            innerSpacingX: 8f,
            textLineHeight: 16f);

        Assert.Equal(120f, layout.ArrowLaneRight);
        Assert.True(layout.CheckboxPosition.X > layout.ArrowLaneRight);
        Assert.True(layout.LabelPosition.X >= layout.CheckboxPosition.X + 20f);
        Assert.InRange(layout.CheckboxPosition.Y, 20f, 24f);
    }

    /// <summary>
    /// 验证窄 Inspector 会把 Position/Scale 的 X、Y 改为分行布局，避免两个数值框互相挤压；
    /// 宽面板仍保留紧凑横排。
    /// </summary>
    [Fact]
    public void TransformFieldsAdaptToInspectorWidth()
    {
        Assert.Equal(TransformFieldLayout.StackedAxes, GameObjectInspectorPanel.ResolveTransformFieldLayout(220f));
        Assert.Equal(TransformFieldLayout.StackedAxes, GameObjectInspectorPanel.ResolveTransformFieldLayout(299.5f));
        Assert.Equal(TransformFieldLayout.InlineAxes, GameObjectInspectorPanel.ResolveTransformFieldLayout(300f));
        Assert.Equal(TransformFieldLayout.InlineAxes, GameObjectInspectorPanel.ResolveTransformFieldLayout(480f));
    }

    /// <summary>
    /// 验证 Vector2/3/4 会按实际分栏所需宽度切换横排与分行布局，
    /// 而不是在窄 Inspector 中把数值框压成不可编辑的细条。
    /// </summary>
    [Fact]
    public void VectorFieldsAdaptToInspectorWidthAndComponentCount()
    {
        Assert.Equal(VectorFieldLayout.StackedAxes, GameObjectInspectorPanel.ResolveVectorFieldLayout(151.5f, 2));
        Assert.Equal(VectorFieldLayout.InlineAxes, GameObjectInspectorPanel.ResolveVectorFieldLayout(152f, 2));
        Assert.Equal(VectorFieldLayout.StackedAxes, GameObjectInspectorPanel.ResolveVectorFieldLayout(227.5f, 3));
        Assert.Equal(VectorFieldLayout.InlineAxes, GameObjectInspectorPanel.ResolveVectorFieldLayout(228f, 3));
        Assert.Equal(VectorFieldLayout.StackedAxes, GameObjectInspectorPanel.ResolveVectorFieldLayout(303.5f, 4));
        Assert.Equal(VectorFieldLayout.InlineAxes, GameObjectInspectorPanel.ResolveVectorFieldLayout(304f, 4));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => GameObjectInspectorPanel.ResolveVectorFieldLayout(200f, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => GameObjectInspectorPanel.ResolveVectorFieldLayout(200f, 5));
    }

    /// <summary>
    /// 验证 Play Mode Inspector 的拖拽写回不会先压成 float 再转换，避免 Int64/UInt64/decimal
    /// 丢精度或溢出；nullable 空值仍保持 null 语义。
    /// </summary>
    [Fact]
    public void RuntimeNumberConversionPreservesExactScalarTypes()
    {
        static void AssertConverted<T>(string serialized, T expected)
        {
            Assert.True(GameObjectInspectorPanel.TryConvertRuntimeSerializedNumber(
                serialized,
                typeof(T),
                out object? converted));
            Assert.Equal(expected, Assert.IsType<T>(converted));
        }

        AssertConverted("255", byte.MaxValue);
        AssertConverted("-128", sbyte.MinValue);
        AssertConverted("-32768", short.MinValue);
        AssertConverted("65535", ushort.MaxValue);
        AssertConverted("-2147483648", int.MinValue);
        AssertConverted("4294967295", uint.MaxValue);
        AssertConverted("9223372036854775807", long.MaxValue);
        AssertConverted("18446744073709551615", ulong.MaxValue);
        AssertConverted("1.25", 1.25f);
        AssertConverted("1.0000000000000002", 1.0000000000000002d);
        AssertConverted("79228162514264337593543950335", decimal.MaxValue);

        Assert.True(GameObjectInspectorPanel.TryConvertRuntimeSerializedNumber(
            null,
            typeof(decimal?),
            out object? nullable));
        Assert.Null(nullable);
        Assert.False(GameObjectInspectorPanel.TryConvertRuntimeSerializedNumber("256", typeof(byte), out _));
        Assert.False(GameObjectInspectorPanel.TryConvertRuntimeSerializedNumber("NaN", typeof(float), out _));
        Assert.False(GameObjectInspectorPanel.TryConvertRuntimeSerializedNumber(null, typeof(int), out _));
    }

    /// <summary>
    /// 验证 Runtime Inspector 的 Vector2/3/4 分量写回保持原始向量类型，并拒绝维数不符与非有限值。
    /// </summary>
    [Fact]
    public void RuntimeVectorConversionPreservesShapeAndFiniteValues()
    {
        Assert.True(GameObjectInspectorPanel.TryCreateRuntimeVector(
            typeof(Vector2),
            [1f, 2f],
            out object? vector2));
        Assert.Equal(new Vector2(1f, 2f), Assert.IsType<Vector2>(vector2));

        Assert.True(GameObjectInspectorPanel.TryCreateRuntimeVector(
            typeof(Vector3?),
            [3f, 4f, 5f],
            out object? vector3));
        Assert.Equal(new Vector3(3f, 4f, 5f), Assert.IsType<Vector3>(vector3));

        Assert.True(GameObjectInspectorPanel.TryCreateRuntimeVector(
            typeof(Vector4),
            [6f, 7f, 8f, 9f],
            out object? vector4));
        Assert.Equal(new Vector4(6f, 7f, 8f, 9f), Assert.IsType<Vector4>(vector4));

        Assert.False(GameObjectInspectorPanel.TryCreateRuntimeVector(typeof(Vector3), [1f, 2f], out _));
        Assert.False(GameObjectInspectorPanel.TryCreateRuntimeVector(typeof(Vector2), [float.NaN, 2f], out _));
        Assert.False(GameObjectInspectorPanel.TryCreateRuntimeVector(typeof(string), [1f, 2f], out _));
    }

    /// <summary>
    /// 验证整数 Range 的下界向上取整、上界向下取整，且不把无可表示区间写成范围外值。
    /// </summary>
    [Fact]
    public void IntegerFieldRangeUsesDirectionalRounding()
    {
        static ScriptFieldDescriptor Field(Type type, double minimum, double maximum)
        {
            return new ScriptFieldDescriptor(
                "Value",
                type,
                0,
                CanWrite: true,
                IsPublic: true,
                IsSerializedPrivate: false,
                ScriptFieldKind.Number,
                minimum,
                maximum,
                AssetKind: null);
        }

        Assert.True(GameObjectInspectorPanel.TryResolveIntegerFieldRange(
            typeof(int), Field(typeof(int), 1.9d, 10d), out double positiveMinimum, out double positiveMaximum));
        Assert.Equal(2d, positiveMinimum);
        Assert.Equal(10d, positiveMaximum);

        Assert.True(GameObjectInspectorPanel.TryResolveIntegerFieldRange(
            typeof(long), Field(typeof(long), -2.9d, -1.1d), out double negativeMinimum, out double negativeMaximum));
        Assert.Equal(-2d, negativeMinimum);
        Assert.Equal(-2d, negativeMaximum);

        Assert.True(GameObjectInspectorPanel.TryResolveIntegerFieldRange(
            typeof(uint), Field(typeof(uint), -10d, 5.9d), out double unsignedMinimum, out double unsignedMaximum));
        Assert.Equal(0d, unsignedMinimum);
        Assert.Equal(5d, unsignedMaximum);
        Assert.False(GameObjectInspectorPanel.TryResolveIntegerFieldRange(
            typeof(int), Field(typeof(int), 1.1d, 1.9d), out _, out _));
    }

    /// <summary>
    /// 验证 Inspector 能从 Project Window 共享数据源生成资产摘要，并对缺失资产给出诊断。
    /// </summary>
    [Fact]
    public void InspectorPanelCapturesSelectedProjectAssetSummary()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel scene = EditorSceneModel.Empty("asset-inspector");
        EditorUndoStack undo = new();
        ScriptAssemblyRegistry scripts = new();
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem(
                "scripts/Player.cs",
                AssetBrowserItemKind.Script,
                1024,
                DateTimeOffset.UnixEpoch,
                null,
                "asset_script",
                "脚本：Player，1 KB"),
        ]);
        GameObjectInspectorPanel panel = new(scene, undo, scripts, assetSource: source);

        AssetInspectorSnapshot snapshot = panel.CaptureAssetInspector("scripts/Player.cs");
        AssetInspectorSnapshot missing = panel.CaptureAssetInspector("scripts/Missing.cs");

        // Assert：验证预期结果
        Assert.True(snapshot.Found);
        Assert.Equal("scripts/Player.cs", snapshot.Path);
        Assert.Equal("Script", snapshot.Kind);
        Assert.Equal("asset_script", snapshot.AssetId);
        Assert.Equal(1024, snapshot.SizeBytes);
        Assert.Equal("脚本：Player，1 KB", snapshot.PreviewSummary);
        Assert.Equal("Open Script", snapshot.PrimaryActionLabel);
        Assert.Equal(AssetBrowserPreviewContentKind.Text, snapshot.DetailedPreview?.ContentKind);
        Assert.Equal("就绪", snapshot.Status);

        Assert.False(missing.Found);
        Assert.Equal("Unknown", missing.Kind);
        Assert.Null(missing.AssetId);
        Assert.Contains("资产不存在", missing.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证详细素材预览由 Inspector 按文件签名懒加载并缓存，Project Window 不再拥有第二套预览状态。
    /// </summary>
    [Fact]
    public void InspectorOwnsDetailedAssetPreviewAndCachesByFileSignature()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("asset-preview-cache");
        AssetBrowserItem script = new(
            "ScriptSource/Player.cs",
            AssetBrowserItemKind.Script,
            20,
            DateTimeOffset.UnixEpoch,
            null,
            "asset_script");
        RecordingAssetSource source = new([script]);
        AssetBrowserDetailedPreview expected = new(
            "Player.cs",
            AssetBrowserPreviewContentKind.Text,
            "脚本：Player",
            [new AssetBrowserPreviewProperty("类型", "C# 脚本")],
            "public sealed class Player {}");
        source.SetPreview(script.Path, expected);
        using GameObjectInspectorPanel panel = new(scene, new EditorUndoStack(), new ScriptAssemblyRegistry(), assetSource: source);

        AssetInspectorSnapshot first = panel.CaptureAssetInspector(script.Path);
        AssetInspectorSnapshot second = panel.CaptureAssetInspector(script.Path);

        Assert.Same(expected, first.DetailedPreview);
        Assert.Same(first.DetailedPreview, second.DetailedPreview);
        Assert.Equal(1, source.PreviewReadCount);

        source.ReplaceAssets([script with { SizeBytes = 21 }]);
        _ = panel.CaptureAssetInspector(script.Path);

        Assert.Equal(2, source.PreviewReadCount);
    }

    /// <summary>
    /// 验证 Inspector 独占的图片预览 lease 会复用，并在面板释放时归还给生产缩略图数据源。
    /// </summary>
    [Fact]
    public void InspectorImagePreviewLeaseIsReusedAndReleasedOnDispose()
    {
        AssetBrowserItem texture = new(
            "Content/textures/Crate.png",
            AssetBrowserItemKind.Texture,
            128,
            DateTimeOffset.UnixEpoch,
            null,
            "asset_texture");
        RecordingAssetSource source = new([texture]);
        source.SetPreview(
            texture.Path,
            new AssetBrowserDetailedPreview(
                "Crate.png",
                AssetBrowserPreviewContentKind.Image,
                "32 × 16",
                []));
        GameObjectInspectorPanel panel = new(
            EditorSceneModel.Empty("asset-preview-thumbnail"),
            new EditorUndoStack(),
            new ScriptAssemblyRegistry(),
            assetSource: source);

        AssetThumbnail? first = panel.CaptureAssetPreviewThumbnail(texture.Path);
        AssetThumbnail? second = panel.CaptureAssetPreviewThumbnail(texture.Path);

        Assert.Equal(first, second);
        Assert.Equal([texture.Path], source.ThumbnailAcquisitions);
        panel.Dispose();
        Assert.Equal([(texture.Path, first!.Value.TextureHandle)], source.ThumbnailReleases);
    }

    /// <summary>
    /// 验证窄 Inspector 的 label/value 分栏与文本预览高度保持可读且有界。
    /// </summary>
    [Fact]
    public void InspectorAssetLayoutResolvesReadableBoundedColumns()
    {
        Assert.Equal(72f, GameObjectInspectorPanel.ResolveInspectorLabelWidth(120f));
        Assert.Equal(108f, GameObjectInspectorPanel.ResolveInspectorLabelWidth(300f), precision: 3);
        Assert.Equal(128f, GameObjectInspectorPanel.ResolveInspectorLabelWidth(600f));
        Assert.Equal(96f, GameObjectInspectorPanel.ResolveTextPreviewHeight(120f));
        Assert.Equal(180f, GameObjectInspectorPanel.ResolveTextPreviewHeight(400f));
        Assert.Equal(260f, GameObjectInspectorPanel.ResolveTextPreviewHeight(1000f));
    }

    /// <summary>
    /// 验证 Inspector 能从 Project Window 共享数据源生成文件夹摘要。
    /// </summary>
    [Fact]
    public void InspectorPanelCapturesSelectedProjectFolderSummary()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel scene = EditorSceneModel.Empty("folder-inspector");
        EditorUndoStack undo = new();
        ScriptAssemblyRegistry scripts = new();
        RecordingAssetSource source = new([]);
        source.ReplaceFolders(
        [
            new AssetBrowserFolderItem(string.Empty, 3),
            new AssetBrowserFolderItem("levels", 2),
        ]);
        GameObjectInspectorPanel panel = new(scene, undo, scripts, assetSource: source);

        FolderInspectorSnapshot root = panel.CaptureFolderInspector(string.Empty);
        FolderInspectorSnapshot levels = panel.CaptureFolderInspector("levels");
        FolderInspectorSnapshot missing = panel.CaptureFolderInspector("missing");

        // Assert：验证预期结果
        Assert.True(root.Found);
        Assert.Equal(string.Empty, root.Path);
        Assert.Equal(3, root.AssetCount);
        Assert.True(levels.Found);
        Assert.Equal("levels", levels.Path);
        Assert.Equal(2, levels.AssetCount);
        Assert.False(missing.Found);
        Assert.Contains("文件夹不存在", missing.Status, StringComparison.Ordinal);
    }


    /// <summary>
    /// 验证资产 Inspector 主操作复用 Project Window 的脚本打开与 Prefab 实例化能力，并写回状态。
    /// </summary>
    [Fact]
    public void InspectorPanelInvokesProjectAssetPrimaryActions()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel scene = EditorSceneModel.Empty("asset-inspector-actions");
        EditorUndoStack undo = new();
        ScriptAssemblyRegistry scripts = new();
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("scripts/Player.cs", AssetBrowserItemKind.Script, 10, DateTimeOffset.UnixEpoch, null, "asset_script"),
            new AssetBrowserItem("prefabs/Crate.prefab", AssetBrowserItemKind.Prefab, 20, DateTimeOffset.UnixEpoch, null, "asset_prefab"),
            new AssetBrowserItem("Content/scenes/Mine.scene", AssetBrowserItemKind.Scene, 25, DateTimeOffset.UnixEpoch, null, "asset_scene"),
            new AssetBrowserItem("textures/Crate.png", AssetBrowserItemKind.Texture, 30, DateTimeOffset.UnixEpoch, null, "asset_texture"),
        ]);
        List<string> openedScripts = [];
        List<string> instantiatedPrefabs = [];
        List<string> openedScenes = [];
        bool OpenScriptAsset(string path, out string diagnostic)
        {
            openedScripts.Add(path);
            diagnostic = $"opened {path}";
            return true;
        }

        bool OpenSceneAsset(string path, out string diagnostic)
        {
            openedScenes.Add(path);
            diagnostic = $"opened {path}";
            return true;
        }

        bool InstantiatePrefab(string path, out string diagnostic)
        {
            instantiatedPrefabs.Add(path);
            diagnostic = string.Empty;
            return true;
        }

        GameObjectInspectorPanel panel = new(
            scene,
            undo,
            scripts,
            assetSource: source,
            instantiatePrefab: InstantiatePrefab,
            openScriptAsset: OpenScriptAsset,
            openSceneAsset: OpenSceneAsset);

        bool scriptOpened = panel.TryInvokePrimaryAssetAction("scripts/Player.cs");

        // Assert：验证预期结果
        Assert.True(scriptOpened);
        Assert.Equal(["scripts/Player.cs"], openedScripts);
        Assert.Equal("opened scripts/Player.cs", panel.Status);

        bool prefabInstantiated = panel.TryInvokePrimaryAssetAction("prefabs/Crate.prefab");

        Assert.True(prefabInstantiated);
        Assert.Equal(["prefabs/Crate.prefab"], instantiatedPrefabs);
        Assert.Equal("实例化 prefabs/Crate.prefab", panel.Status);

        Assert.True(panel.TryInvokePrimaryAssetAction("Content/scenes/Mine.scene"));
        Assert.Equal(["Content/scenes/Mine.scene"], openedScenes);
        Assert.Equal("opened Content/scenes/Mine.scene", panel.Status);

        bool textureHandled = panel.TryInvokePrimaryAssetAction("textures/Crate.png");

        Assert.False(textureHandled);
        Assert.Equal(["scripts/Player.cs"], openedScripts);
        Assert.Equal(["prefabs/Crate.prefab"], instantiatedPrefabs);
        Assert.Contains("没有 Inspector 主操作", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证业务失败保留 Inspector 诊断，而 callback programmer error 不被面板重复捕获或吞掉。
    /// </summary>
    [Fact]
    public void InspectorPanelKeepsPrefabInstantiationFailuresInsideUiBoundary()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("prefab-action-failure");
        EditorUndoStack undo = new();
        ScriptAssemblyRegistry scripts = new();
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("prefabs/Broken.prefab", AssetBrowserItemKind.Prefab, 20, DateTimeOffset.UnixEpoch, null, "asset_prefab"),
        ]);
        static bool FailPrefab(string path, out string diagnostic)
        {
            diagnostic = $"Prefab 文件损坏：{path}";
            return false;
        }

        GameObjectInspectorPanel failedPanel = new(
            scene,
            undo,
            scripts,
            assetSource: source,
            instantiatePrefab: FailPrefab);
        Assert.False(failedPanel.TryInvokePrimaryAssetAction("prefabs/Broken.prefab"));
        Assert.Equal("Prefab 文件损坏：prefabs/Broken.prefab", failedPanel.Status);

        static bool ThrowPrefab(string _, out string diagnostic)
        {
            diagnostic = string.Empty;
            throw new InvalidOperationException("循环嵌套 prefab");
        }

        GameObjectInspectorPanel throwingPanel = new(
            scene,
            undo,
            scripts,
            assetSource: source,
            instantiatePrefab: ThrowPrefab);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => throwingPanel.TryInvokePrimaryAssetAction("prefabs/Broken.prefab"));
        Assert.Contains("循环嵌套 prefab", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Shell handler 是 Prefab 动作日志的唯一 owner，Inspector 只同步状态，不重复写第二条 Console。
    /// </summary>
    [Fact]
    public void InspectorPrefabActionDoesNotDuplicateShellConsoleResult()
    {
        EditorShellApp app = EditorShellApp.CreateForTests();
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("Content/prefabs/Crate.prefab", AssetBrowserItemKind.Prefab, 20, DateTimeOffset.UnixEpoch, null, "asset_prefab"),
        ]);
        GameObjectInspectorPanel panel = new(
            EditorSceneModel.Empty("prefab-console-owner"),
            new EditorUndoStack(),
            new ScriptAssemblyRegistry(),
            console: app.ConsoleStore,
            assetSource: source,
            instantiatePrefab: app.InstantiatePrefab);

        Assert.False(panel.TryInvokePrimaryAssetAction("Content/prefabs/Crate.prefab"));
        EditorConsoleEntry entry = Assert.Single(app.ConsoleStore.Snapshot());
        Assert.Equal("prefab-instantiator", entry.Source);
        Assert.Contains("没有打开的工程", entry.Text, StringComparison.Ordinal);
        Assert.Equal(entry.Text, panel.Status);
    }

    /// <summary>
    /// 验证 Project Window 选择资产或文件夹后，Hierarchy 不会把旧 GameObject 选择反写回来。
    /// </summary>
    [Fact]
    public void HierarchyPanelDoesNotOverrideProjectWindowSelection()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        string contentRoot = Path.Combine(projectRoot, "content");
        try
        {
            EditorSceneModel scene = EditorSceneModel.Empty("selection-arbitration");
            EditorUndoStack undo = new();
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorPrefabAssetStore prefabs = new(manifest.ContentRoot, manifest);
            GameObjectHierarchyPanel hierarchy = new(scene, undo, prefabs);
            EditorGameObject gameObject = scene.Create("Old Selection");
            scene.Select(gameObject.StableId);
            EditorSelection selection = new();

            selection.SelectFolder("levels");
            hierarchy.SyncSelection(selection);

            // Assert：验证预期结果
            Assert.Equal("levels", selection.FolderPath);
            Assert.Null(selection.GameObjectStableId);
            Assert.Null(scene.SelectedStableId);

            scene.Select(gameObject.StableId);
            selection.SelectAsset("scripts/Player.cs");
            hierarchy.SyncSelection(selection);

            Assert.Equal("scripts/Player.cs", selection.AssetPath);
            Assert.Null(selection.GameObjectStableId);
            Assert.Null(scene.SelectedStableId);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证从 A 的连续 Transform 编辑直接切换到 B 时，会显式收口一条 Undo，
    /// 不依赖已经不再绘制的 A 控件触发 IsItemDeactivatedAfterEdit。
    /// </summary>
    [Fact]
    public void InspectorSelectionChangeCommitsPendingTransformAsOneUndo()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("inspector-transform-transaction");
        EditorGameObject first = scene.Create("A");
        EditorGameObject second = scene.Create("B");
        EditorUndoStack undo = new();
        GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());
        EditorSceneTransform changed = first.Transform.Clone();
        changed.X = 42f;

        Assert.True(panel.BeginTransformEdit(first.StableId));
        Assert.True(panel.ApplyTransformEdit(first.StableId, changed));
        panel.PrepareFrame(second.StableId);

        Assert.True(undo.CanUndo);
        Assert.Equal(42f, scene.Get(first.StableId).Transform.X);
        Assert.True(undo.Undo(scene));
        Assert.Equal(0f, scene.Get(first.StableId).Transform.X);
        Assert.False(undo.CanUndo);
    }

    /// <summary>
    /// 验证连续拖拽同一个脚本字段只产生一条 Undo，并以激活前的序列化值作为基线。
    /// </summary>
    [Fact]
    public void InspectorComponentFieldDragCommitsAsOneUndo()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("inspector-component-field-transaction");
        EditorGameObject gameObject = scene.Create("Player");
        gameObject.PrefabLink = new EditorPrefabLink
        {
            AssetId = "prefab-player",
            AssetPath = "prefabs/player.prefab",
            SourceStableId = "1",
        };
        EditorComponentModel component = new("Game.PlayerController");
        component.SerializedFields["MoveSpeed"] = "1";
        gameObject.Components.Add(component);
        EditorUndoStack undo = new();
        GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());

        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "2"));
        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "3.5"));
        panel.CommitPendingComponentFieldEdit();

        Assert.Equal("3.5", component.SerializedFields["MoveSpeed"]);
        _ = Assert.Single(gameObject.PrefabLink.Overrides);
        Assert.True(undo.Undo(scene));
        Assert.Equal("1", component.SerializedFields["MoveSpeed"]);
        Assert.Empty(gameObject.PrefabLink.Overrides);
        Assert.False(undo.CanUndo);
        Assert.True(undo.Redo(scene));
        Assert.Equal("3.5", component.SerializedFields["MoveSpeed"]);
        _ = Assert.Single(gameObject.PrefabLink.Overrides);
    }

    /// <summary>
    /// 验证 Ctrl+Z 或其它场景命令会先通过 Undo 栈边界收口 active field，
    /// 且重入提交不会递归触发 flush。
    /// </summary>
    [Fact]
    public void UndoStackFlushesActiveComponentFieldBeforeOperation()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("inspector-component-field-flush");
        EditorGameObject gameObject = scene.Create("Player");
        EditorComponentModel component = new("Game.PlayerController");
        component.SerializedFields["MoveSpeed"] = "1";
        gameObject.Components.Add(component);
        EditorUndoStack undo = new();
        GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());
        undo.BeforeOperation = panel.CommitPendingEdits;

        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "4"));
        Assert.True(undo.Undo(scene));

        Assert.Equal("1", component.SerializedFields["MoveSpeed"]);
        Assert.False(undo.CanUndo);
        Assert.True(undo.CanRedo);

        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "6"));
        undo.Execute(scene, new SetGameObjectEnabledCommand(gameObject.StableId, enabled: false));
        Assert.False(gameObject.Enabled);
        Assert.Equal("6", component.SerializedFields["MoveSpeed"]);

        Assert.True(undo.Undo(scene));
        Assert.True(gameObject.Enabled);
        Assert.Equal("6", component.SerializedFields["MoveSpeed"]);
        Assert.True(undo.Undo(scene));
        Assert.Equal("1", component.SerializedFields["MoveSpeed"]);
    }

    /// <summary>
    /// 验证旧字段的 deactivation 边沿不会误提交同帧刚激活的新字段事务。
    /// </summary>
    [Fact]
    public void ComponentFieldDeactivationOnlyCommitsMatchingTransaction()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("inspector-component-field-deactivation");
        EditorGameObject gameObject = scene.Create("Player");
        EditorComponentModel component = new("Game.PlayerController");
        component.SerializedFields["A"] = "1";
        component.SerializedFields["B"] = "2";
        gameObject.Components.Add(component);
        EditorUndoStack undo = new();
        GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());

        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "A", "3"));
        Assert.False(panel.CommitComponentFieldEditIfMatches(gameObject.StableId, 0, "B"));
        Assert.False(undo.CanUndo);
        Assert.True(panel.CommitComponentFieldEditIfMatches(gameObject.StableId, 0, "A"));
        Assert.True(undo.CanUndo);
    }

    /// <summary>
    /// 验证拖拽前不存在的序列化字段在 Undo 时会被移除，而不是残留空字符串。
    /// </summary>
    [Fact]
    public void InspectorComponentFieldUndoRestoresMissingValue()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("inspector-component-field-missing-baseline");
        EditorGameObject gameObject = scene.Create("Player");
        EditorComponentModel component = new("Game.PlayerController");
        gameObject.Components.Add(component);
        EditorUndoStack undo = new();
        GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());

        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "SpawnOffset", "1,2,3"));
        panel.CommitPendingComponentFieldEdit();

        Assert.Equal("1,2,3", component.SerializedFields["SpawnOffset"]);
        Assert.True(undo.Undo(scene));
        Assert.False(component.SerializedFields.ContainsKey("SpawnOffset"));
    }

    /// <summary>
    /// 验证场景替换后即使 StableId 与组件索引相同，也不会把旧组件的字段事务应用到新对象。
    /// </summary>
    [Fact]
    public void InspectorDropsPendingComponentFieldWhenSceneReplacesTargetIdentity()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("old-component-scene");
        EditorGameObject oldObject = scene.Create("Old");
        EditorComponentModel oldComponent = new("Game.Controller");
        oldComponent.SerializedFields["Speed"] = "1";
        oldObject.Components.Add(oldComponent);
        EditorUndoStack undo = new();
        GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());
        Assert.True(panel.ApplyComponentFieldEdit(oldObject.StableId, 0, "Speed", "5"));

        EditorSceneModel replacement = EditorSceneModel.Empty("new-component-scene");
        EditorGameObject newObject = replacement.Create("New");
        EditorComponentModel newComponent = new("Game.Controller");
        newComponent.SerializedFields["Speed"] = "99";
        newObject.Components.Add(newComponent);
        Assert.Equal(oldObject.StableId, newObject.StableId);
        scene.ReplaceWith(replacement, markDirty: false);

        panel.PrepareFrame(newObject.StableId);

        Assert.False(undo.CanUndo);
        Assert.Equal("99", scene.Get(newObject.StableId).Components[0].SerializedFields["Speed"]);
    }

    /// <summary>
    /// 验证 ReplaceWith 后复用相同 StableId 的新对象不会继承旧 Inspector transaction baseline。
    /// </summary>
    [Fact]
    public void InspectorDropsPendingTransformWhenSceneReplacesTargetIdentity()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("old-scene");
        EditorGameObject oldObject = scene.Create("Old");
        EditorUndoStack undo = new();
        GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());
        EditorSceneTransform changed = oldObject.Transform.Clone();
        changed.X = 15f;
        Assert.True(panel.ApplyTransformEdit(oldObject.StableId, changed));

        EditorSceneModel replacement = EditorSceneModel.Empty("new-scene");
        EditorGameObject newObject = replacement.Create("New");
        Assert.Equal(oldObject.StableId, newObject.StableId);
        newObject.Transform.X = 99f;
        scene.ReplaceWith(replacement, markDirty: false);

        panel.PrepareFrame(newObject.StableId);

        Assert.False(undo.CanUndo);
        Assert.Equal(99f, scene.Get(newObject.StableId).Transform.X);
    }

    /// <summary>
    /// 验证 destructive transition 的 dirty 判断会先收口 Inspector 名称草稿，
    /// 避免尚未失焦的 InputText 内容绕过未保存修改提示而丢失。
    /// </summary>
    [Fact]
    public void DirtyCheckFlushesPendingInspectorNameBeforeInspectingSceneState()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("pending-name-dirty-check");
        EditorGameObject gameObject = scene.Create("Before");
        scene.MarkSaved();
        EditorUndoStack undo = new();
        using GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());

        Assert.True(panel.ApplyNameEdit(gameObject.StableId, "After"));
        Assert.Equal("Before", gameObject.Name);
        Assert.False(scene.IsDirty);

        bool observedCommittedName = false;
        bool dirty = EditorShellApp.FlushPendingAuthoringEditsAndCheckDirty(
            panel.CommitPendingEdits,
            () =>
            {
                observedCommittedName = string.Equals(gameObject.Name, "After", StringComparison.Ordinal);
                return scene.IsDirty;
            });

        Assert.True(observedCommittedName);
        Assert.True(dirty);
        Assert.Equal("After", gameObject.Name);
        Assert.Equal("Rename GameObject", undo.UndoName);
    }

    /// <summary>
    /// 验证真实 Prefab refresh 每帧替换 Transform baseline 后，连续 Inspector 拖拽仍保持最新值；
    /// 整次手势只提交一条 Undo，且 Undo/Redo 会连同 prefab overrides 一起恢复。
    /// </summary>
    [Fact]
    public void PrefabTransformLiveEditSurvivesRepeatedRefreshAsOneUndo()
    {
        string projectRoot = CreateTempProjectRoot();
        string contentRoot = Path.Combine(projectRoot, "content");
        try
        {
            EditorPrefabAssetStore prefabs = new(contentRoot);
            EditorSceneModel prefabSource = EditorSceneModel.Empty("prefab-transform-source");
            EditorGameObject source = prefabSource.Create("Player");
            source.Transform.X = 2f;
            prefabs.CreatePrefabFromSubtree(prefabSource, source.StableId, "prefabs/Player.prefab");

            EditorSceneModel scene = EditorSceneModel.Empty("prefab-transform-instance");
            EditorGameObject instance = prefabs.InstantiatePrefab(scene, "prefabs/Player.prefab", parentId: null);
            EditorUndoStack undo = new();
            using GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());

            EditorSceneTransform first = instance.Transform.Clone();
            first.X = 12f;
            Assert.True(panel.ApplyTransformEdit(instance.StableId, first));
            prefabs.RefreshPrefabInstances(scene);
            Assert.Equal(12f, instance.Transform.X);

            EditorSceneTransform second = instance.Transform.Clone();
            second.X = 24f;
            Assert.True(panel.ApplyTransformEdit(instance.StableId, second));
            prefabs.RefreshPrefabInstances(scene);
            Assert.Equal(24f, instance.Transform.X);

            panel.CommitPendingEdits();

            Assert.Equal("Set Transform", undo.UndoName);
            Assert.NotNull(instance.PrefabLink);
            Assert.Contains(
                instance.PrefabLink.Overrides,
                item => string.Equals(item.PropertyPath, "Transform.X", StringComparison.Ordinal) &&
                    string.Equals(item.Value, "24", StringComparison.Ordinal));

            Assert.True(undo.Undo(scene));
            prefabs.RefreshPrefabInstances(scene);
            Assert.Equal(2f, instance.Transform.X);
            Assert.Empty(instance.PrefabLink!.Overrides);
            Assert.False(undo.Undo(scene));

            Assert.True(undo.Redo(scene));
            prefabs.RefreshPrefabInstances(scene);
            Assert.Equal(24f, instance.Transform.X);
            Assert.Contains(
                instance.PrefabLink!.Overrides,
                item => string.Equals(item.PropertyPath, "Transform.X", StringComparison.Ordinal) &&
                    string.Equals(item.Value, "24", StringComparison.Ordinal));
            Assert.False(undo.CanRedo);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证真实 Prefab refresh 克隆组件对象后，连续字段编辑仍绑定同一逻辑字段；
    /// 最新值与 override 不丢失，并只产生一条可完整 Undo/Redo 的命令。
    /// </summary>
    [Fact]
    public void PrefabComponentLiveEditSurvivesComponentClonesAsOneUndo()
    {
        string projectRoot = CreateTempProjectRoot();
        string contentRoot = Path.Combine(projectRoot, "content");
        try
        {
            EditorPrefabAssetStore prefabs = new(contentRoot);
            EditorSceneModel prefabSource = EditorSceneModel.Empty("prefab-component-source");
            EditorGameObject source = prefabSource.Create("Player");
            EditorComponentModel sourceComponent = new("Game.PlayerController");
            sourceComponent.SerializedFields["MoveSpeed"] = "1";
            source.Components.Add(sourceComponent);
            prefabs.CreatePrefabFromSubtree(prefabSource, source.StableId, "prefabs/Player.prefab");

            EditorSceneModel scene = EditorSceneModel.Empty("prefab-component-instance");
            EditorGameObject instance = prefabs.InstantiatePrefab(scene, "prefabs/Player.prefab", parentId: null);
            EditorUndoStack undo = new();
            using GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());
            EditorComponentModel originalComponent = instance.Components[0];

            Assert.True(panel.ApplyComponentFieldEdit(instance.StableId, 0, "MoveSpeed", "2"));
            prefabs.RefreshPrefabInstances(scene);
            EditorComponentModel firstClone = instance.Components[0];
            Assert.NotSame(originalComponent, firstClone);
            Assert.Equal("2", firstClone.SerializedFields["MoveSpeed"]);

            Assert.True(panel.ApplyComponentFieldEdit(instance.StableId, 0, "MoveSpeed", "3.5"));
            prefabs.RefreshPrefabInstances(scene);
            EditorComponentModel secondClone = instance.Components[0];
            Assert.NotSame(firstClone, secondClone);
            Assert.Equal("3.5", secondClone.SerializedFields["MoveSpeed"]);

            panel.CommitPendingComponentFieldEdit();

            Assert.Equal("Set Component Field", undo.UndoName);
            Assert.Contains(
                instance.PrefabLink!.Overrides,
                item => string.Equals(item.PropertyPath, "Component:Game.PlayerController:MoveSpeed", StringComparison.Ordinal) &&
                    string.Equals(item.Value, "3.5", StringComparison.Ordinal));

            Assert.True(undo.Undo(scene));
            prefabs.RefreshPrefabInstances(scene);
            Assert.Equal("1", instance.Components[0].SerializedFields["MoveSpeed"]);
            Assert.Empty(instance.PrefabLink!.Overrides);
            Assert.False(undo.Undo(scene));

            Assert.True(undo.Redo(scene));
            prefabs.RefreshPrefabInstances(scene);
            Assert.Equal("3.5", instance.Components[0].SerializedFields["MoveSpeed"]);
            Assert.Contains(
                instance.PrefabLink!.Overrides,
                item => string.Equals(item.PropertyPath, "Component:Game.PlayerController:MoveSpeed", StringComparison.Ordinal) &&
                    string.Equals(item.Value, "3.5", StringComparison.Ordinal));
            Assert.False(undo.CanRedo);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Transform 与普通非 null 组件字段的连续编辑改回原值时不创建 Undo，
    /// 并恢复事务开始前的 dirty 状态；内容 Version 只允许继续递增。
    /// </summary>
    [Fact]
    public void RevertedContinuousEditsRestoreStartingDirtyStateWithoutUndo()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("reverted-continuous-edits");
        EditorGameObject gameObject = scene.Create("Player");
        gameObject.PrefabLink = new EditorPrefabLink
        {
            AssetId = "prefab-player",
            AssetPath = "prefabs/player.prefab",
            SourceStableId = "1",
        };
        EditorComponentModel component = new("Game.PlayerController");
        component.SerializedFields["MoveSpeed"] = "1";
        gameObject.Components.Add(component);
        EditorUndoStack undo = new();
        using GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());
        scene.MarkSaved();

        int transformStartVersion = scene.Version;
        EditorSceneTransform original = gameObject.Transform.Clone();
        EditorSceneTransform changed = original.Clone();
        changed.X = 18f;
        Assert.True(panel.ApplyTransformEdit(gameObject.StableId, changed));
        Assert.True(panel.ApplyTransformEdit(gameObject.StableId, original));
        panel.CommitPendingEdits();

        Assert.False(scene.IsDirty);
        Assert.True(scene.Version > transformStartVersion);
        Assert.False(undo.CanUndo);
        Assert.Empty(gameObject.PrefabLink!.Overrides);

        int componentStartVersion = scene.Version;
        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "2"));
        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "MoveSpeed", "1"));
        panel.CommitPendingComponentFieldEdit();

        Assert.Equal("1", component.SerializedFields["MoveSpeed"]);
        Assert.False(scene.IsDirty);
        Assert.True(scene.Version > componentStartVersion);
        Assert.False(undo.CanUndo);
        Assert.Empty(gameObject.PrefabLink.Overrides);
    }

    /// <summary>
    /// 验证 Prefab 字段未显式序列化时，nullable decimal 清空仍会用空字符串
    /// override 表达“覆盖脚本非 null 默认值为 null”，并生成一条可 Undo/Redo 命令。
    /// </summary>
    [Fact]
    public void PrefabMissingNullableValueKeepsExplicitNullOverrideAsUndoableChange()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("prefab-explicit-null");
        EditorGameObject gameObject = scene.Create("Player");
        gameObject.PrefabLink = new EditorPrefabLink
        {
            AssetId = "prefab-player",
            AssetPath = "prefabs/player.prefab",
            SourceStableId = "1",
        };
        EditorComponentModel component = new("Game.PlayerController");
        gameObject.Components.Add(component);
        EditorUndoStack undo = new();
        using GameObjectInspectorPanel panel = new(scene, undo, new ScriptAssemblyRegistry());
        scene.MarkSaved();

        Assert.True(panel.ApplyComponentFieldEdit(gameObject.StableId, 0, "Damage", null));
        panel.CommitPendingComponentFieldEdit();

        Assert.True(scene.IsDirty);
        Assert.False(component.SerializedFields.ContainsKey("Damage"));
        Assert.Equal("Set Component Field", undo.UndoName);
        EditorPrefabOverride explicitNull = Assert.Single(gameObject.PrefabLink!.Overrides);
        Assert.Equal("Component:Game.PlayerController:Damage", explicitNull.PropertyPath);
        Assert.Equal(string.Empty, explicitNull.Value);

        Assert.True(undo.Undo(scene));
        Assert.False(component.SerializedFields.ContainsKey("Damage"));
        Assert.Empty(gameObject.PrefabLink.Overrides);
        Assert.False(undo.CanUndo);

        Assert.True(undo.Redo(scene));
        Assert.False(component.SerializedFields.ContainsKey("Damage"));
        explicitNull = Assert.Single(gameObject.PrefabLink.Overrides);
        Assert.Equal("Component:Game.PlayerController:Damage", explicitNull.PropertyPath);
        Assert.Equal(string.Empty, explicitNull.Value);
        Assert.False(undo.CanRedo);
    }

    /// <summary>
    /// 验证 decimal 文本草稿不会因中间态尚不可解析而在下一帧被当前序列化值覆盖，
    /// 并验证提交时的 nullable null 与 Range clamp 语义。
    /// </summary>
    [Fact]
    public void DecimalDraftPreservesIntermediateTextAndNormalizesOnCommit()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("decimal-draft");
        EditorGameObject gameObject = scene.Create("Player");
        EditorComponentModel component = new("Game.PlayerController");
        component.SerializedFields["Damage"] = "1";
        gameObject.Components.Add(component);
        ScriptFieldDescriptor field = NumberField("Damage", typeof(decimal), minimum: -2.5d, maximum: 3.5d);
        DecimalFieldTextEditState state = new(
            gameObject.StableId,
            componentIndex: 0,
            field,
            scene.SceneGeneration,
            gameObject,
            component.TypeName,
            text: "1");

        state.Update("-");
        Assert.True(state.Matches(scene, gameObject.StableId, 0, field.Name));
        string nextFrameText = state.Text;
        state.Update(nextFrameText);

        Assert.True(state.Dirty);
        Assert.Equal("-", state.Text);
        Assert.False(GameObjectInspectorPanel.TryNormalizeDecimalFieldValue(field, state.Text, out _));
        Assert.True(GameObjectInspectorPanel.TryNormalizeDecimalFieldValue(field, "99", out string? high));
        Assert.Equal("3.5", high);
        Assert.True(GameObjectInspectorPanel.TryNormalizeDecimalFieldValue(field, "-99", out string? low));
        Assert.Equal("-2.5", low);

        ScriptFieldDescriptor nullable = NumberField("Damage", typeof(decimal?));
        Assert.True(GameObjectInspectorPanel.TryNormalizeDecimalFieldValue(nullable, string.Empty, out string? empty));
        Assert.Null(empty);
    }

    /// <summary>
    /// 验证浮点与 decimal 的 Range 必须和字段类型存在交集；完全落在可表示范围之外时
    /// 不得构造伪范围，也不得让 malformed/non-finite 标量进入拖拽编辑路径。
    /// </summary>
    [Fact]
    public void NumericValidationRejectsDisjointRangesMalformedAndNonFiniteValues()
    {
        ScriptFieldDescriptor floatAboveType = NumberField("Value", typeof(float), minimum: double.MaxValue);
        ScriptFieldDescriptor floatBelowType = NumberField("Value", typeof(float), maximum: double.MinValue);
        ScriptFieldDescriptor decimalAboveType = NumberField("Value", typeof(decimal), minimum: double.MaxValue);
        ScriptFieldDescriptor decimalBelowType = NumberField("Value", typeof(decimal), maximum: double.MinValue);

        Assert.False(GameObjectInspectorPanel.TryResolveFloatingFieldRange(typeof(float), floatAboveType, out _, out _));
        Assert.False(GameObjectInspectorPanel.TryResolveFloatingFieldRange(typeof(float), floatBelowType, out _, out _));
        Assert.False(GameObjectInspectorPanel.TryResolveDecimalFieldRange(decimalAboveType, out _, out _));
        Assert.False(GameObjectInspectorPanel.TryResolveDecimalFieldRange(decimalBelowType, out _, out _));

        Assert.False(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(int), "1.5"));
        Assert.False(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(int), "999999999999999999999"));
        Assert.False(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(float), "NaN"));
        Assert.False(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(float), "Infinity"));
        Assert.False(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(double), "-Infinity"));
        Assert.False(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(double), "1,2"));
        Assert.False(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(decimal), "NaN"));
        Assert.True(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(float), "1.25"));
        Assert.True(GameObjectInspectorPanel.IsValidNumericSerializedValue(typeof(decimal), "79228162514264337593543950335"));
    }


    /// <summary>
    /// 验证内建 Canvas/Scaler 的结构修改、primary 切换与 prefab override 都是单步 Undo/Redo。
    /// </summary>
    [Fact]
    public void CanvasCommandsRoundTripUndoPrimaryAndPrefabOverrides()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("canvas-commands");
        EditorGameObject first = scene.Create("HUD");
        EditorGameObject second = scene.Create("Menu");
        scene.SetPrefabLink(first.StableId, new EditorPrefabLink
        {
            AssetPath = "prefabs/hud.prefab",
            SourceStableId = "1",
        });
        scene.MarkSaved();
        EditorUndoStack undo = new();

        EditorWebCanvasComponent firstCanvas = new()
        {
            ManifestPath = "ui/ui-manifest.json",
            InitialScreenId = "hud",
            Primary = true,
        };
        EditorCanvasScalerComponent firstScaler = new()
        {
            Settings = UiCanvasScalerSettings.Default with
            {
                ScaleMode = UiScaleMode.ScaleWithScreenSize,
                ReferenceWidth = 1920f,
                ReferenceHeight = 1080f,
            },
        };
        undo.Execute(
            scene,
            new SetBuiltInCanvasComponentsCommand(first.StableId, firstCanvas, firstScaler));

        Assert.NotNull(first.WebCanvas);
        Assert.NotNull(first.CanvasScaler);
        Assert.Contains(first.PrefabLink!.Overrides, item =>
            item.PropertyPath == "WebCanvas.Exists" && item.Value == bool.TrueString);
        Assert.Contains(first.PrefabLink.Overrides, item =>
            item.PropertyPath == "CanvasScaler.ReferenceWidth" && item.Value == "1920");
        Assert.True(undo.Undo(scene));
        Assert.Null(first.WebCanvas);
        Assert.Null(first.CanvasScaler);
        Assert.Empty(first.PrefabLink!.Overrides);
        Assert.True(undo.Redo(scene));
        Assert.True(first.WebCanvas!.Primary);
        Assert.Equal(1920f, first.CanvasScaler!.Settings.ReferenceWidth);

        undo.Execute(
            scene,
            new SetBuiltInCanvasComponentsCommand(
                second.StableId,
                new EditorWebCanvasComponent { ManifestPath = "ui/menu.json" },
                new EditorCanvasScalerComponent()));
        undo.Execute(scene, new SetPrimaryWebCanvasCommand(second.StableId));

        Assert.False(first.WebCanvas.Primary);
        Assert.True(second.WebCanvas!.Primary);
        Assert.True(undo.Undo(scene));
        Assert.True(first.WebCanvas.Primary);
        Assert.False(second.WebCanvas.Primary);
        Assert.True(undo.Redo(scene));
        Assert.False(first.WebCanvas.Primary);
        Assert.True(second.WebCanvas.Primary);
    }

    /// <summary>
    /// 验证 Inspector 明确呈现 implicit、默认 Scaler、孤立 Scaler、Primary None 与冲突诊断。
    /// </summary>
    [Fact]
    public void CanvasInspectorSnapshotExposesDerivedIdentityAndDiagnostics()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("canvas-inspector");
        EditorGameObject canvasObject = scene.Create("Canvas");
        EditorGameObject orphan = scene.Create("Orphan Scaler");
        using GameObjectInspectorPanel panel = new(scene, new EditorUndoStack(), new ScriptAssemblyRegistry());

        CanvasInspectorSnapshot implicitSnapshot = panel.CaptureCanvasInspector(canvasObject.StableId);
        Assert.False(implicitSnapshot.HasExplicitCanvases);
        Assert.Contains("implicit primary", implicitSnapshot.Diagnostic, StringComparison.Ordinal);

        canvasObject.WebCanvas = new EditorWebCanvasComponent { Primary = true };
        CanvasInspectorSnapshot defaultScaler = panel.CaptureCanvasInspector(canvasObject.StableId);
        Assert.True(defaultScaler.HasExplicitCanvases);
        Assert.True(defaultScaler.UsesDefaultScaler);
        Assert.True(defaultScaler.IsEffectivePrimary);
        Assert.Equal(GameUiCanvasIdentity.FromStableId(canvasObject.StableId).Value, defaultScaler.DerivedCanvasId);
        Assert.Contains("默认值", defaultScaler.Diagnostic, StringComparison.Ordinal);

        orphan.CanvasScaler = new EditorCanvasScalerComponent();
        CanvasInspectorSnapshot orphanSnapshot = panel.CaptureCanvasInspector(orphan.StableId);
        Assert.True(orphanSnapshot.IsOrphanScaler);
        Assert.Contains("不会物化", orphanSnapshot.Diagnostic, StringComparison.Ordinal);

        canvasObject.WebCanvas.Enabled = false;
        CanvasInspectorSnapshot none = panel.CaptureCanvasInspector(canvasObject.StableId);
        Assert.True(none.PrimaryNone);
        Assert.False(none.IsRuntimeEnabled);

        canvasObject.WebCanvas.Enabled = true;
        orphan.WebCanvas = new EditorWebCanvasComponent { Primary = true };
        CanvasInspectorSnapshot conflict = panel.CaptureCanvasInspector(canvasObject.StableId);
        Assert.True(conflict.HasConflict);
        Assert.Contains("多个", conflict.Diagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 prefab 源没有内建 Canvas 时，scene instance 的结构 override 仍可在 baseline 刷新后保留。
    /// </summary>
    [Fact]
    public void PrefabInstanceCanvasStructuralOverridesSurviveRefresh()
    {
        string root = CreateTempProjectRoot();
        try
        {
            EditorSceneModel prefabScene = EditorSceneModel.Empty("plain-prefab");
            EditorGameObject source = prefabScene.Create("Plain");
            EditorPrefabAssetStore store = new(root);
            store.CreatePrefabFromSubtree(prefabScene, source.StableId, "prefabs/plain.prefab");

            EditorSceneModel scene = EditorSceneModel.Empty("instance");
            EditorGameObject instance = store.InstantiatePrefab(scene, "prefabs/plain.prefab", parentId: null);
            EditorUndoStack undo = new();
            undo.Execute(
                scene,
                new SetBuiltInCanvasComponentsCommand(
                    instance.StableId,
                    new EditorWebCanvasComponent { ManifestPath = "ui/ui-manifest.json", Primary = true },
                    new EditorCanvasScalerComponent
                    {
                        Settings = UiCanvasScalerSettings.Default with { ScaleFactor = 1.5f },
                    }));

            store.RefreshPrefabInstances(scene);

            Assert.Equal("ui/ui-manifest.json", instance.WebCanvas!.ManifestPath);
            Assert.True(instance.WebCanvas.Primary);
            Assert.Equal(1.5f, instance.CanvasScaler!.Settings.ScaleFactor);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 验证 prefab instance 能用空 optional override 清除源 Canvas 的 manifest 与 initial screen，
    /// baseline 刷新时不会因 null-coalescing 把源值错误复活。
    /// </summary>
    [Fact]
    public void PrefabCanvasOptionalOverridesCanClearSourceValues()
    {
        string root = CreateTempProjectRoot();
        try
        {
            EditorSceneModel prefabScene = EditorSceneModel.Empty("canvas-prefab");
            EditorGameObject source = prefabScene.Create("Canvas");
            prefabScene.SetBuiltInCanvasComponents(
                source.StableId,
                new EditorWebCanvasComponent
                {
                    ManifestAssetId = "source-manifest",
                    ManifestPath = "ui/source.json",
                    InitialScreenId = "source-screen",
                },
                new EditorCanvasScalerComponent());
            EditorPrefabAssetStore store = new(root);
            store.CreatePrefabFromSubtree(prefabScene, source.StableId, "prefabs/canvas.prefab");

            EditorSceneModel scene = EditorSceneModel.Empty("instance");
            EditorGameObject instance = store.InstantiatePrefab(scene, "prefabs/canvas.prefab", parentId: null);
            EditorWebCanvasComponent cleared = instance.WebCanvas!.Clone();
            cleared.ManifestAssetId = null;
            cleared.ManifestPath = null;
            cleared.InitialScreenId = null;
            EditorUndoStack undo = new();
            undo.Execute(
                scene,
                new SetBuiltInCanvasComponentsCommand(
                    instance.StableId,
                    cleared,
                    instance.CanvasScaler));

            store.RefreshPrefabInstances(scene);

            Assert.Null(instance.WebCanvas!.ManifestAssetId);
            Assert.Null(instance.WebCanvas.ManifestPath);
            Assert.Null(instance.WebCanvas.InitialScreenId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingAssetSource(IReadOnlyList<AssetBrowserItem> assets) :
        IAssetBrowserDataSource,
        IAssetBrowserFolderDataSource,
        IAssetBrowserPreviewDataSource,
        IAssetBrowserThumbnailDataSource
    {
        private IReadOnlyList<AssetBrowserItem> _assets = assets;
        private IReadOnlyList<AssetBrowserFolderItem> _folders = [];
        private readonly Dictionary<string, AssetBrowserDetailedPreview> _previews = new(StringComparer.OrdinalIgnoreCase);
        private uint _nextThumbnailHandle = 100;

        public int PreviewReadCount { get; private set; }

        public List<string> ThumbnailAcquisitions { get; } = [];

        public List<(string Path, uint TextureHandle)> ThumbnailReleases { get; } = [];

        public IReadOnlyList<AssetBrowserItem> ListAssets()
        {
            return _assets;
        }

        public IReadOnlyList<AssetBrowserFolderItem> ListFolders()
        {
            return _folders;
        }

        public void ReplaceFolders(IReadOnlyList<AssetBrowserFolderItem> folders)
        {
            _folders = folders;
        }

        public void ReplaceAssets(IReadOnlyList<AssetBrowserItem> assets)
        {
            _assets = assets;
        }

        public void SetPreview(string assetPath, AssetBrowserDetailedPreview preview)
        {
            _previews[assetPath] = preview;
        }

        public bool TryGetPreview(string assetPath, out AssetBrowserDetailedPreview preview)
        {
            PreviewReadCount++;
            return _previews.TryGetValue(assetPath, out preview!);
        }

        public bool TryAcquireThumbnail(string assetPath, out AssetThumbnail thumbnail)
        {
            ThumbnailAcquisitions.Add(assetPath);
            thumbnail = new AssetThumbnail(_nextThumbnailHandle++, 32, 16);
            return true;
        }

        public void ReleaseThumbnail(string assetPath, uint textureHandle)
        {
            ThumbnailReleases.Add((assetPath, textureHandle));
        }
    }

    private static string CreateTempProjectRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-inspector-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        return root;
    }

    private static ScriptFieldDescriptor NumberField(
        string name,
        Type fieldType,
        double? minimum = null,
        double? maximum = null)
    {
        return new ScriptFieldDescriptor(
            name,
            fieldType,
            Value: null,
            CanWrite: true,
            IsPublic: true,
            IsSerializedPrivate: false,
            ScriptFieldKind.Number,
            minimum,
            maximum,
            AssetKind: null);
    }
}
