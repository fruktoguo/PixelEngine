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
