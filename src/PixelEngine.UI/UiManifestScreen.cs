namespace PixelEngine.UI;

/// <summary>
/// content/ui 清单中的一个屏幕条目。
/// </summary>
/// <param name="Id">屏幕稳定字符串 id。</param>
/// <param name="RelativePath">相对 content/ui 根目录的文档路径。</param>
/// <param name="FullPath">规范化后的磁盘绝对路径。</param>
/// <param name="Preload">是否应在启动或场景载入时预载。</param>
/// <param name="ScreenId">稳定数值屏幕 id。</param>
public readonly record struct UiManifestScreen(
    string Id,
    string RelativePath,
    string FullPath,
    bool Preload,
    UiScreenId ScreenId)
{
    /// <summary>
    /// 转换为后端可载入的文档来源。
    /// </summary>
    /// <returns>文档来源。</returns>
    public UiDocumentSource ToDocumentSource()
    {
        return UiDocumentSource.Asset(FullPath, ScreenId.Value);
    }
}
