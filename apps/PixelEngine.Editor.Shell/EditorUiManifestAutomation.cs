using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Shell;

internal static class EditorUiManifestAutomation
{
    private const long MaximumManifestBytes = 2L * 1024 * 1024;
    private const int MaximumScreens = 4096;

    internal static AutomationUiManifestSnapshot Capture(
        string contentRoot,
        IReadOnlyList<AssetBrowserItem> assets,
        string diagnostic = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(assets);
        string root = Path.GetFullPath(contentRoot);
        string uiRoot = Path.Combine(root, "ui");
        string manifestPath = Path.Combine(uiRoot, "ui-manifest.json");
        EnsurePathChainNoReparse(root, manifestPath);
        EditorUiManifestDocument document = ReadManifest(manifestPath);
        EditorUiManifestScreenDocument[] source = document.Screens ?? [];
        if (source.Length > MaximumScreens)
        {
            throw new InvalidDataException(
                $"UI manifest screen 数超过 {MaximumScreens} 上限。");
        }

        Dictionary<string, string> assetIds = BuildAssetIdMap(assets);
        HashSet<string> screenIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> screenPaths = new(StringComparer.OrdinalIgnoreCase);
        AutomationUiManifestScreen[] screens = new AutomationUiManifestScreen[source.Length];
        int missing = 0;
        int unregistered = 0;
        for (int i = 0; i < source.Length; i++)
        {
            EditorUiManifestScreenDocument screen = source[i] ??
                throw new InvalidDataException($"UI manifest screen[{i}] 为空。");
            string screenId = NormalizeScreenId(screen.Id, i);
            string path = NormalizeScreenPath(screen.Path, i);
            if (!screenIds.Add(screenId))
            {
                throw new InvalidDataException($"UI manifest screen ID 重复：{screenId}。");
            }

            if (!screenPaths.Add(path))
            {
                throw new InvalidDataException($"UI manifest screen path 重复：{path}。");
            }

            string fullPath = ResolveUnderRoot(uiRoot, path);
            EnsurePathChainNoReparse(uiRoot, fullPath);
            bool exists = File.Exists(fullPath);
            string logicalPath = "Content/ui/" + path;
            _ = assetIds.TryGetValue(logicalPath, out string? assetId);
            missing += exists ? 0 : 1;
            unregistered += string.IsNullOrWhiteSpace(assetId) ? 1 : 0;
            screens[i] = new AutomationUiManifestScreen
            {
                ScreenId = screenId,
                Path = path,
                Preload = screen.Preload,
                FileExists = exists,
                AssetId = assetId,
                LogicalPath = logicalPath,
            };
        }

        Array.Sort(screens, static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.ScreenId, right.ScreenId));
        return new AutomationUiManifestSnapshot
        {
            Screens = screens,
            MissingFileCount = missing,
            UnregisteredAssetCount = unregistered,
            Diagnostic = diagnostic,
        };
    }

    private static EditorUiManifestDocument ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            return new EditorUiManifestDocument { Screens = [], Images = [] };
        }

        FileInfo before = new(path);
        if (before.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                $"UI manifest 超过 {MaximumManifestBytes} 字节上限。");
        }

        byte[] bytes = File.ReadAllBytes(path);
        FileInfo after = new(path);
        if (!after.Exists || after.Length != before.Length ||
            after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            throw new IOException("读取 UI manifest 时文件发生变化，请重试。");
        }

        try
        {
            return JsonSerializer.Deserialize(
                    bytes,
                    EditorShellJsonContext.Default.EditorUiManifestDocument) ??
                throw new InvalidDataException("UI manifest 为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("UI manifest JSON 无效。", exception);
        }
    }

    private static Dictionary<string, string> BuildAssetIdMap(IReadOnlyList<AssetBrowserItem> assets)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assets.Count; i++)
        {
            AssetBrowserItem asset = assets[i];
            if (string.IsNullOrWhiteSpace(asset.AssetId) || string.IsNullOrWhiteSpace(asset.Path))
            {
                continue;
            }

            result[asset.Path.Replace('\\', '/')] = asset.AssetId;
        }

        return result;
    }

    private static string NormalizeScreenId(string value, int index)
    {
        string id = value?.Trim() ?? string.Empty;
        return id.Length is < 1 or > 128 || id.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')
            ? throw new InvalidDataException($"UI manifest screen[{index}] ID 无效。")
            : id;
    }

    private static string NormalizeScreenPath(string value, int index)
    {
        string path = value?.Trim().Replace('\\', '/') ?? string.Empty;
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return path.Length is < 1 or > 2048 || Path.IsPathRooted(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            segments.Length == 0 ||
            segments.Any(static segment => segment is "." or ".." || segment.Contains(':'))
            ? throw new InvalidDataException($"UI manifest screen[{index}] path 无效或越界。")
            : string.Join('/', segments);
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine(
            canonicalRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = canonicalRoot + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : throw new InvalidDataException("UI manifest screen path 越过 content/ui root。");
    }

    private static void EnsurePathChainNoReparse(string root, string path)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string canonicalPath = Path.GetFullPath(path);
        string prefix = canonicalRoot + Path.DirectorySeparatorChar;
        if (!string.Equals(canonicalPath, canonicalRoot, StringComparison.OrdinalIgnoreCase) &&
            !canonicalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("UI manifest 路径越过获准 root。");
        }

        string current = canonicalRoot;
        string relative = Path.GetRelativePath(canonicalRoot, canonicalPath);
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = -1; i < segments.Length; i++)
        {
            if (i >= 0)
            {
                current = Path.Combine(current, segments[i]);
            }

            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"UI manifest 路径包含 reparse point：{current}");
            }
        }
    }
}
