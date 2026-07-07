using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 资源浏览器面板测试。
/// </summary>
public sealed class AssetBrowserPanelTests
{
    /// <summary>
    /// 验证文件系统数据源会分类 content 资产并接入缩略图提供器。
    /// </summary>
    [Fact]
    public void FileSystemDataSourceClassifiesContentAssetsAndThumbnails()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-assets-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "materials.json"), "{}");
            _ = Directory.CreateDirectory(Path.Combine(root, "textures"));
            File.WriteAllBytes(Path.Combine(root, "textures", "sand.png"), [1, 2, 3]);
            _ = Directory.CreateDirectory(Path.Combine(root, "audio"));
            File.WriteAllBytes(Path.Combine(root, "audio", "hit.wav"), [4, 5, 6]);
            File.WriteAllText(Path.Combine(root, "demo.scene"), "{}");
            _ = Directory.CreateDirectory(Path.Combine(root, "prefabs"));
            File.WriteAllText(Path.Combine(root, "prefabs", "rock.prefab"), "{}");
            RecordingThumbnailProvider thumbnails = new();
            FileSystemAssetBrowserDataSource source = new(root, thumbnails);

            IReadOnlyList<AssetBrowserItem> assets = source.ListAssets();

            Assert.Contains(assets, item => item.Path == "materials.json" && item.Kind == AssetBrowserItemKind.Material);
            AssetBrowserItem texture = Assert.Single(assets, item => item.Path == "textures/sand.png");
            Assert.Equal(AssetBrowserItemKind.Texture, texture.Kind);
            Assert.Equal(new AssetThumbnail(12, 16, 16), texture.Thumbnail);
            Assert.Contains(assets, item => item.Path == "audio/hit.wav" && item.Kind == AssetBrowserItemKind.Audio);
            Assert.Contains(assets, item => item.Path == "demo.scene" && item.Kind == AssetBrowserItemKind.Scene);
            Assert.Contains(assets, item => item.Path == "prefabs/rock.prefab" && item.Kind == AssetBrowserItemKind.Prefab);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 验证资源浏览器可筛选、选择并试听音频。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelFiltersSelectsAndPreviewsAudio()
    {
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 10, DateTimeOffset.UnixEpoch, new AssetThumbnail(5, 8, 8)),
            new AssetBrowserItem("audio/hit.wav", AssetBrowserItemKind.Audio, 20, DateTimeOffset.UnixEpoch, null),
            new AssetBrowserItem("scenes/demo.scene", AssetBrowserItemKind.Scene, 30, DateTimeOffset.UnixEpoch, null),
        ]);
        RecordingAudioPreview preview = new();
        AssetBrowserPanel panel = new(source, preview);
        EditorSelection selection = new();

        _ = panel.Refresh();
        panel.SetSearch("audio");
        bool selected = panel.SelectAsset("audio/hit.wav", selection);
        bool played = panel.TryPreviewAudio("audio/hit.wav");

        AssetBrowserItem filtered = Assert.Single(panel.FilteredAssets);
        Assert.Equal("audio/hit.wav", filtered.Path);
        Assert.True(selected);
        Assert.Equal("audio/hit.wav", selection.AssetPath);
        Assert.True(played);
        Assert.Equal(["audio/hit.wav"], preview.Played);
    }

    /// <summary>
    /// 验证 Project Window 能为 Shell 资产拖拽语义创建 typed payload，并拒绝缺 stable id 的旧数据源项。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelCreatesTypedDragPayloadOnlyForStableAssets()
    {
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("prefabs/rock.prefab", AssetBrowserItemKind.Prefab, 10, DateTimeOffset.UnixEpoch, null, "asset_prefab"),
            new AssetBrowserItem("textures/legacy.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null),
        ]);
        AssetBrowserPanel panel = new(source);

        _ = panel.Refresh();
        bool created = panel.TryCreateDragPayload("prefabs/rock.prefab", out AssetBrowserDragPayload payload);
        bool legacyCreated = panel.TryCreateDragPayload("textures/legacy.png", out AssetBrowserDragPayload legacyPayload);

        Assert.True(created);
        Assert.Equal("asset_prefab", payload.AssetId);
        Assert.Equal("prefabs/rock.prefab", payload.Path);
        Assert.Equal(AssetBrowserItemKind.Prefab, payload.Kind);
        Assert.False(legacyCreated);
        Assert.Equal(default, legacyPayload);
        Assert.Contains("stable asset id", panel.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Project Window 只会把 script 资产交给外部编辑器回调，并把失败诊断写回状态。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelOpensOnlyScriptAssetsThroughCallbackAndStoresDiagnostics()
    {
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("scripts/PlayerController.cs", AssetBrowserItemKind.Script, 10, DateTimeOffset.UnixEpoch, null, "asset_script"),
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_texture"),
        ]);
        List<string> opened = [];
        bool OpenScriptAsset(string path, out string diagnostic)
        {
            opened.Add(path);
            diagnostic = $"opened {path}";
            return true;
        }

        AssetBrowserPanel panel = new(source, openScriptAsset: OpenScriptAsset);

        _ = panel.Refresh();
        bool openedScript = panel.TryOpenScriptAsset("scripts/PlayerController.cs");
        bool openedTexture = panel.TryOpenScriptAsset("textures/sand.png");

        Assert.True(openedScript);
        Assert.False(openedTexture);
        Assert.Equal(["scripts/PlayerController.cs"], opened);
        Assert.Contains("仅 script", panel.Status, StringComparison.Ordinal);

        static bool FailOpenScriptAsset(string path, out string diagnostic)
        {
            diagnostic = $"failed {path}";
            return false;
        }

        AssetBrowserPanel failingPanel = new(source, openScriptAsset: FailOpenScriptAsset);
        _ = failingPanel.Refresh();

        bool failed = failingPanel.TryOpenScriptAsset("scripts/PlayerController.cs");

        Assert.False(failed);
        Assert.Equal("failed scripts/PlayerController.cs", failingPanel.Status);
    }

    /// <summary>
    /// 验证 Project Window 删除动作必须先经确认回调，且缺 stable id 的旧数据源项不能删除。
    /// </summary>
    [Fact]
    public void AssetBrowserPanelDeletesOnlyStableAssetsAfterConfirmation()
    {
        RecordingAssetSource source = new(
        [
            new AssetBrowserItem("textures/sand.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null, "asset_texture"),
            new AssetBrowserItem("textures/legacy.png", AssetBrowserItemKind.Texture, 20, DateTimeOffset.UnixEpoch, null),
        ]);
        List<AssetBrowserDeleteRequest> requests = [];
        AssetBrowserDeleteResult DeleteAsset(AssetBrowserDeleteRequest request)
        {
            requests.Add(request);
            return request.Confirmed
                ? new AssetBrowserDeleteResult(true, false, $"deleted {request.Path}")
                : new AssetBrowserDeleteResult(false, true, $"confirm {request.Path}");
        }

        AssetBrowserPanel panel = new(source, deleteAsset: DeleteAsset);

        _ = panel.Refresh();
        bool requested = panel.TryRequestDeleteAsset("textures/sand.png");
        bool confirmed = panel.TryConfirmDeleteAsset("textures/sand.png");
        bool legacy = panel.TryRequestDeleteAsset("textures/legacy.png");

        Assert.False(requested);
        Assert.True(confirmed);
        Assert.False(legacy);
        Assert.Equal(2, requests.Count);
        Assert.False(requests[0].Confirmed);
        Assert.True(requests[1].Confirmed);
        Assert.Equal("asset_texture", requests[1].AssetId);
        Assert.Equal(AssetBrowserItemKind.Texture, requests[1].Kind);
        Assert.Contains("stable asset id", panel.Status, StringComparison.Ordinal);
    }

    private sealed class RecordingThumbnailProvider : ITextureThumbnailProvider
    {
        public bool TryGetThumbnail(string assetPath, out AssetThumbnail thumbnail)
        {
            if (assetPath == "textures/sand.png")
            {
                thumbnail = new AssetThumbnail(12, 16, 16);
                return true;
            }

            thumbnail = default;
            return false;
        }
    }

    private sealed class RecordingAssetSource(IReadOnlyList<AssetBrowserItem> assets) : IAssetBrowserDataSource
    {
        public IReadOnlyList<AssetBrowserItem> ListAssets()
        {
            return assets;
        }
    }

    private sealed class RecordingAudioPreview : IAudioPreviewService
    {
        public List<string> Played { get; } = [];

        public bool TryPlayPreview(string assetPath)
        {
            Played.Add(assetPath);
            return true;
        }
    }
}
