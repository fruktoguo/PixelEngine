using System.Text;

namespace PixelEngine.UI;

/// <summary>
/// UI 模型路径命名工具，把 dotted path 映射为后端可用的合法变量名。
/// </summary>
public static class UiModelPathName
{
    /// <summary>
    /// 把模型路径映射为 ASCII 标识符，并追加稳定 hash 后缀避免规范化碰撞。
    /// </summary>
    /// <param name="path">原始模型路径，例如 <c>hud.health.current</c>。</param>
    /// <returns>可供 RmlUi data-model / JS bridge 使用的合法变量名。</returns>
    public static string ToVariableName(string path)
    {
        path = NormalizePath(path);
        StringBuilder builder = new(path.Length + 12);
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (IsAsciiLetter(c) || c == '_' || char.IsAsciiDigit(c))
            {
                _ = builder.Append(c);
                continue;
            }

            if (c is '.' or '-' or '/' or ':' or ' ')
            {
                _ = builder.Append('_');
                continue;
            }

            _ = builder.Append("_u");
            _ = builder.Append(((int)c).ToString("X4"));
            _ = builder.Append('_');
        }

        if (builder.Length == 0 || char.IsAsciiDigit(builder[0]))
        {
            _ = builder.Insert(0, '_');
        }

        _ = builder.Append("__");
        _ = builder.Append(unchecked((uint)UiStableId.Hash(path)).ToString("X8"));
        return builder.ToString();
    }

    /// <summary>
    /// 计算模型路径的稳定数值句柄。
    /// </summary>
    /// <param name="path">原始模型路径。</param>
    /// <returns>模型路径句柄。</returns>
    public static UiPathId ToPathId(string path)
    {
        return new UiPathId(UiStableId.Hash(NormalizePath(path)));
    }

    /// <summary>
    /// 判断字符串是否符合 UI 后端变量名规则。
    /// </summary>
    /// <param name="name">变量名。</param>
    /// <returns>合法则返回 true。</returns>
    public static bool IsLegalVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || char.IsAsciiDigit(name[0]))
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!IsAsciiLetter(c) && !char.IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Trim();
    }

    private static bool IsAsciiLetter(char c)
    {
        return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }
}
