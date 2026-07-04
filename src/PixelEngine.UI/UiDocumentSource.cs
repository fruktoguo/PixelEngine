namespace PixelEngine.UI;

/// <summary>
/// UI 文档来源。载入属于低频路径，允许保存资产路径字符串。
/// </summary>
/// <param name="Kind">来源类型。</param>
/// <param name="Path">资产路径或虚拟路径。</param>
/// <param name="StableId">内容稳定 id。</param>
public readonly record struct UiDocumentSource(UiDocumentSourceKind Kind, string Path, int StableId)
{
    /// <summary>
    /// 创建内容资产文档来源。
    /// </summary>
    /// <param name="path">资产路径。</param>
    /// <param name="stableId">稳定 id。</param>
    /// <returns>文档来源。</returns>
    public static UiDocumentSource Asset(string path, int stableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new UiDocumentSource(UiDocumentSourceKind.Asset, path, stableId);
    }
}
