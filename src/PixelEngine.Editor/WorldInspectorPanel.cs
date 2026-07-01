using Hexa.NET.ImGui;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

/// <summary>
/// 世界 cell 检视器面板。
/// </summary>
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
        return selection.CellX is int x && selection.CellY is int y && InspectAt(x, y);
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
