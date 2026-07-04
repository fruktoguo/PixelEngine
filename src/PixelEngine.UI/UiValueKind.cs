namespace PixelEngine.UI;

/// <summary>
/// UI 值类型。
/// </summary>
public enum UiValueKind : byte
{
    /// <summary>
    /// 空值。
    /// </summary>
    Empty = 0,

    /// <summary>
    /// 布尔值。
    /// </summary>
    Boolean = 1,

    /// <summary>
    /// 64 位整数。
    /// </summary>
    Int64 = 2,

    /// <summary>
    /// 双精度浮点数。
    /// </summary>
    Double = 3,

    /// <summary>
    /// 字符串池句柄。
    /// </summary>
    StringHandle = 4,
}
