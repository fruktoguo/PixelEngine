using PixelEngine.Physics;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// Editor 世界编辑工具测试。
/// 不变式：世界编辑工具写入经 Simulation 安全窗口。
/// </summary>
public sealed class EditorWorldToolsTests
{
    /// <summary>
    /// 验证圆形材质画刷只写入半径内 cell。
    /// </summary>
    [Fact]
    public void BrushApplicatorPaintsCircleMask()
    {
        // Arrange：准备输入与初始状态
        RecordingEditApi edit = new();
        MaterialBrushApplicator applicator = new(edit);
        MaterialBrushSettings settings = new()
        {
            Tool = EditorBrushTool.Paint,
            Shape = EditorBrushShape.Circle,
            Radius = 1,
            MaterialId = 2,
        };

        int writes = applicator.ApplyAt(10, 20, settings);

        // Assert：验证预期结果
        Assert.Equal(5, writes);
        Assert.Contains((10, 20, (ushort)2), edit.Painted);
        Assert.DoesNotContain((9, 19, (ushort)2), edit.Painted);
    }

    /// <summary>
    /// 验证横纵半径可独立形成椭圆与矩形 footprint，旧 Radius 写入仍保持等轴兼容。
    /// </summary>
    [Fact]
    public void BrushApplicatorSupportsIndependentHorizontalAndVerticalRadii()
    {
        RecordingEditApi edit = new();
        MaterialBrushApplicator applicator = new(edit);
        MaterialBrushSettings ellipse = new()
        {
            Tool = EditorBrushTool.Paint,
            Shape = EditorBrushShape.Circle,
            RadiusX = 2,
            RadiusY = 1,
            LockAspectRatio = false,
            MaterialId = 3,
        };

        int ellipseWrites = applicator.ApplyAt(10, 20, ellipse);

        Assert.Equal(7, ellipseWrites);
        Assert.Contains((8, 20, (ushort)3), edit.Painted);
        Assert.Contains((10, 19, (ushort)3), edit.Painted);
        Assert.DoesNotContain((9, 19, (ushort)3), edit.Painted);

        MaterialBrushSettings rectangle = new()
        {
            Tool = EditorBrushTool.Erase,
            Shape = EditorBrushShape.Square,
            RadiusX = 3,
            RadiusY = 1,
            LockAspectRatio = false,
        };
        int rectangleWrites = applicator.ApplyAt(4, 5, rectangle);

        Assert.Equal(21, rectangleWrites);
        Assert.Equal([(1, 4, 7, 6)], edit.ClearedRects);

        rectangle.Radius = 2;
        Assert.Equal(2, rectangle.RadiusX);
        Assert.Equal(2, rectangle.RadiusY);
    }

    /// <summary>
    /// 验证概率为 0 时不会写世界。
    /// </summary>
    [Fact]
    public void BrushApplicatorHonorsZeroProbability()
    {
        RecordingEditApi edit = new();
        MaterialBrushApplicator applicator = new(edit);
        MaterialBrushSettings settings = new()
        {
            Tool = EditorBrushTool.Erase,
            Shape = EditorBrushShape.Square,
            Radius = 4,
            Probability = 0f,
        };

        int writes = applicator.ApplyAt(0, 0, settings);

        Assert.Equal(0, writes);
        Assert.Empty(edit.Cleared);
    }

    /// <summary>
    /// 验证满概率方形材质画刷走批量矩形写入。
    /// </summary>
    [Fact]
    public void BrushApplicatorUsesBulkRectForFullProbabilitySquare()
    {
        // Arrange：准备输入与初始状态
        RecordingEditApi edit = new();
        MaterialBrushApplicator applicator = new(edit);
        MaterialBrushSettings settings = new()
        {
            Tool = EditorBrushTool.Paint,
            Shape = EditorBrushShape.Square,
            Radius = 2,
            MaterialId = 4,
        };

        int writes = applicator.ApplyAt(10, 20, settings);

        // Assert：验证预期结果
        Assert.Equal(25, writes);
        Assert.Equal([(8, 18, 12, 22, 4)], edit.PaintedRects);
        Assert.Empty(edit.Painted);
    }

