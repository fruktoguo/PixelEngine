using System.Globalization;
using System.Text.Json;
using PixelEngine.Hosting;

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
}
