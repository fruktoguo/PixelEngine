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

        XDocument document = XDocument.Load(source.Path, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("UI 文档缺少根节点。");
        string title = ReadAttribute(root, "title") ?? Path.GetFileNameWithoutExtension(source.Path);
        ManagedUiBox rootBox = ReadBox(root);
        List<ManagedUiControl> controls = new(Math.Min(maxControls, 16));
        CollectControls(root, source.Path, controls, maxControls);
        return new ManagedUiDocument(handle, source, title, rootBox, [.. controls]);
    }

    private static void CollectControls(XElement element, string documentPath, List<ManagedUiControl> controls, int maxControls)
    {
        if (controls.Count >= maxControls)
        {
            throw new InvalidDataException("UI 文档控件数量超过 ManagedFallbackBackend 容量。");
        }

        ManagedUiControl? control = TryCreateControl(element, documentPath);
        if (control is not null)
        {
            controls.Add(control);
        }

        foreach (XElement child in element.Elements())
        {
            CollectControls(child, documentPath, controls, maxControls);
        }
    }

    private static ManagedUiControl? TryCreateControl(XElement element, string documentPath)
    {
        string name = element.Name.LocalName;
        if (StringEquals(name, "text") || StringEquals(name, "p") || StringEquals(name, "span"))
        {
            string text = ReadText(element);
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Text,
                Id = ReadId(element, text),
                Text = text,
                Element = new UiElementId(UiStableId.Hash(ReadId(element, text))),
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
            };
        }

        if (StringEquals(name, "img") || StringEquals(name, "image"))
        {
            string id = ReadId(element, ReadAttribute(element, "data-image") ?? ReadAttribute(element, "src") ?? "image");
            string imagePath = ResolveImagePath(documentPath, element);
            UiImageBitmap bitmap = UiPngImageLoader.Load(imagePath);
            string? style = ReadAttribute(element, "style");
            float width = ReadFloat(element, style, "width", "width") ?? bitmap.Width;
            float height = ReadFloat(element, style, "height", "height") ?? bitmap.Height;
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

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

}
