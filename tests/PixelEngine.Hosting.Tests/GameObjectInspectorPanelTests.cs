using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using System.Numerics;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// GameObject Inspector 面板测试。
/// 不变式：Inspector 跟随当前选择态展示 GameObject、Component 或 Project Window 资产。
/// </summary>
public sealed class GameObjectInspectorPanelTests
{
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
}