    /// <summary>
    /// 验证圆形画刷跨出驻留 chunk 时只跳过不可编辑 cell，不把输入错误升级为 Editor 崩溃。
    /// </summary>
    [Fact]
    public void BrushApplicatorSkipsWhollyNonResidentFootprintWithoutThrowing()
    {
        TestChunkSource chunks = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk chunk);
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
        SimulationEditApi edit = new(kernel, materials);
        MaterialBrushApplicator applicator = new(edit, inspectApi: edit);
        MaterialBrushSettings settings = new()
        {
            Tool = EditorBrushTool.Paint,
            Shape = EditorBrushShape.Circle,
            Radius = 1,
            MaterialId = 1,
        };

        int writes = applicator.ApplyAt(130, 10, settings, out int skipped);

        Assert.Equal(0, writes);
        Assert.Equal(5, skipped);
        Assert.All(chunk.MaterialBuffer, static material => Assert.Equal(0, material));
    }

    /// <summary>
    /// 验证满概率方形画刷跨 chunk 时仍按驻留子矩形走批量路径，并精确报告跳过区域。
    /// </summary>
    [Fact]
    public void BrushApplicatorSplitsBulkRectAtResidentChunkBoundary()
    {
        TestChunkSource chunks = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk chunk);
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
        SimulationEditApi edit = new(kernel, materials);
        MaterialBrushApplicator applicator = new(edit, inspectApi: edit);
        MaterialBrushSettings settings = new()
        {
            Tool = EditorBrushTool.Paint,
            Shape = EditorBrushShape.Square,
            Radius = 1,
            MaterialId = 1,
        };

        int writes = applicator.ApplyAt(
            63,
            10,
            settings,
            new MaterialBrushBounds(0, 0, 63, 63),
            out int skippedNonResident,
            out int skippedOutOfBounds);

        Assert.Equal(6, writes);
        Assert.Equal(0, skippedNonResident);
        Assert.Equal(3, skippedOutOfBounds);
        for (int y = 9; y <= 11; y++)
        {
            Assert.Equal(1, chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(62, y)]);
            Assert.Equal(1, chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(63, y)]);
        }
    }

    /// <summary>
    /// 验证温度目标模式通过编辑 API 写入目标温度。
    /// </summary>
    [Fact]
    public void BrushApplicatorCanSetTemperatureTarget()
    {
        RecordingEditApi edit = new();
        MaterialBrushApplicator applicator = new(edit);
        MaterialBrushSettings settings = new()
        {
            Tool = EditorBrushTool.Temperature,
            Shape = EditorBrushShape.Point,
            TemperatureMode = TemperatureBrushMode.Target,
            TemperatureCelsius = 650f,
        };

        int writes = applicator.ApplyAt(4, 5, settings);

        Assert.Equal(1, writes);
        Assert.Equal([(4, 5, 650f)], edit.SetTemperatures);
    }

    /// <summary>
    /// 验证世界检视器读取材质、温度、flag、dirty 与 chunk 状态。
    /// </summary>
    [Fact]
    public void SimulationEditApiReadsCellChunkAndBodySnapshot()
    {
        // Arrange：准备输入与初始状态
        Chunk chunk = new(new ChunkCoord(0, 0));
        int local = CellAddressing.LocalIndexFromLocal(3, 4);
        chunk.MaterialBuffer[local] = 1;
        chunk.FlagsBuffer[local] = CellFlags.Burning | CellFlags.RigidOwned | CellFlags.Parity;
        chunk.Parity = CellFlags.Parity;
        chunk.SetCurrentDirty(new DirtyRect(1, 2, 5, 6));
        TestChunkSource chunks = new(chunk);
        TemperatureField temperature = new();
        temperature.AddHeat(3, 4, 125f);
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
        RigidStampRegistry registry = new();
        registry.Register(3, 4, new RigidStamp(7, 1, 2, 1));
        SimulationEditApi source = new(kernel, materials, temperature, registry);

        bool found = source.TryInspectCell(3, 4, out SimulationCellInspection inspection);

        // Assert：验证预期结果
        Assert.True(found);
        Assert.Equal(1, inspection.MaterialId);
        Assert.Equal("sand", inspection.MaterialName);
        Assert.Equal(125f, inspection.TemperatureCelsius);
        Assert.True(inspection.TemperatureAvailable);
        Assert.True(inspection.Flags.Burning);
        Assert.True(inspection.Flags.RigidOwned);
        Assert.True(inspection.Flags.Parity);
        Assert.Equal(7, inspection.BodyId);
        Assert.Equal(new DirtyRect(1, 2, 5, 6), inspection.CurrentDirty);
        Assert.Equal(ChunkState.Awake, inspection.ChunkState);
    }

    /// <summary>
    /// 验证世界检视器可跟随跨面板 cell 选择刷新。
    /// </summary>
    [Fact]
    public void WorldInspectorPanelRefreshesFromSelection()
    {
        // Arrange：准备输入与初始状态
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(2, 3)] = 1;
        TestChunkSource chunks = new(chunk);
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
        WorldInspectorPanel panel = new(new SimulationEditApi(kernel, materials));
        EditorSelection selection = new();
        selection.SelectCell(2, 3);

        bool found = panel.RefreshFromSelection(selection);

        // Assert：验证预期结果
        Assert.True(found);
        Assert.True(panel.LastInspection.HasValue);
        SimulationCellInspection inspection = panel.LastInspection.Value;
        Assert.Equal(1, inspection.MaterialId);
        Assert.Equal("sand", inspection.MaterialName);
    }

    /// <summary>验证跟随选择消失时清除陈旧结果，锁定状态可完整恢复。</summary>
    [Fact]
    public void WorldInspectorPanelStateIsReversibleAndClearsStaleSelection()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(2, 3)] = 1;
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(new TestChunkSource(chunk), new MaterialPropsTable(materials.Hot));
        WorldInspectorPanel panel = new(new SimulationEditApi(kernel, materials));
        EditorSelection selection = new();
        selection.SelectCell(2, 3);
        panel.ApplyState(followSelection: true, worldX: 10, worldY: 20, selection);
        WorldInspectorPanelState before = panel.CaptureState();

        panel.ApplyState(followSelection: false, worldX: 100, worldY: 100, selection);
        Assert.False(panel.FollowMouse);
        Assert.Null(panel.LastInspection);

        panel.RestoreState(before);
        Assert.True(panel.StateEquals(before));
        Assert.True(panel.FollowMouse);
        Assert.True(panel.LastInspection.HasValue);
        Assert.Equal((ushort)1, panel.LastInspection.Value.MaterialId);

        selection.Clear();
        Assert.False(panel.RefreshFromSelection(selection));
        Assert.Null(panel.LastInspection);
    }

    /// <summary>
    /// 验证 Simulation 编辑 API 的 phase [1] 写入会标记 current dirty 与边界邻居，而不是 working dirty。
    /// </summary>
    [Fact]
    public void SimulationEditApiPaintsAtInputPhaseAndMarksCurrentDirty()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource chunks = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk east = chunks.GetRequired(new ChunkCoord(1, 0));
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
        SimulationEditApi edit = new(kernel, materials);

        edit.PaintCell(63, 10, 1);

        // Assert：验证预期结果
        Assert.Equal(1, center.MaterialBuffer[CellAddressing.LocalIndexFromLocal(63, 10)]);
        Assert.Equal(new DirtyRect(61, 8, 63, 12), center.CurrentDirty);
        Assert.Equal(new DirtyRect(0, 8, 1, 12), east.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, center.WorkingDirty);
        Assert.Equal(DirtyRect.Empty, east.WorkingDirty);
    }

    /// <summary>
    /// 验证温度笔刷走粗温度场并唤醒 current dirty。
    /// </summary>
    [Fact]
    public void SimulationEditApiTemperatureBrushWritesFieldAndMarksCurrentDirty()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));
        TestChunkSource chunks = new(chunk);
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
        TemperatureField temperature = new();
        SimulationEditApi edit = new(kernel, materials, temperature);

        edit.SetTemperature(8, 9, 320f);

        Assert.Equal(320f, temperature.GetTemperature(8, 9));
        Assert.Equal(new DirtyRect(6, 7, 10, 11), chunk.CurrentDirty);
    }

    /// <summary>
    /// 验证调色板面板从材质表生成条目并能应用当前画刷。
    /// </summary>
    [Fact]
    public void MaterialBrushPalettePanelBuildsEntriesAndAppliesBrush()
    {
        RecordingEditApi edit = new();
        MaterialBrushPalettePanel panel = new(CreateMaterials(), edit);

        panel.Settings.Shape = EditorBrushShape.Point;
        panel.Settings.MaterialId = 1;
        Assert.False(panel.IsActive);
        Assert.Equal(0, panel.ApplyAt(8, 9));
        Assert.Empty(edit.Painted);

        panel.SetActive(true);
        int writes = panel.ApplyAt(8, 9);

        Assert.Equal(2, panel.Entries.Length);
        Assert.True(panel.IsActive);
        Assert.Equal(1, writes);
        List<(int X, int Y, ushort Material)> expected = [(8, 9, 1)];
        Assert.Equal(expected, edit.Painted);
    }

    /// <summary>
    /// 验证画刷参数可切换为 Scene View 内嵌承载，同时仍保留 Window 菜单显隐状态。
    /// </summary>
    [Fact]
    public void MaterialBrushPalettePanelCanBeHostedInsideSceneView()
    {
        MaterialBrushPalettePanel panel = new(CreateMaterials(), new RecordingEditApi());

        panel.HostInSceneView();
        panel.Visible = false;

        Assert.True(panel.IsSceneHosted);
        Assert.False(panel.Visible);
    }

    private static MaterialTable CreateMaterials()
    {
        return new MaterialTable(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1f, TextureId = -1 },
            new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder, HeatCapacity = 1f, TextureId = -1, BaseColorBGRA = 0xFF40C0FFu },
        ]);
    }

    private sealed class RecordingEditApi : ISimulationEditApi
    {
        public List<(int X, int Y, ushort Material)> Painted { get; } = [];

        public List<(int MinX, int MinY, int MaxX, int MaxY, ushort Material)> PaintedRects { get; } = [];

        public List<(int X, int Y)> Cleared { get; } = [];

        public List<(int MinX, int MinY, int MaxX, int MaxY)> ClearedRects { get; } = [];

        public List<(int X, int Y, float Temperature)> AddedTemperatures { get; } = [];

        public List<(int X, int Y, float Temperature)> SetTemperatures { get; } = [];

        public void PaintCell(int worldX, int worldY, ushort material)
        {
            Painted.Add((worldX, worldY, material));
        }

        public int PaintRect(int minX, int minY, int maxX, int maxY, ushort material)
        {
            PaintedRects.Add((minX, minY, maxX, maxY, material));
            return (maxX - minX + 1) * (maxY - minY + 1);
        }

        public void ClearCell(int worldX, int worldY)
        {
            Cleared.Add((worldX, worldY));
        }

        public int ClearRect(int minX, int minY, int maxX, int maxY)
        {
            ClearedRects.Add((minX, minY, maxX, maxY));
            return (maxX - minX + 1) * (maxY - minY + 1);
        }

        public void AddTemperature(int worldX, int worldY, float deltaCelsius)
        {
            AddedTemperatures.Add((worldX, worldY, deltaCelsius));
        }

        public void SetTemperature(int worldX, int worldY, float targetCelsius)
        {
            SetTemperatures.Add((worldX, worldY, targetCelsius));
        }
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord = chunks.ToDictionary(static chunk => chunk.Coord);

        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            return _byCoord.TryGetValue(coord, out chunk!);
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }

        public Chunk GetRequired(ChunkCoord coord)
        {
            return _byCoord[coord];
        }
    }

    private static TestChunkSource CreateNeighborhood(ChunkCoord centerCoord, out Chunk center)
    {
        Chunk[] chunks = new Chunk[9];
        int index = 0;
        center = null!;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Chunk chunk = new(new ChunkCoord(centerCoord.X + dx, centerCoord.Y + dy));
                chunks[index++] = chunk;
                if (dx == 0 && dy == 0)
                {
                    center = chunk;
                }
            }
        }

        return new TestChunkSource(chunks);
    }
}
