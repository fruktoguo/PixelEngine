namespace PixelEngine.UI;

/// <summary>
/// 游戏 UI 图片预加载契约；在文档正式载入前将 PNG 转为 RmlUi 可消费的 TGA 缓存。
/// </summary>
internal interface IGameUiImagePreloader
{
    /// <summary>
    /// 预加载并转换指定 UI 图片路径，结果写入后端图片缓存。
    /// </summary>
    /// <param name="path">已解析的 PNG 图片绝对路径。</param>
    void PreloadImage(string path);
}
