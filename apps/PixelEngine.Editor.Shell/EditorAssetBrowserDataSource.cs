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

    public AssetBrowserDeleteResult DeleteAsset(AssetBrowserDeleteRequest request, EditorSceneModel? activeScene = null)
    {
        if (string.IsNullOrWhiteSpace(request.AssetId) || string.IsNullOrWhiteSpace(request.Path))
        {
            return new AssetBrowserDeleteResult(false, false, "删除请求缺少 stable asset id 或 logical path。");
        }

        if (!_assets.TryResolveAssetId(request.AssetId, out EditorAssetRecord record))
        {
            return new AssetBrowserDeleteResult(false, false, $"资产 manifest 缺少 stable asset id：{request.AssetId}");
        }

        EditorAssetType requestType = MapKind(request.Kind);
        if (!string.Equals(record.LogicalPath, request.Path, StringComparison.OrdinalIgnoreCase) || record.AssetType != requestType)
        {
            return new AssetBrowserDeleteResult(false, false, $"删除请求与 manifest 不一致：{request.Path} / {request.Kind}。");
        }

        EditorAssetDeleteResult result = _assets.DeleteAsset(record.LogicalPath, activeScene, request.Confirmed);
        return new AssetBrowserDeleteResult(result.Deleted, result.RequiresConfirmation, result.Diagnostic);
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
            EditorAssetType.Other => AssetBrowserItemKind.Other,
            _ => AssetBrowserItemKind.Other,
        };
    }

    private static EditorAssetType MapKind(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Material => EditorAssetType.Material,
            AssetBrowserItemKind.Texture => EditorAssetType.Texture,
            AssetBrowserItemKind.Audio => EditorAssetType.Audio,
            AssetBrowserItemKind.Scene => EditorAssetType.Scene,
            AssetBrowserItemKind.Prefab => EditorAssetType.Prefab,
            AssetBrowserItemKind.Script => EditorAssetType.Script,
            AssetBrowserItemKind.Json => EditorAssetType.Json,
            AssetBrowserItemKind.Other => EditorAssetType.Other,
            _ => EditorAssetType.Other,
        };
    }
}
