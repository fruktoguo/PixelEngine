namespace PixelEngine.Editor.Shell;

internal enum EditorAssetFileTraversalSelection
{
    AllFiles,
    ReferenceDocuments,
}

internal static class EditorAssetFileTraversal
{
    private const int MaximumVisitedDirectories = 32768;
    private const int MaximumVisitedEntries = 262144;

    internal static string[] EnumerateFiles(
        string rootPath,
        EditorAssetFileTraversalSelection selection,
        int maximumSelectedFiles,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (maximumSelectedFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSelectedFiles),
                maximumSelectedFiles,
                "文件上限必须大于零。");
        }

        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        RejectReparsePoint(root, operation, "root directory");
        List<string> files = [];
        Stack<string> pending = new();
        pending.Push(root);
        int visitedDirectories = 0;
        int visitedEntries = 0;
        while (pending.TryPop(out string? directory))
        {
            if (++visitedDirectories > MaximumVisitedDirectories)
            {
                throw new InvalidOperationException(
                    $"{operation} 访问目录数超过 {MaximumVisitedDirectories} 上限。");
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++visitedEntries > MaximumVisitedEntries)
                {
                    throw new InvalidOperationException(
                        $"{operation} 访问文件系统条目数超过 {MaximumVisitedEntries} 上限。");
                }

                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (string.Equals(
                            Path.GetFileName(entry),
                            ".pixelengine",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    RejectReparsePoint(entry, attributes, operation, "directory");
                    pending.Push(Path.GetFullPath(entry));
                    continue;
                }

                if (!MatchesSelection(entry, selection))
                {
                    continue;
                }

                RejectReparsePoint(entry, attributes, operation, "file");
                if (files.Count >= maximumSelectedFiles)
                {
                    throw new InvalidOperationException(
                        $"{operation} 匹配文件数超过 {maximumSelectedFiles} 上限。");
                }

                files.Add(Path.GetFullPath(entry));
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return [.. files];
    }

    internal static string[] EnumerateDirectories(
        string rootPath,
        int maximumSelectedDirectories,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (maximumSelectedDirectories <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSelectedDirectories),
                maximumSelectedDirectories,
                "目录上限必须大于零。");
        }

        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        RejectReparsePoint(root, operation, "root directory");
        List<string> directories = [];
        Stack<string> pending = new();
        pending.Push(root);
        int visitedDirectories = 0;
        int visitedEntries = 0;
        while (pending.TryPop(out string? directory))
        {
            if (++visitedDirectories > MaximumVisitedDirectories)
            {
                throw new InvalidOperationException(
                    $"{operation} 访问目录数超过 {MaximumVisitedDirectories} 上限。");
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++visitedEntries > MaximumVisitedEntries)
                {
                    throw new InvalidOperationException(
                        $"{operation} 访问文件系统条目数超过 {MaximumVisitedEntries} 上限。");
                }

                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    RejectReparsePoint(entry, attributes, operation, "file");
                    continue;
                }

                if (string.Equals(
                    Path.GetFileName(entry),
                    ".pixelengine",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                RejectReparsePoint(entry, attributes, operation, "directory");
                if (directories.Count >= maximumSelectedDirectories)
                {
                    throw new InvalidOperationException(
                        $"{operation} 匹配目录数超过 {maximumSelectedDirectories} 上限。");
                }

                string fullPath = Path.GetFullPath(entry);
                directories.Add(fullPath);
                pending.Push(fullPath);
            }
        }

        directories.Sort(StringComparer.OrdinalIgnoreCase);
        return [.. directories];
    }

    private static bool MatchesSelection(
        string path,
        EditorAssetFileTraversalSelection selection)
    {
        if (selection == EditorAssetFileTraversalSelection.AllFiles)
        {
            return true;
        }

        string extension = Path.GetExtension(path);
        return extension.Equals(".scene", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectReparsePoint(string path, string operation, string kind)
    {
        RejectReparsePoint(path, File.GetAttributes(path), operation, kind);
    }

    private static void RejectReparsePoint(
        string path,
        FileAttributes attributes,
        string operation,
        string kind)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"{operation} 拒绝 reparse point {kind}：{path}");
        }
    }
}
