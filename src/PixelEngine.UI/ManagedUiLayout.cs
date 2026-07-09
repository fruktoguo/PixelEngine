using System.Globalization;
using System.Xml.Linq;

namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 使用的 XHTML 子集解析器。
/// </summary>
internal static class ManagedUiLayout
{
    internal static ManagedUiDocument Load(
        UiDocumentHandle handle,
        in UiDocumentSource source,
        int maxControls)
    {
        if (source.Kind != UiDocumentSourceKind.Asset)
        {
            throw new NotSupportedException("ManagedFallbackBackend 当前只支持资产文档来源。");
        }

        if (!File.Exists(source.Path))
        {
            throw new FileNotFoundException("找不到 UI 文档。", source.Path);
        }

        // 解析 XHTML 子集：先读 style 规则与根盒模型，再 DFS 收集可绑定控件。
        XDocument document = XDocument.Load(source.Path, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("UI 文档缺少根节点。");
        string title = ReadAttribute(root, "title") ?? Path.GetFileNameWithoutExtension(source.Path);
        Dictionary<string, ManagedUiStyle> styleRules = ReadStyleRules(root);
        ManagedUiBox rootBox = ReadBox(root);
        List<ManagedUiControl> controls = new(Math.Min(maxControls, 16));
        CollectControls(root, source.Path, styleRules, controls, maxControls);
        return new ManagedUiDocument(handle, source, title, rootBox, [.. controls]);
    }

    private static void CollectControls(
        XElement element,
        string documentPath,
        Dictionary<string, ManagedUiStyle> styleRules,
        List<ManagedUiControl> controls,
        int maxControls)
    {
        if (controls.Count >= maxControls)
        {
            throw new InvalidDataException("UI 文档控件数量超过 ManagedFallbackBackend 容量。");
        }

        ManagedUiControl? control = TryCreateControl(element, documentPath, styleRules);
        if (control is not null)
        {
            controls.Add(control);
        }

        foreach (XElement child in element.Elements())
        {
            CollectControls(child, documentPath, styleRules, controls, maxControls);
        }
    }

    private static ManagedUiControl? TryCreateControl(
        XElement element,
        string documentPath,
        Dictionary<string, ManagedUiStyle> styleRules)
    {
        string name = element.Name.LocalName;
        ManagedUiStyle style = ResolveStyle(element, styleRules);
        if (StringEquals(name, "text") || StringEquals(name, "p") || StringEquals(name, "span"))
        {
            string text = ReadText(element);
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Text,
                Id = ReadId(element, text),
                Text = text,
                Element = new UiElementId(UiStableId.Hash(ReadId(element, text))),
                Style = style,
            };
        }

        if (StringEquals(name, "button"))
        {
            string text = ReadText(element);
            string action = ReadAttribute(element, "data-event-click") ??
                ReadAttribute(element, "action") ??
                ReadId(element, text);
            string id = ReadId(element, action);
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Button,
                Id = id,
                Text = text,
                Element = new UiElementId(UiStableId.Hash(id)),
                Action = new UiActionId(UiStableId.Hash(action)),
                Style = style,
            };
        }

