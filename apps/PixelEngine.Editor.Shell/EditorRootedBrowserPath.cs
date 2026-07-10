namespace PixelEngine.Editor.Shell;

/// <summary>
/// Project Window 双根路径的统一解析、格式化与物理路径映射。
/// </summary>
internal static class EditorRootedBrowserPath
{
    public const string ContentRootName = "Content";
    public const string ScriptSourceRootName = "ScriptSource";

    /// <summary>
    /// 创建一个规范化 rooted browser path。
    /// </summary>
    public static EditorAssetPath Create(EditorAssetRootKind root, string relativePath)
    {
        ValidateRoot(root);
        return new EditorAssetPath(root, NormalizeRelativePath(relativePath));
    }

    /// <summary>
    /// 把 rooted browser path 格式化为稳定的 Root/relative/path 文本。
    /// </summary>
    public static string Format(EditorAssetPath path)
    {
        EditorAssetPath normalized = Create(path.Root, path.RelativePath);
        return GetRootName(normalized.Root) + "/" + normalized.RelativePath;
    }

    /// <summary>
    /// 尝试解析 Content/... 或 ScriptSource/... 路径。
    /// </summary>
    public static bool TryParse(string? value, out EditorAssetPath path, out string diagnostic)
    {
        path = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostic = "Project Window rooted path 不能为空。";
            return false;
        }

        string candidate = value.Replace('\\', '/');
        int separator = candidate.IndexOf('/');
        if (separator <= 0 || separator == candidate.Length - 1)
        {
            diagnostic = $"Project Window 路径必须使用 Content/<path> 或 ScriptSource/<path>：{value}";
            return false;
        }

        string rootName = candidate[..separator];
        EditorAssetRootKind root;
        if (string.Equals(rootName, ContentRootName, StringComparison.OrdinalIgnoreCase))
        {
            root = EditorAssetRootKind.Content;
        }
        else if (string.Equals(rootName, ScriptSourceRootName, StringComparison.OrdinalIgnoreCase))
        {
            root = EditorAssetRootKind.ScriptSource;
        }
        else
        {
            diagnostic = $"未知 Project Window logical root：{rootName}。仅支持 {ContentRootName} 与 {ScriptSourceRootName}。";
            return false;
        }

        try
        {
            path = Create(root, candidate[(separator + 1)..]);
            diagnostic = string.Empty;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            diagnostic = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// 将 rooted browser path 映射到 content 或 script source 物理根，并再次验证路径包含关系。
    /// </summary>
    public static string ResolveFullPath(
        EditorAssetPath path,
        string contentRoot,
        string scriptSourceRoot)
    {
        EditorAssetPath normalized = Create(path.Root, path.RelativePath);
        string selectedRoot = normalized.Root switch
        {
            EditorAssetRootKind.Content => contentRoot,
            EditorAssetRootKind.ScriptSource => scriptSourceRoot,
            _ => throw new ArgumentOutOfRangeException(nameof(path), path.Root, "未知 Editor 资产逻辑根。"),
        };
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRoot);
        string physicalRoot = Path.GetFullPath(selectedRoot);
        string fullPath = Path.GetFullPath(Path.Combine(
            physicalRoot,
            normalized.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootWithSeparator = Path.EndsInDirectorySeparator(physicalRoot)
            ? physicalRoot
            : physicalRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(rootWithSeparator, comparison)
            ? fullPath
            : throw new InvalidOperationException(
                $"Project Window 路径越过 {GetRootName(normalized.Root)} 根目录：{Format(normalized)}");
    }

    private static string GetRootName(EditorAssetRootKind root)
    {
        return root switch
        {
            EditorAssetRootKind.Content => ContentRootName,
            EditorAssetRootKind.ScriptSource => ScriptSourceRootName,
            _ => throw new ArgumentOutOfRangeException(nameof(root), root, "未知 Editor 资产逻辑根。"),
        };
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("Project Window 相对路径不能为空。");
        }

        string candidate = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(candidate) || candidate.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Project Window 路径必须位于 logical root 内：{relativePath}");
        }

        string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> normalized = new(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == ".")
            {
                continue;
            }

            if (parts[i] == "..")
            {
                throw new InvalidOperationException($"Project Window 路径不能越过 logical root：{relativePath}");
            }

            normalized.Add(parts[i]);
        }

        return normalized.Count == 0
            ? throw new InvalidOperationException("Project Window 相对路径不能为空。")
            : string.Join('/', normalized);
    }

    private static void ValidateRoot(EditorAssetRootKind root)
    {
        if (!Enum.IsDefined(root))
        {
            throw new ArgumentOutOfRangeException(nameof(root), root, "未知 Editor 资产逻辑根。");
        }
    }
}
