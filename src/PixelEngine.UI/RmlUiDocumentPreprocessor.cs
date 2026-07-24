using System.Xml.Linq;
using System.Text;

namespace PixelEngine.UI;

/// <summary>
/// RmlUi 文档载入前处理器：保留 Web-first 源文件语义，并把 native 后端需要的资源与 DPI 单位转换隔离在载入边界。
/// </summary>
internal static class RmlUiDocumentPreprocessor
{
    /// <summary>
    /// 预处理 RmlUi 文档；PNG 引用会改写为 TGA 缓存路径，CSS px 会改写为 RmlUi dp。
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
            XAttribute? styleAttribute = element.Attribute("style");
            if (styleAttribute is not null)
            {
                string normalized = NormalizeCssPixelsToDensityUnits(styleAttribute.Value);
                if (!string.Equals(normalized, styleAttribute.Value, StringComparison.Ordinal))
                {
                    styleAttribute.Value = normalized;
                    rewritten = true;
                }
            }

            string localName = element.Name.LocalName;
            if (StringEquals(localName, "style"))
            {
                string normalized = NormalizeCssPixelsToDensityUnits(element.Value);
                if (!string.Equals(normalized, element.Value, StringComparison.Ordinal))
                {
                    element.Value = normalized;
                    rewritten = true;
                }

                continue;
            }

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
    /// 把 CSS declaration 中的数值 px 长度改写为 dp。源 XHTML 继续使用浏览器熟悉的 px，
    /// RmlUi 则通过 Context DPR 在物理分辨率上得到等价的 CanvasScaler 尺寸。
    /// </summary>
    internal static string NormalizeCssPixelsToDensityUnits(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        StringBuilder? builder = null;
        int copyStart = 0;
        char quote = '\0';
        bool comment = false;
        for (int i = 0; i < css.Length; i++)
        {
            char current = css[i];
            if (comment)
            {
                if (current == '*' && i + 1 < css.Length && css[i + 1] == '/')
                {
                    comment = false;
                    i++;
                }

                continue;
            }

            if (quote != '\0')
            {
                if (current == '\\')
                {
                    i++;
                }
                else if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if (current == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                comment = true;
                i++;
                continue;
            }

            if (!IsCssNumberStart(css, i))
            {
                continue;
            }

            int numberStart = i;
            int numberEnd = ConsumeCssNumber(css, numberStart);
            if (numberEnd + 1 >= css.Length ||
                (css[numberEnd] is not ('p' or 'P')) ||
                (css[numberEnd + 1] is not ('x' or 'X')) ||
                (numberEnd + 2 < css.Length && IsCssIdentifierCharacter(css[numberEnd + 2])))
            {
                i = numberEnd - 1;
                continue;
            }

            builder ??= new StringBuilder(css.Length);
            _ = builder.Append(css, copyStart, numberEnd - copyStart);
            _ = builder.Append("dp");
            i = numberEnd + 1;
            copyStart = i + 1;
        }

        if (builder is null)
        {
            return css;
        }

        _ = builder.Append(css, copyStart, css.Length - copyStart);
        return builder.ToString();
    }

    private static bool IsCssNumberStart(string css, int index)
    {
        char current = css[index];
        bool numeric = char.IsAsciiDigit(current) ||
            (current == '.' && index + 1 < css.Length && char.IsAsciiDigit(css[index + 1])) ||
            (current is '+' or '-' && index + 1 < css.Length &&
                (char.IsAsciiDigit(css[index + 1]) ||
                 (css[index + 1] == '.' && index + 2 < css.Length && char.IsAsciiDigit(css[index + 2]))));
        return numeric && (index == 0 || !IsCssIdentifierCharacter(css[index - 1]));
    }

    private static int ConsumeCssNumber(string css, int index)
    {
        int cursor = index;
        if (css[cursor] is '+' or '-')
        {
            cursor++;
        }

        while (cursor < css.Length && char.IsAsciiDigit(css[cursor]))
        {
            cursor++;
        }

        if (cursor < css.Length && css[cursor] == '.')
        {
            cursor++;
            while (cursor < css.Length && char.IsAsciiDigit(css[cursor]))
            {
                cursor++;
            }
        }

        return cursor;
    }

    private static bool IsCssIdentifierCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value is '_' or '-';
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
