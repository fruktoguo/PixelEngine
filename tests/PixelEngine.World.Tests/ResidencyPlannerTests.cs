using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.World.Tests;

/// <summary>
/// Plan 07 驻留规划与内存预算测试。
/// </summary>
public sealed class ResidencyPlannerTests
{
    /// <summary>
    /// 验证 active/border 中缺失的 chunk 会进入装载计划，已有 chunk 会被重分类。
    /// </summary>
    [Fact]
    public void PlanLoadsMissingBorderAreaAndClassifiesExistingChunks()
    {
        ResidencyTable table = new();
        ChunkCoord activeCoord = new(0, 0);
        ChunkCoord borderCoord = new(-1, 0);
        table.Set(activeCoord, new ChunkResidencyInfo(ChunkResidencyState.Border, 1, ChunkMemoryBudget.EstimatedResidentChunkBytes, false));
        table.Set(borderCoord, new ChunkResidencyInfo(ChunkResidencyState.Cached, 1, ChunkMemoryBudget.EstimatedResidentChunkBytes, false));
        ResidencyPlanner planner = new(new WorldStreamingConfig { MaxStreamOpsPerFrame = 64 });

        ResidencyPlan plan = planner.Plan(
            active: new ChunkRect(0, 0, 0, 0),
            border: new ChunkRect(-1, -1, 1, 1),
            table,
            Budget());

        Assert.Contains(new ResidencyStateChange(activeCoord, ChunkResidencyState.Active), plan.StateChanges);
        Assert.Contains(new ResidencyStateChange(borderCoord, ChunkResidencyState.Border), plan.StateChanges);
        Assert.Contains(new ChunkCoord(-1, -1), plan.LoadCoords);
        Assert.DoesNotContain(activeCoord, plan.LoadCoords);
        Assert.DoesNotContain(borderCoord, plan.LoadCoords);
    }

    /// <summary>
    /// 验证单帧流式请求数会限制装载数量。
    /// </summary>
    [Fact]
    public void PlanHonorsMaxStreamOpsPerFrameForLoads()
    {
        ResidencyPlanner planner = new(new WorldStreamingConfig { MaxStreamOpsPerFrame = 3 });

        ResidencyPlan plan = planner.Plan(
            active: new ChunkRect(0, 0, 0, 0),
            border: new ChunkRect(-1, -1, 1, 1),
            new ResidencyTable(),
            Budget());

        Assert.Equal(3, plan.LoadCoords.Count);
        Assert.Empty(plan.UnloadCoords);
    }

    /// <summary>
    /// 验证预算超限时只选择 border 外 cached chunk 驱逐，active/border 不会被卸载。
    /// </summary>
    [Fact]
    public void BudgetEvictsOnlyCachedChunksOutsideBorderByLruDistance()
    {
        ResidencyTable table = new();
        table.Set(new ChunkCoord(0, 0), Info(ChunkResidencyState.Active, frame: 1));
        table.Set(new ChunkCoord(2, 0), Info(ChunkResidencyState.Border, frame: 1));
        table.Set(new ChunkCoord(5, 0), Info(ChunkResidencyState.Cached, frame: 10));
        table.Set(new ChunkCoord(8, 0), Info(ChunkResidencyState.Cached, frame: 1));
        table.Set(new ChunkCoord(-7, 0), Info(ChunkResidencyState.Cached, frame: 1));
        ChunkMemoryBudget budget = Budget(cap: ChunkMemoryBudget.EstimatedResidentChunkBytes * 3L, target: ChunkMemoryBudget.EstimatedResidentChunkBytes * 2L);
        for (int i = 0; i < 5; i++)
        {
            budget.Add(ChunkMemoryBudget.EstimatedResidentChunkBytes);
        }

        IReadOnlyList<ChunkCoord> evictions = budget.SelectEvictions(table, new ChunkRect(-2, -2, 2, 2), budget.EvictionTargetBytes);

        Assert.Equal([new ChunkCoord(8, 0), new ChunkCoord(-7, 0), new ChunkCoord(5, 0)], evictions);
        Assert.DoesNotContain(new ChunkCoord(0, 0), evictions);
        Assert.DoesNotContain(new ChunkCoord(2, 0), evictions);
    }

    /// <summary>
    /// 验证 planner 会把 border 外 resident 重分类为 Cached，并在预算超限时生成卸载计划。
    /// </summary>
    [Fact]
    public void PlanMarksOutsideResidentsCachedAndSubmitsBudgetEvictions()
    {
        ResidencyTable table = new();
        ChunkCoord outside = new(4, 0);
        ChunkCoord cached = new(5, 0);
        table.Set(outside, Info(ChunkResidencyState.Active, frame: 20));
        table.Set(cached, Info(ChunkResidencyState.Cached, frame: 1));
        ChunkMemoryBudget budget = Budget(cap: ChunkMemoryBudget.EstimatedResidentChunkBytes, target: 1);
        budget.Add(ChunkMemoryBudget.EstimatedResidentChunkBytes * 2);
        ResidencyPlanner planner = new(new WorldStreamingConfig { MaxStreamOpsPerFrame = 16 });

        ResidencyPlan plan = planner.Plan(
            active: new ChunkRect(0, 0, 0, 0),
            border: new ChunkRect(-1, -1, 1, 1),
            table,
            budget);

        Assert.Contains(new ResidencyStateChange(outside, ChunkResidencyState.Cached), plan.StateChanges);
        Assert.Equal([cached], plan.UnloadCoords);
        Assert.Contains(new ResidencyStateChange(cached, ChunkResidencyState.Detached), plan.StateChanges);
    }

    private static ChunkResidencyInfo Info(ChunkResidencyState state, long frame)
    {
        return new ChunkResidencyInfo(state, frame, ChunkMemoryBudget.EstimatedResidentChunkBytes, DirtySinceLoad: false);
    }

    private static ChunkMemoryBudget Budget(
        long cap = 512L * 1024 * 1024,
        long target = 448L * 1024 * 1024)
    {
        return new ChunkMemoryBudget(cap, target);
    }
}
