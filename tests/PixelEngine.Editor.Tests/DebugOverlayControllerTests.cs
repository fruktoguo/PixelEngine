using PixelEngine.Rendering;
using PixelEngine.Physics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 调试叠层控制器测试。
/// </summary>
public sealed class DebugOverlayControllerTests
{
    /// <summary>
    /// 验证矢量叠层可从各子系统只读快照生成 overlay command。
    /// </summary>
    [Fact]
    public void BuildVectorOverlaysEmitsEnabledOverlayCommands()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.SetCurrentDirty(new DirtyRect(1, 2, 3, 4));
        chunk.SetWorkingDirty(new DirtyRect(5, 6, 7, 8));
        TestChunkSource chunks = new(chunk);
        DebugOverlaySettings settings = new()
        {
            Enabled = DebugOverlayFlags.DirtyRects |
                DebugOverlayFlags.CaIterationRects |
                DebugOverlayFlags.ChunkGridParity |
                DebugOverlayFlags.KeepAliveHotspots |
                DebugOverlayFlags.ParticleTrails |
                DebugOverlayFlags.ConnectedComponents,
        };
        DebugOverlayController controller = new(settings);
        List<OverlayCommand> commands = [];
        CaIterationSnapshot[] caIterations = [new(new ChunkCoord(0, 0), new DirtyRect(9, 10, 11, 12))];
        BoundaryWakeSnapshot[] wakes = [new(new ChunkCoord(1, 0), 2, new DirtyRect(0, 1, 2, 3))];
        Particle[] particles = [new() { X = 4, Y = 5, Vx = 1, Vy = 2, Life = 9, Material = 1 }];
        ConnectedComponentDebugSnapshot[] components = [new(5, 3, 42, RectI.FromBounds(8, 9, 18, 20), IsFragment: false)];

        int written = controller.BuildVectorOverlays(
            chunks,
            CameraState.OneToOne(0, 0, 128, 128),
            caIterations,
            wakes,
            particles,
            components,
            commands);

        Assert.Equal(commands.Count, written);
        Assert.Equal(7, commands.Count);
        Assert.Contains(commands, static command => command.PrimitiveType == OverlayPrimitiveType.SolidRectangle);
        Assert.Contains(commands, static command => command.PrimitiveType == OverlayPrimitiveType.OutlineRectangle && command.ViewportX == 1f && command.ViewportY == 2f);
        Assert.Contains(commands, static command => command.PrimitiveType == OverlayPrimitiveType.OutlineRectangle && command.ViewportX == 9f && command.ViewportY == 10f);
        Assert.Contains(commands, static command => command.PrimitiveType == OverlayPrimitiveType.Line);
        Assert.All(commands, static command => command.Validate());
    }

    /// <summary>
    /// 验证逐 cell debug 着色按 owned、温度、parity 优先级输出。
    /// </summary>
    [Fact]
    public void DebugCellColorProviderColorsOwnedTemperatureAndParity()
    {
        DebugOverlaySettings settings = new()
        {
            Enabled = DebugOverlayFlags.OwnedByBody | DebugOverlayFlags.TemperatureHeatmap | DebugOverlayFlags.CellParity,
        };
        RecordingOwnership ownership = new(2, 3, 42);
        DebugOverlayController controller = new(settings, ownership);

        Assert.True(controller.TryGetDebugColor(2, 3, 1, CellFlags.RigidOwned, 0f, out uint owned));
        Assert.True(controller.TryGetDebugColor(0, 0, 1, 0, 500f, out uint hot));
        Assert.True(controller.TryGetDebugColor(0, 0, 1, CellFlags.Parity, 0f, out uint parity));
        Assert.NotEqual(owned, hot);
        Assert.NotEqual(hot, parity);
        Assert.False(controller.TryGetDebugColor(0, 0, 0, 0, 0f, out _));
    }

    private sealed class RecordingOwnership(int x, int y, int bodyId) : IRigidCellOwnershipLookup
    {
        public bool TryGetBodyAtCell(int worldX, int worldY, out int bodyKey)
        {
            if (worldX == x && worldY == y)
            {
                bodyKey = bodyId;
                return true;
            }

            bodyKey = 0;
            return false;
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
    }
}
