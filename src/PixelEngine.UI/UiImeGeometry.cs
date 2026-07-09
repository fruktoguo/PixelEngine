namespace PixelEngine.UI;

/// <summary>
/// UI 坐标空间中的 IME caret rect 与候选窗锚点；仅用于平台定位 composition/candidate 窗口，不表示 committed text。
/// </summary>
public readonly struct UiImeGeometry
{
    /// <summary>
    /// 创建完整的 IME 定位几何。
    /// </summary>
    /// <param name="hasCaretRect">是否提供 caret rect。</param>
    /// <param name="caretX">caret 左上角 x（UI 坐标）。</param>
    /// <param name="caretY">caret 左上角 y（UI 坐标）。</param>
    /// <param name="caretWidth">caret 宽度。</param>
    /// <param name="caretHeight">caret 高度。</param>
    /// <param name="hasCandidateAnchor">是否提供候选窗锚点。</param>
    /// <param name="candidateAnchorX">候选窗锚点 x（UI 坐标）。</param>
    /// <param name="candidateAnchorY">候选窗锚点 y（UI 坐标）。</param>
    public UiImeGeometry(
        bool hasCaretRect,
        float caretX,
        float caretY,
        float caretWidth,
        float caretHeight,
        bool hasCandidateAnchor,
        float candidateAnchorX,
        float candidateAnchorY)
    {
        HasCaretRect = hasCaretRect &&
            float.IsFinite(caretX) &&
            float.IsFinite(caretY) &&
            float.IsFinite(caretWidth) &&
            float.IsFinite(caretHeight) &&
            caretWidth > 0f &&
            caretHeight > 0f;
        CaretX = HasCaretRect ? caretX : 0f;
        CaretY = HasCaretRect ? caretY : 0f;
        CaretWidth = HasCaretRect ? caretWidth : 0f;
        CaretHeight = HasCaretRect ? caretHeight : 0f;

        HasCandidateAnchor = hasCandidateAnchor &&
            float.IsFinite(candidateAnchorX) &&
            float.IsFinite(candidateAnchorY);
        CandidateAnchorX = HasCandidateAnchor ? candidateAnchorX : 0f;
        CandidateAnchorY = HasCandidateAnchor ? candidateAnchorY : 0f;
    }

    /// <summary>
    /// 无有效定位信息。
    /// </summary>
    public static UiImeGeometry None => default;

    /// <summary>
    /// 由 caret rect 创建几何；候选窗锚点默认落在 caret 左下角。
    /// </summary>
    /// <param name="caretX">caret 左上角 x。</param>
    /// <param name="caretY">caret 左上角 y。</param>
    /// <param name="caretWidth">caret 宽度。</param>
    /// <param name="caretHeight">caret 高度。</param>
    /// <returns>带 caret rect 与默认候选锚点的几何。</returns>
    public static UiImeGeometry FromCaretRect(float caretX, float caretY, float caretWidth, float caretHeight)
    {
        bool valid =
            float.IsFinite(caretX) &&
            float.IsFinite(caretY) &&
            float.IsFinite(caretWidth) &&
            float.IsFinite(caretHeight) &&
            caretWidth > 0f &&
            caretHeight > 0f;
        if (!valid)
        {
            return None;
        }

        return new UiImeGeometry(
            hasCaretRect: true,
            caretX,
            caretY,
            caretWidth,
            caretHeight,
            hasCandidateAnchor: true,
            caretX,
            caretY + caretHeight);
    }

    /// <summary>
    /// 是否存在有效 caret rect。
    /// </summary>
    public bool HasCaretRect { get; }

    /// <summary>
    /// caret 左上角 x（UI 坐标）。
    /// </summary>
    public float CaretX { get; }

    /// <summary>
    /// caret 左上角 y（UI 坐标）。
    /// </summary>
    public float CaretY { get; }

    /// <summary>
    /// caret 宽度。
    /// </summary>
    public float CaretWidth { get; }

    /// <summary>
    /// caret 高度。
    /// </summary>
    public float CaretHeight { get; }

    /// <summary>
    /// 是否存在有效候选窗锚点。
    /// </summary>
    public bool HasCandidateAnchor { get; }

    /// <summary>
    /// 候选窗锚点 x（UI 坐标）。
    /// </summary>
    public float CandidateAnchorX { get; }

    /// <summary>
    /// 候选窗锚点 y（UI 坐标）。
    /// </summary>
    public float CandidateAnchorY { get; }

    /// <summary>
    /// 是否至少提供 caret 或候选锚点之一。
    /// </summary>
    public bool HasAny => HasCaretRect || HasCandidateAnchor;

    /// <summary>
    /// 对几何做平移与可选缩放，供 Game View / DPI 坐标回写平台窗口。
    /// </summary>
    /// <param name="offsetX">x 平移。</param>
    /// <param name="offsetY">y 平移。</param>
    /// <param name="scaleX">x 缩放。</param>
    /// <param name="scaleY">y 缩放。</param>
    /// <returns>变换后的几何。</returns>
    public UiImeGeometry Transform(float offsetX, float offsetY, float scaleX = 1f, float scaleY = 1f)
    {
        if (!HasAny)
        {
            return None;
        }

        float sx = float.IsFinite(scaleX) && scaleX > 0f ? scaleX : 1f;
        float sy = float.IsFinite(scaleY) && scaleY > 0f ? scaleY : 1f;
        float ox = float.IsFinite(offsetX) ? offsetX : 0f;
        float oy = float.IsFinite(offsetY) ? offsetY : 0f;
        return new UiImeGeometry(
            HasCaretRect,
            (CaretX * sx) + ox,
            (CaretY * sy) + oy,
            CaretWidth * sx,
            CaretHeight * sy,
            HasCandidateAnchor,
            (CandidateAnchorX * sx) + ox,
            (CandidateAnchorY * sy) + oy);
    }
}
