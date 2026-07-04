using System.Globalization;
using System.Xml.Linq;

namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 使用的 XHTML 子集解析器。
/// </summary>
internal static class ManagedUiLayout
{
    public static ManagedUiDocument Load(
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
        List<ManagedUiControl> controls = new(Math.Min(maxControls, 16));
        CollectControls(root, controls, maxControls);
        return new ManagedUiDocument(handle, source, title, [.. controls]);
    }

    private static void CollectControls(XElement element, List<ManagedUiControl> controls, int maxControls)
    {
        if (controls.Count >= maxControls)
        {
            throw new InvalidDataException("UI 文档控件数量超过 ManagedFallbackBackend 容量。");
        }

        ManagedUiControl? control = TryCreateControl(element);
        if (control is not null)
        {
            controls.Add(control);
        }

        foreach (XElement child in element.Elements())
        {
            CollectControls(child, controls, maxControls);
        }
    }

    private static ManagedUiControl? TryCreateControl(XElement element)
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
            bool initial = TryReadBoolean(ReadAttribute(element, "checked"), out bool parsed) && parsed;
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Checkbox,
                Id = id,
                Text = ReadText(element),
                Element = new UiElementId(UiStableId.Hash(id)),
                Action = new UiActionId(UiStableId.Hash(ReadAttribute(element, "data-event-change") ?? id)),
                Path = new UiPathId(UiStableId.Hash(path)),
                Value = UiValue.FromBoolean(initial),
            };
        }

        if (StringEquals(name, "progress"))
        {
            string id = ReadId(element, ReadText(element));
            string path = ReadAttribute(element, "data-model") ?? ReadAttribute(element, "path") ?? id;
            double value = TryReadDouble(ReadAttribute(element, "value"), out double parsed) ? parsed : 0.0;
            return new ManagedUiControl
            {
                Kind = ManagedUiControlKind.Progress,
                Id = id,
                Text = ReadText(element),
                Element = new UiElementId(UiStableId.Hash(id)),
                Path = new UiPathId(UiStableId.Hash(path)),
                Value = new UiValue(value),
            };
        }

        return null;
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

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
