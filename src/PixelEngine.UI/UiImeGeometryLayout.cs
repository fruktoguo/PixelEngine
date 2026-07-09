namespace PixelEngine.UI;

/// <summary>
/// 统一 ManagedFallback / RmlUi 预编辑 overlay 的 caret rect 与候选窗锚点布局契约。
/// </summary>
public static class UiImeGeometryLayout
{
    /// <summary>预编辑 overlay 相对视口边距。</summary>
    public const float OverlayMargin = 12f;

    /// <summary>预编辑 overlay 高度。</summary>
    public const float OverlayHeight = 42f;

    /// <summary>预编辑 overlay 最小宽度。</summary>
    public const float MinOverlayWidth = 180f;

    /// <summary>单字符近似宽度（产品 preedit overlay 估算）。</summary>
    public const float CharWidth = 10f;

    /// <summary>overlay 内容区水平内边距。</summary>
    public const float ContentPadX = 8f;

    /// <summary>overlay 内容区垂直内边距。</summary>
    public const float ContentPadY = 10f;

    /// <summary>caret 宽度。</summary>
    public const float CaretWidth = 2f;

    /// <summary>caret 高度。</summary>
    public const float CaretHeight = 18f;

    /// <summary>overlay 前缀「IME 」占用的字符数。</summary>
    public const int OverlayLabelCharCount = 4;

    /// <summary>
    /// 按当前视口与预编辑文本计算 caret rect / 候选锚点。
    /// </summary>
    /// <param name="viewport">UI 视口。</param>
    /// <param name="textLength">预编辑文本字符数。</param>
    /// <param name="cursorIndex">预编辑内光标索引。</param>
    /// <returns>定位几何；无有效预编辑时返回 <see cref="UiImeGeometry.None" />。</returns>
    public static UiImeGeometry ComputePreeditOverlayGeometry(
        in UiViewport viewport,
        int textLength,
        int cursorIndex)
    {
        if (textLength <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return UiImeGeometry.None;
        }

        float overlayWidth = Math.Min(
            Math.Max(MinOverlayWidth, (textLength * CharWidth) + 32f),
            Math.Max(MinOverlayWidth, viewport.Width - (OverlayMargin * 2f)));
        float overlayX = viewport.X + OverlayMargin;
        float overlayY = Math.Max(viewport.Y, viewport.Y + viewport.Height - OverlayHeight - OverlayMargin);
        int clampedCursor = Math.Clamp(cursorIndex, 0, textLength);
        float caretX = overlayX + ContentPadX + ((OverlayLabelCharCount + clampedCursor) * CharWidth);
        // 保证 caret 仍落在 overlay 水平范围内。
        float maxCaretX = overlayX + Math.Max(ContentPadX, overlayWidth - ContentPadX - CaretWidth);
        caretX = Math.Clamp(caretX, overlayX + ContentPadX, maxCaretX);
        float caretY = overlayY + ContentPadY;
        return UiImeGeometry.FromCaretRect(caretX, caretY, CaretWidth, CaretHeight);
    }
}
