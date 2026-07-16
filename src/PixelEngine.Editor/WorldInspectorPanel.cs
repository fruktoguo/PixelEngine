using Hexa.NET.ImGui;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

/// <summary>
/// 世界 cell 检视器面板。
/// </summary>
[EditorUiSurface("editor.panel.world-inspector")]
public sealed class WorldInspectorPanel(ISimulationInspectApi source) : IEditorPanel
{
    private readonly ISimulationInspectApi _source = source ?? throw new ArgumentNullException(nameof(source));
    private int _x;
    private int _y;

    /// <inheritdoc />
    public string Title => EditorDockSpace.WorldInspectorWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 是否跟随跨面板 cell 选择；false 时使用锁定坐标。
    /// </summary>
    public bool FollowMouse { get; set; } = true;

    /// <summary>
    /// 最近一次检视结果。
    /// </summary>
    public SimulationCellInspection? LastInspection { get; private set; }

    /// <summary>捕获跟随模式、锁定坐标与最近检视结果。</summary>
    /// <returns>可供 Undo/Redo 恢复的完整面板状态。</returns>
    public WorldInspectorPanelState CaptureState()
    {
        return new WorldInspectorPanelState(FollowMouse, _x, _y, LastInspection);
    }

    /// <summary>判断当前面板状态是否仍与快照完全一致。</summary>
    /// <param name="state">待比较状态。</param>
    /// <returns>全部可观察字段一致时为 true。</returns>
    public bool StateEquals(WorldInspectorPanelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return FollowMouse == state.FollowSelection &&
            _x == state.WorldX &&
            _y == state.WorldY &&
            LastInspection == state.LastInspection;
    }

    /// <summary>恢复完整面板 before/after-image，不重新读取 simulation。</summary>
    /// <param name="state">目标状态。</param>
    public void RestoreState(WorldInspectorPanelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        FollowMouse = state.FollowSelection;
        _x = state.WorldX;
        _y = state.WorldY;
        LastInspection = state.LastInspection;
    }

    /// <summary>应用与人工 checkbox/坐标输入相同的跟随或锁定语义，并立即刷新结果。</summary>
    /// <param name="followSelection">是否跟随跨面板 cell 选择。</param>
    /// <param name="worldX">锁定模式的世界 X。</param>
    /// <param name="worldY">锁定模式的世界 Y。</param>
    /// <param name="selection">当前 Editor 跨面板选择。</param>
    public void ApplyState(
        bool followSelection,
        int worldX,
        int worldY,
        EditorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        _x = worldX;
        _y = worldY;
        if (followSelection)
        {
            FollowMouse = true;
            _ = RefreshFromSelection(selection);
            return;
        }

        LockCell(worldX, worldY);
        _ = InspectAt(worldX, worldY);
    }

    /// <summary>
    /// 锁定检视坐标。
    /// </summary>
    public void LockCell(int worldX, int worldY)
    {
        _x = worldX;
        _y = worldY;
        FollowMouse = false;
    }

    /// <summary>
    /// 刷新指定世界坐标的检视快照。
    /// </summary>
    public bool InspectAt(int worldX, int worldY)
    {
        if (_source.TryInspectCell(worldX, worldY, out SimulationCellInspection inspection))
        {
            LastInspection = inspection;
            return true;
        }

        LastInspection = null;
        return false;
    }

    /// <summary>
    /// 从跨面板选择态刷新检视结果。
    /// </summary>
    public bool RefreshFromSelection(EditorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.CellX is int x && selection.CellY is int y)
        {
            return InspectAt(x, y);
        }

        LastInspection = null;
        return false;
    }

    /// <inheritdoc />
    [EditorUiCommands(
        "panel.world-inspector",
        "panel.world-inspector.follow",
        "panel.world-inspector.lock",
        "panel.world-inspector.coordinates",
        "panel.world-inspector.inspect")]
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
        bool followMouse = FollowMouse;
        if (ImGui.Checkbox("跟随鼠标", ref followMouse))
        {
            FollowMouse = followMouse;
        }

        if (!FollowMouse)
        {
            _ = ImGui.InputInt("X", ref _x);
            _ = ImGui.InputInt("Y", ref _y);
            _ = InspectAt(_x, _y);
        }
        else
        {
            _ = RefreshFromSelection(context.Selection);
        }

        if (LastInspection is SimulationCellInspection inspection)
        {
            DrawInspection(inspection);
        }
        else
        {
            ImGui.TextUnformatted("未选中 cell");
        }

        ImGui.End();
    }

    private static void DrawInspection(SimulationCellInspection inspection)
    {
        ImGui.TextUnformatted($"World: {inspection.WorldX}, {inspection.WorldY}");
        ImGui.TextUnformatted($"Chunk: {inspection.ChunkCoord.X}, {inspection.ChunkCoord.Y} / Local: {inspection.LocalX}, {inspection.LocalY}");
        ImGui.TextUnformatted($"Material: {inspection.MaterialId} {inspection.MaterialName}");
        ImGui.TextUnformatted(inspection.TemperatureAvailable ? $"Temperature: {inspection.TemperatureCelsius:F1} C" : "Temperature: unavailable");
        ImGui.TextUnformatted($"Flags: 0x{inspection.Flags.Raw:X2} parity={inspection.Flags.Parity} settled={inspection.Flags.Settled} burning={inspection.Flags.Burning} freefall={inspection.Flags.FreeFalling} rigid={inspection.Flags.RigidOwned}");
        ImGui.TextUnformatted(inspection.BodyId is int bodyId ? $"Body: {bodyId}" : "Body: none");
        ImGui.TextUnformatted($"Dirty current={inspection.CurrentDirty} working={inspection.WorkingDirty}");
        ImGui.TextUnformatted($"Chunk state={inspection.ChunkState} parity={inspection.ChunkParity}");
    }
}

/// <summary>World Inspector 的完整可逆面板状态。</summary>
public sealed record WorldInspectorPanelState(
    bool FollowSelection,
    int WorldX,
    int WorldY,
    SimulationCellInspection? LastInspection);
