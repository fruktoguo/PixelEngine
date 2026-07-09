using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.World.Tests;

/// <summary>
/// Plan 07 世界激活区与驻留元数据测试。
/// 不变式：激活区元数据与 chunk 状态一致。
/// </summary>
public sealed class WorldResidencyTests
{
    /// <summary>
    /// 验证 ChunkRect 的闭区间语义、扩张和枚举顺序。
    /// </summary>
    [Fact]
    public void ChunkRectUsesClosedBoundsAndYMajorIteration()
    {
        ChunkRect rect = new(-1, 2, 1, 3);

        Assert.Equal(6, rect.Count);
        Assert.True(rect.Contains(new ChunkCoord(0, 2)));
        Assert.False(rect.Contains(new ChunkCoord(2, 2)));
        Assert.Equal(new ChunkRect(-2, 1, 2, 4), rect.Expand(1));
        Assert.Equal(
            [
                new ChunkCoord(-1, 2),
                new ChunkCoord(0, 2),
                new ChunkCoord(1, 2),
                new ChunkCoord(-1, 3),
                new ChunkCoord(0, 3),
                new ChunkCoord(1, 3),
            ],
            [.. rect.Iterate()]);
    }

    /// <summary>
    /// 验证相机焦点支持负世界坐标，并按 floor 语义映射 chunk。
    /// </summary>
    [Fact]
    public void WorldCameraMapsNegativeCellsToFloorChunk()
    {
        WorldCamera camera = new(focusX: -1, focusY: -65, viewportCellsX: 64, viewportCellsY: 64);

        Assert.Equal(new ChunkCoord(-1, -2), camera.FocusChunk);

        camera.SetFocus(64, 127);

        Assert.Equal(new ChunkCoord(1, 1), camera.FocusChunk);
    }

    /// <summary>
    /// 验证可见区、激活区和 border ring 的外扩关系。
    /// </summary>
    [Fact]
    public void ActivationPolicyComputesVisibleActiveAndBorderRects()
    {
        WorldCamera camera = new(focusX: 32, focusY: 32, viewportCellsX: 128, viewportCellsY: 128);
        WorldStreamingConfig config = new()
        {
            ActivationMarginChunks = 2,
            BorderRingWidth = 1,
        };
        ActivationPolicy policy = new();

        ChunkRect visible = policy.ComputeVisible(camera);
        ChunkRect active = policy.ComputeActive(camera, config);
        ChunkRect border = policy.ComputeBorder(active, config);

        Assert.Equal(new ChunkRect(-1, -1, 1, 1), visible);
        Assert.Equal(new ChunkRect(-3, -3, 3, 3), active);
        Assert.Equal(new ChunkRect(-4, -4, 4, 4), border);
        Assert.True(border.Contains(new ChunkCoord(-4, 0)));
        Assert.False(active.Contains(new ChunkCoord(-4, 0)));
    }

    /// <summary>
    /// 验证 ResidencyTable 可维护 state、touch 帧与 dirty 诊断元数据。
    /// </summary>
    [Fact]
    public void ResidencyTableTracksInfoTouchAndDirtyState()
    {
        ResidencyTable table = new();
        ChunkCoord coord = new(3, -2);
        ChunkResidencyInfo info = new(ChunkResidencyState.Border, LastTouchedFrame: 10, ResidentBytes: 20_480, DirtySinceLoad: false);

        table.Set(coord, info);
        Assert.True(table.TryGetInfo(coord, out ChunkResidencyInfo loaded));
        Assert.Equal(ChunkResidencyState.Border, loaded.State);
        Assert.True(table.Touch(coord, 14));
        Assert.True(table.MarkDirty(coord, 16));
        Assert.True(table.TryGetInfo(coord, out loaded));
        Assert.Equal(16, loaded.LastTouchedFrame);
        Assert.True(loaded.DirtySinceLoad);
        _ = Assert.Single(table.Entries());
        Assert.True(table.Remove(coord));
        Assert.Equal(0, table.Count);
        Assert.False(table.Touch(coord, 20));
    }
}
