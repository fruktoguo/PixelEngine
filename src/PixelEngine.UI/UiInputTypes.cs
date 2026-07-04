namespace PixelEngine.UI;

/// <summary>
/// UI 指针按钮。
/// </summary>
public enum UiPointerButton : byte
{
    /// <summary>左键。</summary>
    Left = 0,
    /// <summary>右键。</summary>
    Right = 1,
    /// <summary>中键。</summary>
    Middle = 2,
}

/// <summary>
/// UI 键盘按键。具体键码由输入桥映射为稳定整数。
/// </summary>
/// <param name="Value">稳定键码。</param>
public readonly record struct UiKey(int Value);

/// <summary>
/// UI 键盘修饰键。
/// </summary>
[Flags]
public enum UiKeyModifiers : byte
{
    /// <summary>无修饰键。</summary>
    None = 0,
    /// <summary>Shift。</summary>
    Shift = 1 << 0,
    /// <summary>Ctrl。</summary>
    Control = 1 << 1,
    /// <summary>Alt。</summary>
    Alt = 1 << 2,
    /// <summary>Super/Windows/Command。</summary>
    Super = 1 << 3,
}
