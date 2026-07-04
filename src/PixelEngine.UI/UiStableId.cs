namespace PixelEngine.UI;

/// <summary>
/// UI 稳定字符串 id 工具，用于把 action/path/element 名称映射为可持久比较的整数。
/// </summary>
public static class UiStableId
{
    /// <summary>
    /// 使用 FNV-1a 计算稳定正整数 hash。
    /// </summary>
    /// <param name="value">稳定字符串。</param>
    /// <returns>非零正整数 hash。</returns>
    public static int Hash(string? value)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        uint hash = offset;
        string text = value ?? string.Empty;
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= prime;
        }

        int result = unchecked((int)(hash & 0x7FFF_FFFFu));
        return result == 0 ? 1 : result;
    }
}
