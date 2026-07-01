using Hexa.NET.ImGui;
using PixelEngine.Simulation;
using System.Numerics;

namespace PixelEngine.Editor;

/// <summary>
/// 材质调色板与世界画刷面板。
/// </summary>
public sealed class MaterialBrushPalettePanel : IEditorPanel
{
    private readonly MaterialPaletteEntry[] _entries;
    private readonly string[] _names;
    private readonly MaterialBrushApplicator _applicator;
    private int _selectedIndex;

    /// <summary>
    /// 创建面板。
    /// </summary>
    /// <param name="materials">材质表。</param>
    /// <param name="editApi">Simulation 编辑 API。</param>
    public MaterialBrushPalettePanel(MaterialTable materials, ISimulationEditApi editApi)
    {
        ArgumentNullException.ThrowIfNull(materials);
        _entries = BuildEntries(materials);
        _names = Array.ConvertAll(_entries, static entry => $"{entry.Id}: {entry.Name}");
        _applicator = new MaterialBrushApplicator(editApi);
        Settings = new MaterialBrushSettings();
        if (_entries.Length != 0)
        {
            Settings.MaterialId = _entries[0].Id;
        }
    }

    /// <inheritdoc />
    public string Title => EditorDockSpace.MaterialBrushWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 当前画刷设置。
    /// </summary>
    public MaterialBrushSettings Settings { get; }

    /// <summary>
    /// 调色板条目。
    /// </summary>
    public ReadOnlySpan<MaterialPaletteEntry> Entries => _entries;

    /// <summary>
    /// 在指定世界坐标应用当前画刷。
    /// </summary>
    public int ApplyAt(int worldX, int worldY)
    {
        return _applicator.ApplyAt(worldX, worldY, Settings);
    }

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawToolSelector();
        DrawShapeSelector();
        int radius = Settings.Radius;
        if (ImGui.SliderInt("半径", ref radius, 0, 64))
        {
            Settings.Radius = radius;
        }

        float probability = Settings.Probability;
        if (ImGui.SliderFloat("概率", ref probability, 0f, 1f))
        {
            Settings.Probability = probability;
        }

        DrawMaterialSelector();
        DrawTemperatureControls();
        ImGui.End();
    }

    private static MaterialPaletteEntry[] BuildEntries(MaterialTable materials)
    {
        List<MaterialPaletteEntry> entries = [];
        for (int i = 0; i < materials.Count; i++)
        {
            ushort id = (ushort)i;
            if (materials.IsTombstone(id))
            {
                continue;
            }

            ref readonly MaterialDef def = ref materials.Get(id);
            entries.Add(new MaterialPaletteEntry(id, def.Name, def.BaseColorBGRA));
        }

        return [.. entries];
    }

    private void DrawToolSelector()
    {
        int tool = (int)Settings.Tool;
        _ = ImGui.RadioButton("画", ref tool, (int)EditorBrushTool.Paint);
        ImGui.SameLine();
        _ = ImGui.RadioButton("挖", ref tool, (int)EditorBrushTool.Dig);
        ImGui.SameLine();
        _ = ImGui.RadioButton("擦", ref tool, (int)EditorBrushTool.Erase);
        ImGui.SameLine();
        _ = ImGui.RadioButton("温度", ref tool, (int)EditorBrushTool.Temperature);
        Settings.Tool = (EditorBrushTool)tool;
    }

    private void DrawShapeSelector()
    {
        int shape = (int)Settings.Shape;
        _ = ImGui.RadioButton("点", ref shape, (int)EditorBrushShape.Point);
        ImGui.SameLine();
        _ = ImGui.RadioButton("圆", ref shape, (int)EditorBrushShape.Circle);
        ImGui.SameLine();
        _ = ImGui.RadioButton("方", ref shape, (int)EditorBrushShape.Square);
        Settings.Shape = (EditorBrushShape)shape;
    }

    private void DrawMaterialSelector()
    {
        if (_entries.Length == 0)
        {
            ImGui.TextUnformatted("无可用材质");
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _entries.Length - 1);
        _ = ImGui.Combo("材质", ref _selectedIndex, _names, _names.Length);
        MaterialPaletteEntry selected = _entries[_selectedIndex];
        Settings.MaterialId = selected.Id;
        _ = ImGui.ColorButton("材质色", BgraToVector4(selected.BaseColorBgra), ImGuiColorEditFlags.NoTooltip, new Vector2(18f, 18f));
        ImGui.SameLine();
        ImGui.TextUnformatted(selected.Name);
    }

    private void DrawTemperatureControls()
    {
        int mode = (int)Settings.TemperatureMode;
        _ = ImGui.RadioButton("增量温度", ref mode, (int)TemperatureBrushMode.Additive);
        ImGui.SameLine();
        _ = ImGui.RadioButton("目标温度", ref mode, (int)TemperatureBrushMode.Target);
        Settings.TemperatureMode = (TemperatureBrushMode)mode;
        float temperature = Settings.TemperatureCelsius;
        if (ImGui.SliderFloat("温度 C", ref temperature, -200f, 2000f))
        {
            Settings.TemperatureCelsius = temperature;
        }
    }

    private static Vector4 BgraToVector4(uint bgra)
    {
        float b = (bgra & 0xFF) / 255f;
        float g = ((bgra >> 8) & 0xFF) / 255f;
        float r = ((bgra >> 16) & 0xFF) / 255f;
        float a = ((bgra >> 24) & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }
}
