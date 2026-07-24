namespace PixelEngine.Editor.Shell;

/// <summary>
/// 用户级命名 dock layout 存储；每个 profile 是经过校验的 Dear ImGui ini 文本。
/// </summary>
internal sealed class EditorLayoutProfileStore
{
    private const int MaximumNameLength = 64;
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public EditorLayoutProfileStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        DirectoryPath = Path.GetFullPath(directory);
    }

    public string DirectoryPath { get; }

    public bool TryList(out string[] names, out string diagnostic)
    {
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                names = [];
                diagnostic = string.Empty;
                return true;
            }

            names = [.. Directory.EnumerateFiles(DirectoryPath, "*.ini", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name!)
                .Order(StringComparer.OrdinalIgnoreCase)];
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            names = [];
            diagnostic = $"读取布局列表失败：{exception.Message}";
            return false;
        }
    }

    public bool TrySave(string name, string layout, out string normalizedName, out string diagnostic)
    {
        if (!TryNormalizeName(name, out normalizedName, out diagnostic) ||
            !EditorDockLayoutValidator.TryValidate(layout, out string normalizedLayout, out diagnostic))
        {
            return false;
        }

        try
        {
            _ = Directory.CreateDirectory(DirectoryPath);
            EditorAtomicTextFile.WriteAllText(ProfilePath(normalizedName), normalizedLayout);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostic = $"保存布局 '{normalizedName}' 失败：{exception.Message}";
            return false;
        }
    }

    public bool TryLoad(string name, out string normalizedLayout, out string diagnostic)
    {
        normalizedLayout = string.Empty;
        if (!TryNormalizeName(name, out string normalizedName, out diagnostic))
        {
            return false;
        }

        try
        {
            string path = ProfilePath(normalizedName);
            if (!File.Exists(path))
            {
                diagnostic = $"布局 '{normalizedName}' 不存在。";
                return false;
            }

            return EditorDockLayoutValidator.TryValidate(
                File.ReadAllText(path),
                out normalizedLayout,
                out diagnostic);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostic = $"读取布局 '{normalizedName}' 失败：{exception.Message}";
            return false;
        }
    }

    public bool TryDelete(string name, out string diagnostic)
    {
        if (!TryNormalizeName(name, out string normalizedName, out diagnostic))
        {
            return false;
        }

        try
        {
            File.Delete(ProfilePath(normalizedName));
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostic = $"删除布局 '{normalizedName}' 失败：{exception.Message}";
            return false;
        }
    }

    internal static bool TryNormalizeName(string name, out string normalized, out string diagnostic)
    {
        normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > MaximumNameLength ||
            normalized is "." or ".." ||
            normalized.EndsWith('.') ||
            normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.Contains('/') ||
            normalized.Contains('\\') ||
            ReservedWindowsNames.Contains(normalized))
        {
            diagnostic = $"布局名称必须为 1-{MaximumNameLength} 个字符，且不能包含路径或系统保留名称。";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private string ProfilePath(string normalizedName)
    {
        return Path.Combine(DirectoryPath, $"{normalizedName}.ini");
    }
}
