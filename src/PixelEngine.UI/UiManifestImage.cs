namespace PixelEngine.UI;

/// <summary>
/// content/ui 清单中的一个图片资产条目。
/// </summary>
/// <param name="Id">图片稳定字符串 id。</param>
/// <param name="RelativePath">相对 content/ui 根目录的图片路径，必须位于 images/ 下。</param>
/// <param name="FullPath">规范化后的磁盘绝对路径。</param>
/// <param name="Preload">是否应在启动或场景载入时预载。</param>
/// <param name="StableId">稳定数值图片 id。</param>
public readonly record struct UiManifestImage(
    string Id,
    string RelativePath,
    string FullPath,
    bool Preload,
    int StableId);
