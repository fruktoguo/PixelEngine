namespace PixelEngine.UI;

/// <summary>
/// 表示平台 IME composition 的当前预编辑状态；已提交文本仍通过独立文本输入通道传递。
/// </summary>
public readonly struct UiTextComposition
{
    /// <summary>
    /// 创建 UI 文本预编辑状态。
    /// </summary>
    /// <param name="isActive">当前是否存在平台 IME composition。</param>
    /// <param name="cursorIndex">预编辑文本内的光标字符索引。</param>
    /// <param name="selectionStart">预编辑文本内的选区起点字符索引。</param>
    /// <param name="selectionLength">预编辑文本内的选区字符长度。</param>
    public UiTextComposition(bool isActive, int cursorIndex, int selectionStart = 0, int selectionLength = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cursorIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionStart);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionLength);
        IsActive = isActive;
        CursorIndex = cursorIndex;
        SelectionStart = selectionStart;
        SelectionLength = selectionLength;
    }

    /// <summary>
    /// 不存在平台 IME composition 的空状态。
    /// </summary>
    public static UiTextComposition Inactive => default;

    /// <summary>
    /// 当前是否存在平台 IME composition。
    /// </summary>
    public bool IsActive { get; }

    /// <summary>
    /// 预编辑文本内的光标字符索引。
    /// </summary>
    public int CursorIndex { get; }

    /// <summary>
    /// 预编辑文本内的选区起点字符索引。
    /// </summary>
    public int SelectionStart { get; }

    /// <summary>
    /// 预编辑文本内的选区字符长度。
    /// </summary>
    public int SelectionLength { get; }

    /// <summary>
    /// 按当前预编辑文本长度夹取光标与选区范围。
    /// </summary>
    /// <param name="textLength">预编辑文本字符数。</param>
    /// <returns>范围合法化后的预编辑状态。</returns>
    public UiTextComposition ClampToTextLength(int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);
        if (!IsActive)
        {
            return Inactive;
        }

        int cursor = Math.Clamp(CursorIndex, 0, textLength);
        int selectionStart = Math.Clamp(SelectionStart, 0, textLength);
        int selectionLength = Math.Min(SelectionLength, textLength - selectionStart);
        return new UiTextComposition(isActive: true, cursor, selectionStart, selectionLength);
    }
}
