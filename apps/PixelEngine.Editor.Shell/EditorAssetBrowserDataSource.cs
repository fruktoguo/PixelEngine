using System.Globalization;
using System.Text.Json;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 资产浏览器面板的数据源，对接 manifest 与拖放。
/// </summary>
internal sealed class EditorAssetBrowserDataSource : IAssetBrowserDataSource
{
    private readonly EditorAssetManifestStore _assets;
    private readonly ITextureThumbnailProvider? _thumbnailProvider;
    private readonly EditorProjectSceneAssetMoveService? _sceneAssetMoveService;

    public EditorAssetBrowserDataSource(EditorProject project, ITextureThumbnailProvider? thumbnailProvider = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _assets = new EditorAssetManifestStore(project);
        _thumbnailProvider = thumbnailProvider;
        _sceneAssetMoveService = new EditorProjectSceneAssetMoveService(project, _assets);
    }

    public EditorAssetBrowserDataSource(
        EditorAssetManifestStore assets,
        ITextureThumbnailProvider? thumbnailProvider = null,
        EditorProjectSceneAssetMoveService? sceneAssetMoveService = null)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _thumbnailProvider = thumbnailProvider;
        _sceneAssetMoveService = sceneAssetMoveService;
    }

    /// <summary>
    /// 刷新 manifest 并映射为资产浏览器条目（含缩略图与预览摘要）。
    /// </summary>
    public IReadOnlyList<AssetBrowserItem> ListAssets()
    {
        // 资产浏览：manifest 刷新 → 分类映射 → 缩略图解析
        IReadOnlyList<EditorAssetRecord> records = _assets.Refresh();
        AssetBrowserItem[] items = new AssetBrowserItem[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            EditorAssetRecord record = records[i];
            AssetThumbnail? thumbnail = TryResolveThumbnail(record.LogicalPath, out AssetThumbnail resolved) ? resolved : null;
            AssetBrowserItemKind kind = MapKind(record.AssetType);
            items[i] = new AssetBrowserItem(
                record.LogicalPath,
                kind,
                record.SizeBytes,
                record.LastModifiedUtc,
                thumbnail,
                record.Id,
                BuildPreviewSummary(record, kind, thumbnail));
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

    public EditorAssetBrowserMoveResult MoveAsset(string currentPath, string newPath, EditorSceneModel? activeScene = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        EditorAssetRecord current = _assets.EnsureAsset(currentPath);
        EditorAssetType newType = EditorAssetManifestStore.Classify(newPath);
        if (current.AssetType != newType)
        {
            return new EditorAssetBrowserMoveResult(
                false,
                current,
                $"资产移动不能改变类型：{current.LogicalPath} -> {newPath}。");
        }

        if (current.AssetType == EditorAssetType.Scene && _sceneAssetMoveService is not null)
        {
            EditorSceneAssetMoveResult result = _sceneAssetMoveService.MoveSceneAsset(current.LogicalPath, newPath, activeScene);
            return new EditorAssetBrowserMoveResult(
                true,
                result.AssetMove.Asset,
                $"已移动 Scene 资产并同步引用：{result.SettingsUpdates.FormatDiagnostic()}");
        }

        EditorAssetMoveResult move = _assets.MoveAsset(current.LogicalPath, newPath, activeScene);
        return new EditorAssetBrowserMoveResult(
            true,
            move.Asset,
            move.UpdatedActiveScene || move.UpdatedReferenceDocuments > 0
                ? $"已移动资产并重写引用：active={move.UpdatedActiveScene}, documents={move.UpdatedReferenceDocuments.ToString(CultureInfo.InvariantCulture)}"
                : $"已移动资产：{move.Asset.LogicalPath}");
    }

    public AssetBrowserMoveResult MoveAsset(AssetBrowserMoveRequest request, EditorSceneModel? activeScene = null)
    {
        if (string.IsNullOrWhiteSpace(request.AssetId) ||
            string.IsNullOrWhiteSpace(request.Path) ||
            string.IsNullOrWhiteSpace(request.NewPath))
        {
            return new AssetBrowserMoveResult(false, "移动请求缺少 stable asset id、当前路径或目标路径。");
        }

        if (!_assets.TryResolveAssetId(request.AssetId, out EditorAssetRecord record))
        {
            return new AssetBrowserMoveResult(false, $"资产 manifest 缺少 stable asset id：{request.AssetId}");
        }

        EditorAssetType requestType = MapKind(request.Kind);
        if (!string.Equals(record.LogicalPath, request.Path, StringComparison.OrdinalIgnoreCase) || record.AssetType != requestType)
        {
            return new AssetBrowserMoveResult(false, $"移动请求与 manifest 不一致：{request.Path} / {request.Kind}。");
        }

        try
        {
            EditorAssetBrowserMoveResult result = MoveAsset(record.LogicalPath, request.NewPath, activeScene);
            return new AssetBrowserMoveResult(result.Succeeded, result.Diagnostic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AssetBrowserMoveResult(false, ex.Message);
        }
    }

    public AssetBrowserCreateResult CreateAsset(AssetBrowserCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return new AssetBrowserCreateResult(false, "资产创建请求缺少 logical path。");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return new AssetBrowserCreateResult(false, $"未知资产类型：{request.Kind}。");
        }

        EditorAssetType type = MapKind(request.Kind);
        if (!IsCreatableType(type))
        {
            return new AssetBrowserCreateResult(false, $"Project Window 暂不支持直接创建 {request.Kind} 资产。");
        }

        try
        {
            EditorAssetRecord asset = _assets.CreateAsset(request.Path, type);
            return new AssetBrowserCreateResult(
                true,
                $"已创建资产：{asset.LogicalPath}",
                asset.Id,
                asset.LogicalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AssetBrowserCreateResult(false, ex.Message);
        }
    }

    private bool TryResolveThumbnail(string logicalPath, out AssetThumbnail thumbnail)
    {
        thumbnail = default;
        return _thumbnailProvider is not null && _thumbnailProvider.TryGetThumbnail(logicalPath, out thumbnail);
    }

    private string BuildPreviewSummary(EditorAssetRecord record, AssetBrowserItemKind kind, AssetThumbnail? thumbnail)
    {
        return kind switch
        {
            AssetBrowserItemKind.Texture => thumbnail is { } image
                ? $"纹理：{image.Width.ToString(CultureInfo.InvariantCulture)}×{image.Height.ToString(CultureInfo.InvariantCulture)}，{FormatSize(record.SizeBytes)}"
                : $"纹理：{FormatSize(record.SizeBytes)}",
            AssetBrowserItemKind.Audio => $"音频：{FormatSize(record.SizeBytes)}",
            AssetBrowserItemKind.Material => BuildJsonAssetPreview(record, "材质定义"),
            AssetBrowserItemKind.Scene => BuildSceneAssetPreview(record, "场景"),
            AssetBrowserItemKind.Prefab => BuildSceneAssetPreview(record, "Prefab"),
            AssetBrowserItemKind.Script => BuildScriptAssetPreview(record),
            AssetBrowserItemKind.Json => BuildJsonAssetPreview(record, "JSON"),
            AssetBrowserItemKind.Other => $"文件：{FormatSize(record.SizeBytes)}",
            _ => $"文件：{FormatSize(record.SizeBytes)}",
        };
    }

    private string BuildSceneAssetPreview(EditorAssetRecord record, string label)
    {
        try
        {
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(ResolveAssetFullPath(record.LogicalPath));
            EngineSceneEntityDocument[] entities = document.Entities ?? [];
            int rootCount = 0;
            int behaviourCount = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!entities[i].ParentId.HasValue)
                {
                    rootCount++;
                }

                behaviourCount += entities[i].Behaviours?.Length ?? 0;
            }

            return $"{label}：{entities.Length.ToString(CultureInfo.InvariantCulture)} 个 GameObject，{rootCount.ToString(CultureInfo.InvariantCulture)} 个根，{behaviourCount.ToString(CultureInfo.InvariantCulture)} 个 Behaviour";
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            return $"{label}摘要不可用：{FormatSize(record.SizeBytes)}";
        }
    }

    private string BuildJsonAssetPreview(EditorAssetRecord record, string label)
    {
        try
        {
            using FileStream stream = File.OpenRead(ResolveAssetFullPath(record.LogicalPath));
            using JsonDocument document = JsonDocument.Parse(stream);
            string shape = TryCountJsonCollection(document.RootElement, out int count)
                ? $"{count.ToString(CultureInfo.InvariantCulture)} 项"
                : DescribeJsonShape(document.RootElement);
            return $"{label}：{shape}，{FormatSize(record.SizeBytes)}";
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            return $"{label}摘要不可用：{FormatSize(record.SizeBytes)}";
        }
    }

    private string BuildScriptAssetPreview(EditorAssetRecord record)
    {
        try
        {
            string fullPath = ResolveAssetFullPath(record.LogicalPath);
            string? className = TryReadFirstClassName(fullPath);
            return string.IsNullOrWhiteSpace(className)
                ? $"脚本：{FormatSize(record.SizeBytes)}"
                : $"脚本：{className}，{FormatSize(record.SizeBytes)}";
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            return $"脚本摘要不可用：{FormatSize(record.SizeBytes)}";
        }
    }

    private string ResolveAssetFullPath(string logicalPath)
    {
        return Path.GetFullPath(Path.Combine(_assets.ContentRoot, logicalPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool TryCountJsonCollection(JsonElement root, out int count)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            count = root.GetArrayLength();
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetArrayCount(root, "materials", out count) ||
                TryGetArrayCount(root, "reactions", out count) ||
                TryGetArrayCount(root, "items", out count))
            {
                return true;
            }

            count = root.EnumerateObject().Count();
            return count > 0;
        }

        count = 0;
        return false;
    }

    private static bool TryGetArrayCount(JsonElement root, string propertyName, out int count)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
        {
            count = value.GetArrayLength();
            return true;
        }

        count = 0;
        return false;
    }

    private static string DescribeJsonShape(JsonElement root)
    {
        return root.ValueKind switch
        {
            JsonValueKind.Object => "JSON 对象",
            JsonValueKind.Array => "JSON 数组",
            JsonValueKind.String => "JSON 字符串",
            JsonValueKind.Number => "JSON 数字",
            JsonValueKind.True or JsonValueKind.False => "JSON 布尔值",
            JsonValueKind.Null => "JSON null",
            JsonValueKind.Undefined => "JSON 文档",
            _ => "JSON 文档",
        };
    }

    private static string? TryReadFirstClassName(string fullPath)
    {
        foreach (string line in File.ReadLines(fullPath).Take(120))
        {
            string trimmed = line.Trim();
            int classIndex = trimmed.IndexOf("class ", StringComparison.Ordinal);
            if (classIndex < 0)
            {
                continue;
            }

            int start = classIndex + "class ".Length;
            while (start < trimmed.Length && !IsIdentifierStart(trimmed[start]))
            {
                start++;
            }

            int end = start;
            while (end < trimmed.Length && IsIdentifierPart(trimmed[end]))
            {
                end++;
            }

            if (end > start)
            {
                return trimmed[start..end];
            }
        }

        return null;
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static bool IsPreviewFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
        }

        double kib = bytes / 1024d;
        if (kib < 1024d)
        {
            return $"{kib.ToString("0.#", CultureInfo.InvariantCulture)} KiB";
        }

        double mib = kib / 1024d;
        return $"{mib.ToString("0.#", CultureInfo.InvariantCulture)} MiB";
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

    private static bool IsCreatableType(EditorAssetType type)
    {
        return type is EditorAssetType.Scene or EditorAssetType.Prefab or EditorAssetType.Script or EditorAssetType.Json;
    }
}

/// <summary>
/// EditorAssetBrowserMoveResult 数据结构。
/// </summary>
internal sealed record EditorAssetBrowserMoveResult(
    bool Succeeded,
    EditorAssetRecord Asset,
    string Diagnostic);
