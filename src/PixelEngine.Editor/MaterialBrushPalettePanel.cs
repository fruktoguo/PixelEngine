using Hexa.NET.ImGui;
using PixelEngine.Simulation;
using System.Numerics;

namespace PixelEngine.Editor;

/// <summary>
/// 材质调色板与世界画刷面板。
/// </summary>
[EditorUiSurface("editor.panel.brush")]
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
    /// <param name="inspectApi">可选的驻留状态检视 API；省略时从 edit API 自动解析。</param>
    public MaterialBrushPalettePanel(
        MaterialTable materials,
        ISimulationEditApi editApi,
        ISimulationInspectApi? inspectApi = null)
    {
        ArgumentNullException.ThrowIfNull(materials);
        _entries = BuildEntries(materials);
        _names = Array.ConvertAll(_entries, static entry => $"{entry.Id}: {entry.Name}");
        _applicator = new MaterialBrushApplicator(editApi, inspectApi: inspectApi);
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
    /// 当前参数 UI 是否由 Scene View overlay 承载，而不是作为独立 Editor 窗口绘制。
    /// </summary>
    public bool IsSceneHosted { get; private set; }

    /// <summary>
    /// 当前是否显式激活 Scene View 世界画刷。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 最近一次应用中因 chunk 未驻留而跳过的 cell 数。
    /// </summary>
    public int LastSkippedNonResidentCells { get; private set; }

    /// <summary>
    /// 最近一次应用中因超出 authoring 世界边界而跳过的 cell 数。
    /// </summary>
    public int LastSkippedOutOfBoundsCells { get; private set; }

    /// <summary>
    /// 最近一次工具状态或应用结果说明。
    /// </summary>
    public string Status { get; private set; } = "Scene 画刷未启用。";

    /// <summary>
    /// 当前画刷设置。
    /// </summary>
    public MaterialBrushSettings Settings { get; }

    /// <summary>
    /// 调色板条目。
    /// </summary>
    public ReadOnlySpan<MaterialPaletteEntry> Entries => _entries;

    /// <summary>
    /// 显式切换 Scene View 世界画刷；激活时同步显示参数面板。
    /// </summary>
    public void SetActive(bool active)
    {
        IsActive = active;
        if (active)
        {
            Visible = true;
        }

        Status = active ? "Scene 画刷已启用；左键绘制，W/E/R 返回对象工具。" : "Scene 画刷未启用。";
    }

    /// <summary>
    /// 把本面板切换为 Scene View 内嵌承载；Window 菜单仍通过 <see cref="Visible"/> 控制 overlay 显隐。
    /// </summary>
    public void HostInSceneView()
    {
        IsSceneHosted = true;
    }

    /// <summary>
    /// 在指定世界坐标应用当前画刷。
    /// </summary>
    public int ApplyAt(int worldX, int worldY)
    {
        return ApplyAt(worldX, worldY, MaterialBrushBounds.Unbounded);
    }

    /// <summary>
    /// 在指定世界坐标与 authoring 边界内应用当前画刷。
    /// </summary>
    public int ApplyAt(int worldX, int worldY, MaterialBrushBounds bounds)
    {
        if (!IsActive)
        {
            LastSkippedNonResidentCells = 0;
            LastSkippedOutOfBoundsCells = 0;
            return 0;
        }

        int writes = _applicator.ApplyAt(
            worldX,
            worldY,
            Settings,
            bounds,
            out int skippedNonResidentCells,
            out int skippedOutOfBoundsCells);
        LastSkippedNonResidentCells = skippedNonResidentCells;
        LastSkippedOutOfBoundsCells = skippedOutOfBoundsCells;
        Status = skippedNonResidentCells == 0 && skippedOutOfBoundsCells == 0
            ? $"已应用 {writes} 个 cell。"
            : $"已应用 {writes} 个 cell；跳过 {skippedNonResidentCells} 个未驻留、{skippedOutOfBoundsCells} 个越界 cell。";
        return writes;
    }

    /// <inheritdoc />
    [EditorUiCommands("panel.brush")]
    public void Draw(in EditorContext context)
    {
        _ = context;
        if (IsSceneHosted)
        {
            return;
        }

        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawContents();
        ImGui.End();
    }

    /// <summary>
    /// 绘制不含顶层窗口 chrome 的画刷参数内容，供 Scene View overlay 复用。
    /// </summary>
    [EditorUiCommands(
        "panel.brush.active",
        "panel.brush.radius",
        "panel.brush.strength")]
    public void DrawContents()
    {
        bool active = IsActive;
        if (ImGui.Checkbox("启用 Scene 画刷", ref active))
        {
            SetActive(active);
        }

        ImGui.SameLine();
        ImGui.TextDisabled(IsActive ? "B · 左键/拖动绘制" : "选择 Brush 或按 B 后绘制");
        ImGui.Separator();
        DrawToolSelector();
        DrawShapeSelector();
        DrawSizeControls();

        float probability = Settings.Probability;
        if (ImGui.SliderFloat("概率", ref probability, 0f, 1f))
        {
            Settings.Probability = probability;
        }

        DrawMaterialSelector();
        DrawTemperatureControls();
        if (LastSkippedNonResidentCells != 0 || LastSkippedOutOfBoundsCells != 0)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.70f, 0.25f, 1f), Status);
        }
        else
        {
            ImGui.TextDisabled(Status);
        }
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

    [EditorUiCommands("panel.brush.tool")]
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

    [EditorUiCommands("panel.brush.shape")]
    private void DrawShapeSelector()
    {
        int shape = (int)Settings.Shape;
        _ = ImGui.RadioButton("点", ref shape, (int)EditorBrushShape.Point);
        ImGui.SameLine();
        _ = ImGui.RadioButton("椭圆", ref shape, (int)EditorBrushShape.Circle);
        ImGui.SameLine();
        _ = ImGui.RadioButton("矩形", ref shape, (int)EditorBrushShape.Square);
        Settings.Shape = (EditorBrushShape)shape;
    }

    [EditorUiControlPrimitive]
    private void DrawSizeControls()
    {
        bool locked = Settings.LockAspectRatio;
        if (ImGui.Checkbox("锁定横纵比例", ref locked))
        {
            Settings.LockAspectRatio = locked;
            if (locked)
            {
                Settings.RadiusY = Settings.RadiusX;
            }
        }

        bool point = Settings.Shape == EditorBrushShape.Point;
        if (point)
        {
            ImGui.BeginDisabled();
        }

        int radiusX = Settings.RadiusX;
        if (ImGui.SliderInt("横向半径", ref radiusX, 0, 128))
        {
            Settings.RadiusX = radiusX;
            if (Settings.LockAspectRatio)
            {
                Settings.RadiusY = radiusX;
            }
        }

        int radiusY = Settings.RadiusY;
        if (ImGui.SliderInt("纵向半径", ref radiusY, 0, 128))
        {
            Settings.RadiusY = radiusY;
            if (Settings.LockAspectRatio)
            {
                Settings.RadiusX = radiusY;
            }
        }

        if (point)
        {
            ImGui.EndDisabled();
        }

        int width = point ? 1 : (Settings.ClampedRadiusX * 2) + 1;
        int height = point ? 1 : (Settings.ClampedRadiusY * 2) + 1;
        ImGui.TextDisabled($"{width} x {height} cells");
    }

    [EditorUiCommands("panel.brush.material")]
    private void DrawMaterialSelector()
    {
        if (_entries.Length == 0)
        {
            ImGui.TextUnformatted("无可用材质");
            return;
        }

        int settingsIndex = IndexOfMaterial(Settings.MaterialId);
        _selectedIndex = settingsIndex >= 0
            ? settingsIndex
            : Math.Clamp(_selectedIndex, 0, _entries.Length - 1);
        _ = ImGui.Combo("材质", ref _selectedIndex, _names, _names.Length);
        MaterialPaletteEntry selected = _entries[_selectedIndex];
        Settings.MaterialId = selected.Id;
        _ = ImGui.ColorButton("材质色", BgraToVector4(selected.BaseColorBgra), ImGuiColorEditFlags.NoTooltip, new Vector2(18f, 18f));
        ImGui.SameLine();
        ImGui.TextUnformatted(selected.Name);
    }

    private int IndexOfMaterial(ushort materialId)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Id == materialId)
            {
                return i;
            }
        }

        return -1;
    }

    [EditorUiCommands("panel.brush.temperature")]
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
