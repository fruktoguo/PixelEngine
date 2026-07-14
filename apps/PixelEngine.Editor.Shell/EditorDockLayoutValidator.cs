using System.Text;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Dear ImGui ini 的有界白名单验证；避免把未受限文本直接交给 native parser。
/// </summary>
internal static class EditorDockLayoutValidator
{
    // v1 默认控制 frame 为 1 MiB；保留 JSON escaping、panel registry、revision 与 envelope
    // 的最坏空间。超过该界限的通用大型数据必须走 artifact，而不是挤占控制面。
    public const int MaximumUtf8Bytes = 256 * 1024;
    private const int MaximumLines = 10_000;
    private const int MaximumLineLength = 8192;

    public static bool TryValidate(string? layout, out string normalized, out string diagnostic)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(layout))
        {
            diagnostic = "Dock layout 不能为空。";
            return false;
        }

        normalized = layout.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (!normalized.EndsWith('\n'))
        {
            normalized += '\n';
        }

        int byteLength = Encoding.UTF8.GetByteCount(normalized);
        if (byteLength is <= 0 or > MaximumUtf8Bytes)
        {
            diagnostic = $"Dock layout UTF-8 大小必须在 1..{MaximumUtf8Bytes} bytes。";
            return false;
        }

        string[] lines = normalized.Split('\n');
        if (lines.Length > MaximumLines + 1)
        {
            diagnostic = $"Dock layout 行数不得超过 {MaximumLines}。";
            return false;
        }

        bool hasDockingData = false;
        bool hasSection = false;
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.Length > MaximumLineLength)
            {
                diagnostic = $"Dock layout 第 {lineIndex + 1} 行超过 {MaximumLineLength} 字符。";
                return false;
            }

            for (int characterIndex = 0; characterIndex < line.Length; characterIndex++)
            {
                char character = line[characterIndex];
                if (character != '\t' && char.IsControl(character))
                {
                    diagnostic = $"Dock layout 第 {lineIndex + 1} 行包含控制字符。";
                    return false;
                }
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (!IsAllowedSection(line, out bool dockingData))
                {
                    diagnostic = $"Dock layout 第 {lineIndex + 1} 行包含未知或畸形 section。";
                    return false;
                }

                hasSection = true;
                hasDockingData |= dockingData;
            }
            else if (!hasSection)
            {
                diagnostic = "Dock layout 在首个 section 前包含数据。";
                return false;
            }
        }

        if (!hasDockingData)
        {
            diagnostic = "Dock layout 缺少 [Docking][Data] section。";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private static bool IsAllowedSection(string line, out bool dockingData)
    {
        dockingData = string.Equals(line, "[Docking][Data]", StringComparison.Ordinal);
        return dockingData ||
            HasNamedSection(line, "[Window][") ||
            HasNamedSection(line, "[Table][");
    }

    private static bool HasNamedSection(string line, string prefix)
    {
        return line.StartsWith(prefix, StringComparison.Ordinal) &&
            line.Length > prefix.Length &&
            line[^1] == ']';
    }
}
