namespace PixelEngine.UI;

/// <summary>
/// 统一收敛 UI committed text 与 IME composition 预编辑快照，保持两条输入通道互不冒充。
/// </summary>
internal static class UiTextCompositionNormalizer
{
    /// <summary>
    /// 夹取输入源上报长度并移除不能进入 UI 文本通道的控制字符。
    /// </summary>
    /// <param name="destination">输入源写入的文本缓冲。</param>
    /// <param name="textCount">输入源上报的写入字符数。</param>
    /// <returns>归一化后的文本长度。</returns>
    public static int NormalizeCommittedText(Span<char> destination, int textCount)
    {
        int clampedCount = Math.Clamp(textCount, 0, destination.Length);
        return CompactText(destination, clampedCount);
    }

    /// <summary>
    /// 夹取并归一化 IME composition 文本与光标/选区范围；inactive composition 不保留预编辑文本。
    /// </summary>
    /// <param name="destination">输入源写入的预编辑文本缓冲。</param>
    /// <param name="textCount">输入源上报的写入字符数。</param>
    /// <param name="sourceComposition">输入源上报的 composition 状态。</param>
    /// <param name="composition">归一化后的 composition 状态。</param>
    /// <returns>归一化后的预编辑文本长度；inactive 时恒为 0。</returns>
    public static int NormalizeCompositionText(
        Span<char> destination,
        int textCount,
        in UiTextComposition sourceComposition,
        out UiTextComposition composition)
    {
        int clampedCount = Math.Clamp(textCount, 0, destination.Length);
        int write = CompactText(destination, clampedCount);
        if (!sourceComposition.IsActive)
        {
            destination[..write].Clear();
            composition = UiTextComposition.Inactive;
            return 0;
        }

        composition = sourceComposition.ClampToTextLength(write);
        return write;
    }

    private static int CompactText(Span<char> destination, int textCount)
    {
        int write = 0;
        for (int i = 0; i < textCount; i++)
        {
            char character = destination[i];
            if (character == '\0' || char.IsControl(character))
            {
                continue;
            }

            destination[write++] = character;
        }

        destination[write..textCount].Clear();
        return write;
    }
}
