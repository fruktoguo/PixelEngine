using System.Xml.Linq;

namespace PixelEngine.UI;

internal static class RmlUiDocumentPreprocessor
{
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

    private static string ToRmlPath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    private static string? ReadAttribute(XElement element, string name)
    {
        XAttribute? attribute = element.Attribute(name);
        return string.IsNullOrWhiteSpace(attribute?.Value) ? null : attribute.Value.Trim();
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
