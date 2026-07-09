using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// GameObject Inspector 面板测试。
/// 不变式：Inspector 跟随当前选择态展示 GameObject、Component 或 Project Window 资产。
/// </summary>
public sealed class GameObjectInspectorPanelTests
{
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
        Assert.Equal("Open", snapshot.PrimaryActionLabel);
        Assert.Equal("就绪", snapshot.Status);

        Assert.False(missing.Found);
        Assert.Equal("Unknown", missing.Kind);
        Assert.Null(missing.AssetId);
        Assert.Contains("资产不存在", missing.Status, StringComparison.Ordinal);
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
            new AssetBrowserItem("textures/Crate.png", AssetBrowserItemKind.Texture, 30, DateTimeOffset.UnixEpoch, null, "asset_texture"),
        ]);
        List<string> openedScripts = [];
        List<string> instantiatedPrefabs = [];
        bool OpenScriptAsset(string path, out string diagnostic)
        {
            openedScripts.Add(path);
            diagnostic = $"opened {path}";
            return true;
        }

        GameObjectInspectorPanel panel = new(
            scene,
            undo,
            scripts,
            assetSource: source,
            instantiatePrefab: instantiatedPrefabs.Add,
            openScriptAsset: OpenScriptAsset);

        bool scriptOpened = panel.TryInvokePrimaryAssetAction("scripts/Player.cs");

        // Assert：验证预期结果
        Assert.True(scriptOpened);
        Assert.Equal(["scripts/Player.cs"], openedScripts);
        Assert.Equal("opened scripts/Player.cs", panel.Status);

        bool prefabInstantiated = panel.TryInvokePrimaryAssetAction("prefabs/Crate.prefab");

        Assert.True(prefabInstantiated);
        Assert.Equal(["prefabs/Crate.prefab"], instantiatedPrefabs);
        Assert.Equal("实例化 prefabs/Crate.prefab", panel.Status);

        bool textureHandled = panel.TryInvokePrimaryAssetAction("textures/Crate.png");

        Assert.False(textureHandled);
        Assert.Equal(["scripts/Player.cs"], openedScripts);
        Assert.Equal(["prefabs/Crate.prefab"], instantiatedPrefabs);
        Assert.Contains("没有 Inspector 主操作", panel.Status, StringComparison.Ordinal);
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


    private sealed class RecordingAssetSource(IReadOnlyList<AssetBrowserItem> assets) : IAssetBrowserDataSource, IAssetBrowserFolderDataSource
    {
        private IReadOnlyList<AssetBrowserFolderItem> _folders = [];

        public IReadOnlyList<AssetBrowserItem> ListAssets()
        {
            return assets;
        }

        public IReadOnlyList<AssetBrowserFolderItem> ListFolders()
        {
            return _folders;
        }

        public void ReplaceFolders(IReadOnlyList<AssetBrowserFolderItem> folders)
        {
            _folders = folders;
        }
    }

    private static string CreateTempProjectRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-inspector-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        return root;
    }
}
