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
        Assert.Equal("就绪", snapshot.Status);

        Assert.False(missing.Found);
        Assert.Equal("Unknown", missing.Kind);
        Assert.Null(missing.AssetId);
        Assert.Contains("资产不存在", missing.Status, StringComparison.Ordinal);
    }

    private sealed class RecordingAssetSource(IReadOnlyList<AssetBrowserItem> assets) : IAssetBrowserDataSource
    {
        public IReadOnlyList<AssetBrowserItem> ListAssets()
        {
            return assets;
        }
    }
}
