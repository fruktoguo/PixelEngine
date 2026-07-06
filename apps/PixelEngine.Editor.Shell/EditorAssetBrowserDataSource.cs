using PixelEngine.Editor;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorAssetBrowserDataSource(
    EditorAssetManifestStore assets,
    ITextureThumbnailProvider? thumbnailProvider = null) : IAssetBrowserDataSource
{
    private readonly EditorAssetManifestStore _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    private readonly ITextureThumbnailProvider? _thumbnailProvider = thumbnailProvider;

    public IReadOnlyList<AssetBrowserItem> ListAssets()
    {
        IReadOnlyList<EditorAssetRecord> records = _assets.Refresh();
        AssetBrowserItem[] items = new AssetBrowserItem[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            EditorAssetRecord record = records[i];
            AssetThumbnail? thumbnail = TryResolveThumbnail(record.LogicalPath, out AssetThumbnail resolved) ? resolved : null;
            items[i] = new AssetBrowserItem(
                record.LogicalPath,
                MapKind(record.AssetType),
                record.SizeBytes,
                record.LastModifiedUtc,
                thumbnail,
                record.Id);
        }

        return items;
    }

    private bool TryResolveThumbnail(string logicalPath, out AssetThumbnail thumbnail)
    {
        thumbnail = default;
        return _thumbnailProvider is not null && _thumbnailProvider.TryGetThumbnail(logicalPath, out thumbnail);
    }

    private static AssetBrowserItemKind MapKind(EditorAssetType type)
    {
        return type switch
        {
            EditorAssetType.Material => AssetBrowserItemKind.Material,
            EditorAssetType.Texture => AssetBrowserItemKind.Texture,
            EditorAssetType.Audio => AssetBrowserItemKind.Audio,
            EditorAssetType.Scene => AssetBrowserItemKind.Scene,
            EditorAssetType.Prefab => AssetBrowserItemKind.Prefab,
            EditorAssetType.Script => AssetBrowserItemKind.Script,
            EditorAssetType.Json => AssetBrowserItemKind.Json,
            _ => AssetBrowserItemKind.Other,
        };
    }
}
