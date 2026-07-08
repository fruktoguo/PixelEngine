namespace PixelEngine.UI;

/// <summary>
/// UI 字符串句柄解析器，用于把热路径中的 blittable <see cref="UiStringHandle" /> 映射为实际文本。
/// </summary>
public interface IUiStringResolver
{
    /// <summary>
    /// 尝试解析字符串句柄。
    /// </summary>
    /// <param name="handle">字符串池句柄。</param>
    /// <param name="value">解析出的字符串。</param>
    /// <returns>句柄存在则返回 true。</returns>
    bool TryGetString(UiStringHandle handle, out string value);
}

/// <summary>
/// 简单稳定的 UI 字符串池，负责把托管字符串 intern 成 <see cref="UiStringHandle" />。
/// </summary>
public sealed class UiStringPool : IUiStringResolver
{
    private readonly Dictionary<string, UiStringHandle> _handles = new(StringComparer.Ordinal);
    private readonly List<string> _strings = [];

    /// <summary>
    /// 已登记字符串数量。
    /// </summary>
    public int Count => _strings.Count;

    /// <summary>
    /// 登记字符串并返回稳定句柄；重复字符串复用原句柄。
    /// </summary>
    /// <param name="value">要登记的字符串。</param>
    /// <returns>字符串句柄。</returns>
    public UiStringHandle Intern(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_handles.TryGetValue(value, out UiStringHandle existing))
        {
            return existing;
        }

        UiStringHandle handle = new(_strings.Count + 1);
        _strings.Add(value);
        _handles.Add(value, handle);
        return handle;
    }

    /// <summary>
    /// 尝试按句柄读取已登记字符串。
    /// </summary>
    /// <param name="handle">字符串池句柄。</param>
    /// <param name="value">解析出的字符串。</param>
    /// <returns>句柄有效则返回 true。</returns>
    public bool TryGetString(UiStringHandle handle, out string value)
    {
        int index = handle.Value - 1;
        if ((uint)index >= (uint)_strings.Count)
        {
            value = string.Empty;
            return false;
        }

        value = _strings[index];
        return true;
    }

    /// <summary>
    /// 清空字符串池并使既有句柄失效。
    /// </summary>
    public void Clear()
    {
        _handles.Clear();
        _strings.Clear();
    }
}
