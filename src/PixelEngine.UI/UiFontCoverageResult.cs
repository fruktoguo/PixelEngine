namespace PixelEngine.UI;

/// <summary>
/// 文本码点覆盖扫描结果。
/// </summary>
/// <param name="ScannedCodePoints">扫描过的 Unicode 码点数。</param>
/// <param name="MissingCodePoints">不在共享 glyph range 中的码点数。</param>
public readonly record struct UiFontCoverageResult(int ScannedCodePoints, int MissingCodePoints)
{
    /// <summary>
    /// 是否存在缺字风险。
    /// </summary>
    public bool HasMissingGlyphs => MissingCodePoints > 0;
}
