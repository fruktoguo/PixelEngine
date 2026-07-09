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

    private sealed class RecordingAssetSource(IReadOnlyList<AssetBrowserItem> assets) : IAssetBrowserDataSource
    {
        public IReadOnlyList<AssetBrowserItem> ListAssets()
        {
            return assets;
        }
    }
}
