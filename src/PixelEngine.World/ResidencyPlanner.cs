using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 驻留状态变更请求。
/// </summary>
public readonly record struct ResidencyStateChange(ChunkCoord Coord, ChunkResidencyState State);

/// <summary>
/// 相位 2 生成的驻留计划。
/// </summary>
public sealed class ResidencyPlan(
    IReadOnlyList<ChunkCoord> loadCoords,
    IReadOnlyList<ChunkCoord> unloadCoords,
    IReadOnlyList<ResidencyStateChange> stateChanges)
{
    /// <summary>
    /// 需要后台装载的 chunk 坐标。
    /// </summary>
    public IReadOnlyList<ChunkCoord> LoadCoords { get; } = loadCoords;

    /// <summary>
    /// 需要从 live map 摘下并提交后台卸载的 chunk 坐标。
    /// </summary>
    public IReadOnlyList<ChunkCoord> UnloadCoords { get; } = unloadCoords;

    /// <summary>
    /// 相位 2 可直接应用到 ResidencyTable 的状态变更。
    /// </summary>
    public IReadOnlyList<ResidencyStateChange> StateChanges { get; } = stateChanges;
}

/// <summary>
/// 根据 active/border 矩形与内存预算生成驻留增删计划。
/// </summary>
public sealed class ResidencyPlanner
{
    private readonly WorldStreamingConfig _config;

    /// <summary>
    /// 创建驻留规划器。
    /// </summary>
    public ResidencyPlanner(WorldStreamingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config.Validate();
    }

    /// <summary>
    /// 生成本帧驻留计划。该方法只做决策，不修改 live chunk map。
    /// </summary>
    public ResidencyPlan Plan(ChunkRect active, ChunkRect border, ResidencyTable table, ChunkMemoryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(budget);
        ValidateActiveInsideBorder(active, border);

        List<ChunkCoord> loads = [];
        List<ChunkCoord> unloads = [];
        List<ResidencyStateChange> stateChanges = [];
        int remainingOps = _config.MaxStreamOpsPerFrame;

        foreach (ChunkCoord coord in border.Iterate())
        {
            if (table.TryGetInfo(coord, out ChunkResidencyInfo info) && info.State != ChunkResidencyState.Detached)
            {
                ChunkResidencyState desired = active.Contains(coord) ? ChunkResidencyState.Active : ChunkResidencyState.Border;
                if (info.State != desired)
                {
                    stateChanges.Add(new ResidencyStateChange(coord, desired));
                }

                continue;
            }

            if (remainingOps == 0)
            {
                continue;
            }

            loads.Add(coord);
            remainingOps--;
        }

        foreach (KeyValuePair<ChunkCoord, ChunkResidencyInfo> entry in table.Entries())
        {
            if (border.Contains(entry.Key) || entry.Value.State is ChunkResidencyState.Cached or ChunkResidencyState.Detached)
            {
                continue;
            }

            stateChanges.Add(new ResidencyStateChange(entry.Key, ChunkResidencyState.Cached));
        }

        if (remainingOps > 0 && budget.OverCap)
        {
            IReadOnlyList<ChunkCoord> evictions = budget.SelectEvictions(table, border, budget.EvictionTargetBytes);
            for (int i = 0; i < evictions.Count && remainingOps > 0; i++)
            {
                unloads.Add(evictions[i]);
                stateChanges.Add(new ResidencyStateChange(evictions[i], ChunkResidencyState.Detached));
                remainingOps--;
            }
        }

        return new ResidencyPlan(loads, unloads, stateChanges);
    }

    private static void ValidateActiveInsideBorder(ChunkRect active, ChunkRect border)
    {
        if (active.IsEmpty)
        {
            return;
        }

        if (border.Contains(new ChunkCoord(active.MinCx, active.MinCy)) &&
            border.Contains(new ChunkCoord(active.MaxCx, active.MaxCy)))
        {
            return;
        }

        throw new ArgumentException("border 矩形必须完整包含 active 矩形。", nameof(border));
    }
}
