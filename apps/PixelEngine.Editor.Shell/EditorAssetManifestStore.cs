using System.Globalization;
using System.Text.Json;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal enum EditorAssetType
{
    Material,
    Texture,
    Audio,
    Scene,
    Prefab,
    Script,
    Json,
    Other,
}

internal readonly record struct EditorAssetRecord(
    string Id,
    string LogicalPath,
    EditorAssetType AssetType,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);

internal readonly record struct EditorAssetMoveResult(
    EditorAssetRecord Asset,
    int UpdatedReferenceDocuments,
    bool UpdatedActiveScene);

internal sealed class EditorAssetManifestStore
{
    public const int CurrentFormatVersion = 1;
    public const string ManifestRelativePath = ".pixelengine/assets.json";

    private readonly string _projectRoot;
    private readonly string _contentRoot;
    private readonly string _manifestPath;

    public EditorAssetManifestStore(EditorProject project)
        : this(
            project?.ProjectRoot ?? throw new ArgumentNullException(nameof(project)),
            project.ContentRootPath)
    {
    }

    public EditorAssetManifestStore(string projectRoot, string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
        _contentRoot = Path.IsPathRooted(contentRoot)
            ? Path.GetFullPath(contentRoot)
            : Path.GetFullPath(Path.Combine(_projectRoot, contentRoot));
        _manifestPath = Path.Combine(_projectRoot, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string ProjectRoot => _projectRoot;

    public string ContentRoot => _contentRoot;

    public string ManifestPath => _manifestPath;

    public IReadOnlyList<EditorAssetRecord> Refresh()
    {
        return RefreshCore();
    }

    public bool TryResolveAssetId(string? assetId, out EditorAssetRecord record)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            record = default;
            return false;
        }

        IReadOnlyList<EditorAssetRecord> records = Refresh();
        for (int i = 0; i < records.Count; i++)
        {
            if (string.Equals(records[i].Id, assetId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                record = records[i];
                return true;
            }
        }

        record = default;
        return false;
    }

    public bool TryResolveLogicalPath(string logicalPath, out EditorAssetRecord record)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        IReadOnlyList<EditorAssetRecord> records = Refresh();
        for (int i = 0; i < records.Count; i++)
        {
            if (string.Equals(records[i].LogicalPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                record = records[i];
                return true;
            }
        }

        record = default;
        return false;
    }

    public EditorAssetRecord EnsureAsset(string logicalPath)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        string fullPath = ResolveFullPath(normalized);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("资产文件不存在，无法登记到 manifest。", fullPath);
        }

        if (TryResolveLogicalPath(normalized, out EditorAssetRecord record))
        {
            return record;
        }