        if (StringEquals(name, "checkbox") || IsCheckboxInput(element))
        {
            string id = ReadId(element, ReadText(element));
            string path = ReadAttribute(element, "data-model") ?? ReadAttribute(element, "path") ?? id;
            UiPathId pathId = UiModelPathName.ToPathId(path);
            bool initial = TryReadBoolean(ReadAttribute(element, "checked"), out bool parsed) && parsed;
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Checkbox,
                Id = id,
                Text = ReadText(element),
                Element = new UiElementId(UiStableId.Hash(id)),
                Action = new UiActionId(UiStableId.Hash(ReadAttribute(element, "data-event-change") ?? id)),
                Path = pathId,
                ModelVariableName = UiModelPathName.ToVariableName(path),
                Value = UiValue.FromBoolean(initial),
                Style = style,
            };
        }

        if (StringEquals(name, "progress"))
        {
            string id = ReadId(element, ReadText(element));
            string path = ReadAttribute(element, "data-model") ?? ReadAttribute(element, "path") ?? id;
            UiPathId pathId = UiModelPathName.ToPathId(path);
            double value = TryReadDouble(ReadAttribute(element, "value"), out double parsed) ? parsed : 0.0;
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Progress,
                Id = id,
                Text = ReadText(element),
                Element = new UiElementId(UiStableId.Hash(id)),
                Path = pathId,
                ModelVariableName = UiModelPathName.ToVariableName(path),
                Value = new UiValue(value),
                Style = style,
            };
        }

        if (StringEquals(name, "img") || StringEquals(name, "image"))
        {
            string id = ReadId(element, ReadAttribute(element, "data-image") ?? ReadAttribute(element, "src") ?? "image");
            string imagePath = ResolveImagePath(documentPath, element);
            UiImageBitmap bitmap = UiPngImageLoader.Load(imagePath);
            float width = style.Width ?? bitmap.Width;
            float height = style.Height ?? bitmap.Height;
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Image,
                Id = id,
                Text = ReadAttribute(element, "alt") ?? id,
                Element = new UiElementId(UiStableId.Hash(id)),
                ImagePath = imagePath,
                ImageWidth = bitmap.Width,
                ImageHeight = bitmap.Height,
                DisplayWidth = width,
                DisplayHeight = height,
                Style = style,
            };
        }

        return null;
    }

    private static string ResolveImagePath(string documentPath, XElement element)
    {
        string? imageId = ReadAttribute(element, "data-image");
        string? source = ReadAttribute(element, "src");
        return UiImageAssetResolver.ResolveImagePath(documentPath, imageId, source);
    }

    private static bool IsCheckboxInput(XElement element)
    {
        return StringEquals(element.Name.LocalName, "input") &&
            StringEquals(ReadAttribute(element, "type"), "checkbox");
    }

    private static string ReadId(XElement element, string fallback)
    {
        return ReadAttribute(element, "id") ??
            ReadAttribute(element, "name") ??
            fallback;
    }

    private static string ReadText(XElement element)
    {
        return ReadAttribute(element, "text") ??
            ReadAttribute(element, "label") ??
            element.Value.Trim();
    }

    private static ManagedUiBox ReadBox(XElement element)
    {
        string? style = ReadAttribute(element, "style");
        return new ManagedUiBox(
            ReadFloat(element, style, "x", "left"),
            ReadFloat(element, style, "y", "top"),
            ReadFloat(element, style, "width", "width"),
            ReadFloat(element, style, "height", "height"));
    }

    private static Dictionary<string, ManagedUiStyle> ReadStyleRules(XElement root)
    {
        Dictionary<string, ManagedUiStyle> rules = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement styleElement in root.Descendants().Where(static element => StringEquals(element.Name.LocalName, "style")))
        {
            ParseStyleSheet(styleElement.Value, rules);
        }

        return rules;
    }

    private static void ParseStyleSheet(string css, Dictionary<string, ManagedUiStyle> rules)
    {
        ReadOnlySpan<char> remaining = css.AsSpan();
        while (!remaining.IsEmpty)
        {
            int open = remaining.IndexOf('{');
            if (open < 0)
            {
                break;
            }

            ReadOnlySpan<char> selectors = remaining[..open].Trim();
            remaining = remaining[(open + 1)..];
            int close = remaining.IndexOf('}');
            if (close < 0)
            {
                break;
            }

            ReadOnlySpan<char> declarations = remaining[..close];
            ManagedUiStyle style = ReadStyle(declarations.ToString());
            int selectorStart = 0;
            for (int i = 0; i <= selectors.Length; i++)
            {
                if (i < selectors.Length && selectors[i] != ',')
                {
                    continue;
                }

                ReadOnlySpan<char> selector = selectors[selectorStart..i].Trim();
                selectorStart = i + 1;
                if (!IsSupportedSelector(selector))
                {
                    continue;
                }

                string key = selector.ToString();
                rules[key] = rules.TryGetValue(key, out ManagedUiStyle existing)
                    ? existing.Merge(in style)
                    : style;
            }

            remaining = remaining[(close + 1)..];
        }
    }

    private static bool IsSupportedSelector(ReadOnlySpan<char> selector)
    {
        return !selector.IsEmpty &&
            (selector[0] is '#' or '.'
                ? selector.Length > 1 && IsSimpleSelectorToken(selector[1..])
                : IsSimpleSelectorToken(selector));
    }

    private static ManagedUiStyle ResolveStyle(XElement element, Dictionary<string, ManagedUiStyle> rules)
    {
        ManagedUiStyle style = ManagedUiStyle.Empty;
        string tag = element.Name.LocalName;
        if (rules.TryGetValue(tag, out ManagedUiStyle tagStyle))
        {
            style = style.Merge(in tagStyle);
        }

        string? classes = ReadAttribute(element, "class");
        if (!string.IsNullOrWhiteSpace(classes))
        {
            foreach (string className in SplitClassNames(classes))
            {
                if (rules.TryGetValue("." + className, out ManagedUiStyle classStyle))
                {
                    style = style.Merge(in classStyle);
                }
            }
        }

        string? id = ReadAttribute(element, "id");
        if (!string.IsNullOrWhiteSpace(id) &&
            rules.TryGetValue("#" + id, out ManagedUiStyle idStyle))
        {
            style = style.Merge(in idStyle);
        }

        ManagedUiStyle elementStyle = ReadElementStyle(element);
        return style.Merge(in elementStyle);
    }

    private static ManagedUiStyle ReadElementStyle(XElement element)
    {
        string? inlineStyle = ReadAttribute(element, "style");
        ManagedUiStyle inline = ReadStyle(inlineStyle);
        ManagedUiStyle attributes = new(
            ReadFloat(element, inlineStyle, "x", "left"),
            ReadFloat(element, inlineStyle, "y", "top"),
            ReadFloat(element, inlineStyle, "width", "width"),
            ReadFloat(element, inlineStyle, "height", "height"),
            ReadFloat(element, inlineStyle, "margin-top", "margin-top"));
        return inline.Merge(in attributes);
    }

    private static IEnumerable<string> SplitClassNames(string value)
    {
        return value.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static ManagedUiStyle ReadStyle(string? style)
    {
        float? marginTop = TryReadFloat(ReadStyleProperty(style, "margin-top"), out float explicitMarginTop)
            ? explicitMarginTop
            : TryReadMarginTop(ReadStyleProperty(style, "margin"), out float shorthandMarginTop)
                ? shorthandMarginTop
                : null;
        return new ManagedUiStyle(
            X: TryReadFloat(ReadStyleProperty(style, "left"), out float x) ? x : null,
            Y: TryReadFloat(ReadStyleProperty(style, "top"), out float y) ? y : null,
            Width: TryReadFloat(ReadStyleProperty(style, "width"), out float width) ? width : null,
            Height: TryReadFloat(ReadStyleProperty(style, "height"), out float height) ? height : null,
            MarginTop: marginTop);
    }

    private static float? ReadFloat(XElement element, string? style, string attributeName, string cssName)
    {
        return TryReadFloat(ReadAttribute(element, attributeName), out float value) ||
            TryReadFloat(ReadAttribute(element, "data-" + attributeName), out value) ||
            TryReadFloat(ReadStyleProperty(style, cssName), out value)
            ? value
            : null;
    }

    private static string? ReadStyleProperty(string? style, string name)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return null;
        }

        ReadOnlySpan<char> remaining = style.AsSpan();
        while (!remaining.IsEmpty)
        {
            int separator = remaining.IndexOf(';');
            ReadOnlySpan<char> item = separator >= 0 ? remaining[..separator] : remaining;
            int colon = item.IndexOf(':');
            if (colon > 0)
            {
                ReadOnlySpan<char> key = item[..colon].Trim();
                ReadOnlySpan<char> value = item[(colon + 1)..].Trim();
                if (key.Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return value.ToString();
                }
            }

            if (separator < 0)
            {
                break;
            }

            remaining = remaining[(separator + 1)..];
        }

        return null;
    }

    private static string? ReadAttribute(XElement element, string name)
    {
        XAttribute? attribute = element.Attribute(name);
        return string.IsNullOrWhiteSpace(attribute?.Value) ? null : attribute.Value.Trim();
    }

    private static bool TryReadBoolean(string? value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (string.Equals(value, "1", StringComparison.Ordinal))
        {
            result = true;
            return true;
        }

        result = false;
        return string.Equals(value, "0", StringComparison.Ordinal);
    }

    private static bool TryReadDouble(string? value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryReadFloat(string? value, out float result)
    {
        result = 0f;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^2].Trim();
        }

        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
            float.IsFinite(result);
    }

    private static bool TryReadMarginTop(string? value, out float result)
    {
        result = 0f;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> remaining = value.AsSpan().Trim();
        if (remaining.IsEmpty)
        {
            return false;
        }

        int separator = remaining.IndexOfAny(" \t\r\n".AsSpan());
        ReadOnlySpan<char> top = separator >= 0 ? remaining[..separator] : remaining;
        return TryReadFloat(top.ToString(), out result);
    }

    private static bool IsSimpleSelectorToken(ReadOnlySpan<char> selector)
    {
        if (selector.IsEmpty)
        {
            return false;
        }

        for (int i = 0; i < selector.Length; i++)
        {
            char ch = selector[i];
            bool allowed =
                ch is (>= 'a' and <= 'z') or
                    (>= 'A' and <= 'Z') or
                    (>= '0' and <= '9') or
                    '_' or
                    '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

}
