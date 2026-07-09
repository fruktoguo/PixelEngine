using System.Xml.Linq;

namespace PixelEngine.UI;

/// <summary>
/// RmlUi 文档载入前处理器：扫描 <c>img</c>/<c>image</c> 标签，将 PNG 引用改写为 TGA 缓存路径。
/// </summary>
internal static class RmlUiDocumentPreprocessor
{
    /// <summary>
    /// 预处理 RmlUi 文档；若图片引用被改写则返回内存中的 XML 字符串，否则返回原始文件文本。
    /// </summary>
    /// <param name="documentPath">RmlUi 文档绝对或相对路径。</param>
    /// <param name="imageCache">PNG→TGA 转换缓存。</param>
    /// <returns>可供 RmlUi native 载入的文档文本。</returns>
    internal static string Prepare(string documentPath, RmlUiImageAssetCache imageCache)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(imageCache);
        if (!File.Exists(documentPath))
        {
            throw new FileNotFoundException("找不到 RmlUi 文档。", documentPath);
        }

        XDocument document = XDocument.Load(documentPath, LoadOptions.PreserveWhitespace);
        bool rewritten = false;
        foreach (XElement element in document.Descendants())
        {
            string localName = element.Name.LocalName;
            if (!StringEquals(localName, "img") && !StringEquals(localName, "image"))
            {
                continue;
            }

            string? imageId = ReadAttribute(element, "data-image");
            string? source = ReadAttribute(element, "src");
            string pngPath = UiImageAssetResolver.ResolveImagePath(documentPath, imageId, source);
            string tgaPath = imageCache.ConvertPngToTga(pngPath);
            element.SetAttributeValue("src", ToRmlPath(tgaPath));
            rewritten = true;
        }

        return rewritten
            ? document.ToString(SaveOptions.DisableFormatting)
            : File.ReadAllText(documentPath);
    }

    /// <summary>
    /// 将文件系统路径规范为 RmlUi 可识别的正斜杠绝对路径。
    /// </summary>
    private static string ToRmlPath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    /// <summary>
    /// 读取元素属性并去除首尾空白；空值返回 null。
    /// </summary>
    private static string? ReadAttribute(XElement element, string name)
    {
        XAttribute? attribute = element.Attribute(name);
        return string.IsNullOrWhiteSpace(attribute?.Value) ? null : attribute.Value.Trim();
    }

    /// <summary>
    /// 大小写不敏感的字符串相等比较。
    /// </summary>
    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