        throw new InvalidOperationException($"无法登记资产：{normalized}");
    }

    public EditorAssetRecord CreateAsset(string logicalPath, EditorAssetType assetType, string? textContents = null)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        EditorAssetType classified = Classify(normalized);
        if (assetType != EditorAssetType.Other && classified != assetType)
        {
            throw new InvalidOperationException($"资产路径 {normalized} 的类型 {classified} 与请求类型 {assetType} 不一致。");
        }

        string fullPath = ResolveFullPath(normalized);
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException($"资产已存在：{normalized}");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        WriteDefaultAsset(fullPath, normalized, assetType, textContents);
        return EnsureAsset(normalized);
    }

    public EditorAssetMoveResult MoveAsset(string currentLogicalPath, string newLogicalPath, EditorSceneModel? activeScene = null)
    {
        string current = NormalizeLogicalPath(currentLogicalPath, nameof(currentLogicalPath));
        string next = NormalizeLogicalPath(newLogicalPath, nameof(newLogicalPath));
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("资产移动目标与源路径相同。");
        }

        EditorAssetRecord source = EnsureAsset(current);
        EditorAssetType nextType = Classify(next);
        if (nextType != source.AssetType)
        {
            throw new InvalidOperationException($"资产移动不能改变类型：{source.AssetType} -> {nextType}。");
        }

        string sourceFullPath = ResolveFullPath(current);
        string targetFullPath = ResolveFullPath(next);
        if (File.Exists(targetFullPath))
        {
            throw new InvalidOperationException($"目标资产已存在：{next}");
        }

        string? targetDirectory = Path.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            _ = Directory.CreateDirectory(targetDirectory);
        }

        File.Move(sourceFullPath, targetFullPath);
        SaveDocument(ReplaceRecordLogicalPath(LoadDocument(), source.Id, next));

        bool updatedActiveScene = activeScene is not null && RewritePrefabLinks(activeScene, current, next, source.Id);
        int updatedDocuments = RewritePrefabLinksInReferenceDocuments(current, next, source.Id);
        EditorAssetRecord moved = EnsureAsset(next);
        return new EditorAssetMoveResult(moved, updatedDocuments, updatedActiveScene);
    }

    internal static EditorAssetType Classify(string logicalPath)
    {
        string fileName = Path.GetFileName(logicalPath);
        string extension = Path.GetExtension(logicalPath).ToLowerInvariant();
        return string.Equals(fileName, "materials.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "reactions.json", StringComparison.OrdinalIgnoreCase)
            ? EditorAssetType.Material
            : extension switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".webp" => EditorAssetType.Texture,
                ".wav" or ".ogg" or ".flac" or ".mp3" => EditorAssetType.Audio,
                ".scene" or ".world" => EditorAssetType.Scene,
                ".prefab" => EditorAssetType.Prefab,
                ".cs" => EditorAssetType.Script,
                ".json" => EditorAssetType.Json,
                _ => EditorAssetType.Other,
            };
    }

    private IReadOnlyList<EditorAssetRecord> RefreshCore()
    {
        EditorAssetManifestDocument document = LoadDocument();
        Dictionary<string, EditorAssetRecordDocument> byPath = BuildRecordMap(document.Assets);
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        List<EditorAssetRecordDocument> refreshed = [];
        if (Directory.Exists(_contentRoot))
        {
            foreach (string fullPath in Directory.EnumerateFiles(_contentRoot, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                string logicalPath = NormalizeLogicalPath(Path.GetRelativePath(_contentRoot, fullPath), "content file");
                FileInfo info = new(fullPath);
                string id = byPath.TryGetValue(logicalPath, out EditorAssetRecordDocument? existing) &&
                    !string.IsNullOrWhiteSpace(existing.Id) &&
                    usedIds.Add(existing.Id)
                    ? existing.Id
                    : AllocateAssetId(usedIds);
                refreshed.Add(new EditorAssetRecordDocument
                {
                    Id = id,
                    LogicalPath = logicalPath,
                    AssetType = Classify(logicalPath),
                    SizeBytes = info.Length,
                    LastModifiedUtc = info.LastWriteTimeUtc,
                });
            }
        }

        EditorAssetManifestDocument normalized = new()
        {
            FormatVersion = CurrentFormatVersion,
            Assets = [.. refreshed.OrderBy(static item => item.LogicalPath, StringComparer.OrdinalIgnoreCase)],
        };
        SaveDocument(normalized);
        return [.. normalized.Assets.Select(static item => new EditorAssetRecord(
            item.Id,
            item.LogicalPath,
            item.AssetType,
            item.SizeBytes,
            item.LastModifiedUtc))];
    }

    private string ResolveFullPath(string logicalPath)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        string fullPath = Path.GetFullPath(Path.Combine(_contentRoot, normalized));
        string rootWithSeparator = _contentRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _contentRoot
            : _contentRoot + Path.DirectorySeparatorChar;
        bool insideContentRoot = string.Equals(fullPath, _contentRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        return insideContentRoot
            ? fullPath
            : throw new InvalidOperationException($"资产路径越过 content 根目录：{logicalPath}");
    }

    private EditorAssetManifestDocument LoadDocument()
    {
        if (!File.Exists(_manifestPath))
        {
            return new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = [] };
        }

        string json = File.ReadAllText(_manifestPath);
        EditorAssetManifestDocument document = JsonSerializer.Deserialize(
                json,
                EditorShellJsonContext.Default.EditorAssetManifestDocument) ??
            throw new JsonException("资产 manifest 为空或格式无效。");
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException($"不支持的资产 manifest 版本：{document.FormatVersion}。");
        }

        return new EditorAssetManifestDocument
        {
            FormatVersion = CurrentFormatVersion,
            Assets = NormalizeRecords(document.Assets),
        };
    }

    private void SaveDocument(EditorAssetManifestDocument document)
    {
        string? directory = Path.GetDirectoryName(_manifestPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        EditorAssetManifestDocument normalized = new()
        {
            FormatVersion = CurrentFormatVersion,
            Assets = NormalizeRecords(document.Assets),
        };
        string json = JsonSerializer.Serialize(
            normalized,
            EditorShellJsonContext.Default.EditorAssetManifestDocument);
        File.WriteAllText(_manifestPath, json);
    }

    private static EditorAssetRecordDocument[] NormalizeRecords(EditorAssetRecordDocument[]? records)
    {
        if (records is null || records.Length == 0)
        {
            return [];
        }

        Dictionary<string, EditorAssetRecordDocument> unique = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < records.Length; i++)
        {
            EditorAssetRecordDocument record = records[i];
            if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.LogicalPath))
            {
                continue;
            }

            string logicalPath = NormalizeLogicalPath(record.LogicalPath, nameof(record.LogicalPath));
            unique[logicalPath] = new EditorAssetRecordDocument
            {
                Id = record.Id.Trim(),
                LogicalPath = logicalPath,
                AssetType = Classify(logicalPath),
                SizeBytes = Math.Max(0, record.SizeBytes),
                LastModifiedUtc = record.LastModifiedUtc,
            };
        }

        return [.. unique.Values.OrderBy(static item => item.LogicalPath, StringComparer.OrdinalIgnoreCase)];
    }

    private static Dictionary<string, EditorAssetRecordDocument> BuildRecordMap(EditorAssetRecordDocument[]? records)
    {
        Dictionary<string, EditorAssetRecordDocument> byPath = new(StringComparer.OrdinalIgnoreCase);
        EditorAssetRecordDocument[] normalized = NormalizeRecords(records);
        for (int i = 0; i < normalized.Length; i++)
        {
            byPath[normalized[i].LogicalPath] = normalized[i];
        }

        return byPath;
    }

    private static EditorAssetManifestDocument ReplaceRecordLogicalPath(EditorAssetManifestDocument document, string assetId, string newLogicalPath)
    {
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        bool replaced = false;
        for (int i = 0; i < records.Length; i++)
        {
            if (!string.Equals(records[i].Id, assetId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            records[i] = new EditorAssetRecordDocument
            {
                Id = records[i].Id,
                LogicalPath = newLogicalPath,
                AssetType = Classify(newLogicalPath),
                SizeBytes = records[i].SizeBytes,
                LastModifiedUtc = records[i].LastModifiedUtc,
            };
            replaced = true;
            break;
        }

        if (!replaced)
        {
            throw new InvalidOperationException($"资产 manifest 缺少 asset id：{assetId}");
        }

        return new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = records };
    }

    private int RewritePrefabLinksInReferenceDocuments(string oldPath, string newPath, string assetId)
    {
        if (!Directory.Exists(_contentRoot))
        {
            return 0;
        }

        int updated = 0;
        string[] files =
        [
            .. Directory.EnumerateFiles(_contentRoot, "*.scene", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(_contentRoot, "*.prefab", SearchOption.AllDirectories))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        for (int i = 0; i < files.Length; i++)
        {
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(files[i]);
            EditorSceneModel model = EditorSceneModel.FromDocument(document);
            if (!RewritePrefabLinks(model, oldPath, newPath, assetId))
            {
                continue;
            }

            EngineSceneDocumentLoader.SaveDocument(model.ToDocument(), files[i]);
            updated++;
        }

        return updated;
    }

    private static bool RewritePrefabLinks(EditorSceneModel scene, string oldPath, string newPath, string assetId)
    {
        bool changed = false;
        EditorGameObject[] objects = [.. scene.EnumerateDepthFirst()];
        for (int i = 0; i < objects.Length; i++)
        {
            EditorPrefabLink? link = objects[i].PrefabLink;
            if (link is null || !MatchesPrefabReference(link, oldPath, assetId))
            {
                continue;
            }

            EditorPrefabLink updated = link.Clone();
            updated.AssetId = assetId;
            updated.AssetPath = newPath;
            scene.SetPrefabLink(objects[i].StableId, updated);
            changed = true;
        }

        return changed;
    }

    private static bool MatchesPrefabReference(EditorPrefabLink link, string oldPath, string assetId)
    {
        if (!string.IsNullOrWhiteSpace(link.AssetId) &&
            string.Equals(link.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(link.AssetPath))
        {
            return false;
        }

        try
        {
            return string.Equals(NormalizeLogicalPath(link.AssetPath, nameof(link.AssetPath)), oldPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void WriteDefaultAsset(string fullPath, string logicalPath, EditorAssetType assetType, string? textContents)
    {
        if (textContents is not null)
        {
            File.WriteAllText(fullPath, textContents);
            return;
        }

        switch (assetType)
        {
            case EditorAssetType.Scene:
                EngineSceneDocumentLoader.SaveDocument(
                    new EngineSceneDocument
                    {
                        FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                        Name = Path.GetFileNameWithoutExtension(logicalPath),
                        Entities = [],
                    },
                    fullPath);
                break;
            case EditorAssetType.Prefab:
                EngineSceneDocumentLoader.SaveDocument(
                    new EngineSceneDocument
                    {
                        FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                        Name = Path.GetFileNameWithoutExtension(logicalPath),
                        Entities =
                        [
                            new EngineSceneEntityDocument
                            {
                                StableId = 1,
                                Name = Path.GetFileNameWithoutExtension(logicalPath),
                                Transform = new EngineSceneTransformDocument(),
                            },
                        ],
                    },
                    fullPath);
                break;
            case EditorAssetType.Script:
                File.WriteAllText(fullPath, CreateScriptTemplate(logicalPath));
                break;
            case EditorAssetType.Material:
            case EditorAssetType.Json:
                File.WriteAllText(fullPath, "{}" + Environment.NewLine);
                break;
            default:
                File.WriteAllText(fullPath, string.Empty);
                break;
        }
    }

    private static string CreateScriptTemplate(string logicalPath)
    {
        string name = Path.GetFileNameWithoutExtension(logicalPath);
        string className = SanitizeIdentifier(string.IsNullOrWhiteSpace(name) ? "NewBehaviour" : name);
        return $$"""
using PixelEngine.Scripting;

public sealed class {{className}} : Behaviour
{
}
""";
    }

    private static string SanitizeIdentifier(string value)
    {
        Span<char> buffer = stackalloc char[value.Length + 1];
        int length = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            bool valid = length == 0
                ? char.IsLetter(ch) || ch == '_'
                : char.IsLetterOrDigit(ch) || ch == '_';
            if (valid)
            {
                buffer[length++] = ch;
            }
        }

        if (length == 0)
        {
            return "NewBehaviour";
        }

        string candidate = new(buffer[..length]);
        if (!char.IsLetter(candidate[0]) && candidate[0] != '_')
        {
            candidate = "Asset" + candidate;
        }

        return candidate;
    }

    private static string AllocateAssetId(HashSet<string> usedIds)
    {
        for (int i = 0; i < 10; i++)
        {
            string id = "asset_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            if (usedIds.Add(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException("无法生成唯一 asset id。");
    }

    private static string NormalizeLogicalPath(string value, string fieldName)
    {
        string candidate = string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{fieldName} 不能为空。")
            : value.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(candidate) || candidate.StartsWith('/'))
        {
            throw new InvalidOperationException($"{fieldName} 必须是 content 内相对路径：{candidate}");
        }

        string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> normalized = new(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                throw new InvalidOperationException($"{fieldName} 不能越过 content 根目录：{candidate}");
            }

            normalized.Add(part);
        }

        if (normalized.Count == 0)
        {
            throw new InvalidOperationException($"{fieldName} 不能解析为空路径。");
        }

        return string.Join('/', normalized);
    }
}

internal sealed class EditorAssetManifestDocument
{
    public int FormatVersion { get; init; } = EditorAssetManifestStore.CurrentFormatVersion;

    public EditorAssetRecordDocument[]? Assets { get; init; }
}

internal sealed class EditorAssetRecordDocument
{
    public string Id { get; init; } = string.Empty;

    public string LogicalPath { get; init; } = string.Empty;

    public EditorAssetType AssetType { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset LastModifiedUtc { get; init; }
}
