using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

/// <summary>
/// 编辑器材质图例只读预览，供作者核对材质可玩性与视觉辨识字段。
/// </summary>
public sealed class MaterialLegendPreview
{
    private MaterialLegendPreviewEntry[] _entries = [];

    /// <summary>
    /// 当前只读预览条目，按材质图例分类与材质 name 排序。
    /// </summary>
    public ReadOnlySpan<MaterialLegendPreviewEntry> Entries => _entries;

    /// <summary>
    /// 从材质编辑文档重建预览条目。
    /// </summary>
    public void Rebuild(MaterialReactionEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<MaterialLegendPreviewEntry> entries = new(document.Materials.Count);
        for (int i = 0; i < document.Materials.Count; i++)
        {
            MaterialEditorRow row = document.Materials[i];
            entries.Add(MaterialLegendPreviewEntry.FromRow(row));
        }

        entries.Sort(static (left, right) =>
        {
            int category = left.LegendCategory.CompareTo(right.LegendCategory);
            return category != 0 ? category : string.CompareOrdinal(left.Name, right.Name);
        });
        _entries = [.. entries];
    }

    /// <summary>
    /// 绘制只读预览；不会修改材质文档。
    /// </summary>
    public void Draw()
    {
        ImGui.SeparatorText("MaterialLegendPreview");
        if (_entries.Length == 0)
        {
            ImGui.TextUnformatted("无材质可预览");
            return;
        }

        MaterialLegendCategory? current = null;
        for (int i = 0; i < _entries.Length; i++)
        {
            MaterialLegendPreviewEntry entry = _entries[i];
            if (current != entry.LegendCategory)
            {
                current = entry.LegendCategory;
                ImGui.TextUnformatted(entry.LegendCategory.ToString());
            }

            _ = ImGui.ColorButton(
                $"##legend_base_{i}",
                BgraToVector4(entry.BaseColorBGRA, entry.Alpha),
                ImGuiColorEditFlags.NoTooltip,
                new Vector2(16f, 16f));
            ImGui.SameLine();
            _ = ImGui.ColorButton(
                $"##legend_outline_{i}",
                BgraToVector4(entry.OutlineColorBGRA, byte.MaxValue),
                ImGuiColorEditFlags.NoTooltip,
                new Vector2(10f, 16f));
            ImGui.SameLine();
            _ = ImGui.ColorButton(
                $"##legend_flow_{i}",
                BgraToVector4(entry.FlowTintBGRA, byte.MaxValue),
                ImGuiColorEditFlags.NoTooltip,
                new Vector2(10f, 16f));
            ImGui.SameLine();
            ImGui.TextUnformatted(
                $"{entry.DisplayName} ({entry.Name}) style={entry.RenderStyle} " +
                $"maxIntegrity={entry.MaxIntegrity} flowRate={entry.FlowRate} " +
                $"debris={entry.DebrisCount} mine={entry.MineYield}");
        }
    }

    private static Vector4 BgraToVector4(uint bgra, byte alphaOverride)
    {
        float b = (bgra & 0xFF) / 255f;
        float g = ((bgra >> 8) & 0xFF) / 255f;
        float r = ((bgra >> 16) & 0xFF) / 255f;
        float a = alphaOverride / 255f;
        return new Vector4(r, g, b, a);
    }
}

/// <summary>
/// 材质图例预览条目。
/// </summary>
public readonly record struct MaterialLegendPreviewEntry(
    string Name,
    string DisplayName,
    MaterialLegendCategory LegendCategory,
    MaterialRenderStyle RenderStyle,
    uint BaseColorBGRA,
    uint OutlineColorBGRA,
    byte Alpha,
    uint FlowTintBGRA,
    ushort MaxIntegrity,
    byte FlowRate,
    byte DebrisCount,
    byte MineYield,
    bool LegendVisible)
{
    /// <summary>
    /// 从编辑行创建只读预览条目。
    /// </summary>
    public static MaterialLegendPreviewEntry FromRow(MaterialEditorRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string name = string.IsNullOrWhiteSpace(row.Name) ? "<unnamed>" : row.Name.Trim();
        string displayName = string.IsNullOrWhiteSpace(row.DisplayName) ? name : row.DisplayName.Trim();
        return new MaterialLegendPreviewEntry(
            name,
            displayName,
            ParseEnum(row.LegendCategory, MaterialLegendCategory.Terrain),
            ParseEnum(row.RenderStyle, MaterialRenderStyle.Ground),
            row.BaseColor,
            row.OutlineColorBGRA,
            (byte)Math.Clamp(row.Alpha, byte.MinValue, byte.MaxValue),
            row.FlowTintBGRA,
            (ushort)Math.Clamp(row.MaxIntegrity, ushort.MinValue, ushort.MaxValue),
            (byte)Math.Clamp(row.FlowRate, byte.MinValue, PixelEngine.Core.EngineConstants.MoveCap),
            (byte)Math.Clamp(row.DebrisCount, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp(row.MineYield, byte.MinValue, byte.MaxValue),
            row.LegendVisible);
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return !string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value.Trim(), ignoreCase: true, out TEnum parsed)
            ? parsed
            : fallback;
    }
}
