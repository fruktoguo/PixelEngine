using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 驻留状态变更请求。
/// </summary>
public readonly record struct ResidencyStateChange(ChunkCoord Coord, ChunkResidencyState State);

/// <summary>
/// 相位 2 生成的驻留计划；由 <see cref="ResidencyPlanner" /> 复用，下一次 Plan 会覆盖内容。
/// </summary>
public sealed class ResidencyPlan
{
    private ChunkCoord[] _loadCoords = [];
    private ChunkCoord[] _unloadCoords = [];
    private ResidencyStateChange[] _stateChanges = [];

    /// <summary>
    /// 需要后台装载的 chunk 坐标。
    /// </summary>
    public ReadOnlySpan<ChunkCoord> LoadCoords => _loadCoords.AsSpan(0, LoadCount);

    /// <summary>
    /// 需要从 live map 摘下并提交后台卸载的 chunk 坐标。
    /// </summary>
    public ReadOnlySpan<ChunkCoord> UnloadCoords => _unloadCoords.AsSpan(0, UnloadCount);

    /// <summary>
    /// 相位 2 可直接应用到 ResidencyTable 的状态变更。
    /// </summary>
    public ReadOnlySpan<ResidencyStateChange> StateChanges => _stateChanges.AsSpan(0, StateChangeCount);

    /// <summary>
    /// 装载坐标数量。
    /// </summary>
    public int LoadCount { get; private set; }

    /// <summary>
    /// 卸载坐标数量。
    /// </summary>
    public int UnloadCount { get; private set; }

    /// <summary>
    /// 状态变更数量。
    /// </summary>
    public int StateChangeCount { get; private set; }

    /// <summary>
    /// 创建空驻留计划，供 planner 复用。
    /// </summary>
    public ResidencyPlan()
    {
    }

    /// <summary>
    /// 从调用方提供的 span 创建驻留计划；帧内规划应优先使用 <see cref="ResidencyPlanner" /> 复用实例。
    /// </summary>
    public ResidencyPlan(
        ReadOnlySpan<ChunkCoord> loadCoords,
        ReadOnlySpan<ChunkCoord> unloadCoords,
        ReadOnlySpan<ResidencyStateChange> stateChanges)
    {
        EnsureLoadCapacity(loadCoords.Length);
        loadCoords.CopyTo(_loadCoords);
        LoadCount = loadCoords.Length;

        EnsureUnloadCapacity(unloadCoords.Length);
        unloadCoords.CopyTo(_unloadCoords);
        UnloadCount = unloadCoords.Length;

        EnsureStateChangeCapacity(stateChanges.Length);
        stateChanges.CopyTo(_stateChanges);
        StateChangeCount = stateChanges.Length;
    }

    internal void Clear()
    {
        LoadCount = 0;
        UnloadCount = 0;
        StateChangeCount = 0;
    }

    internal void AddLoad(ChunkCoord coord)
    {
        EnsureLoadCapacity(LoadCount + 1);
        _loadCoords[LoadCount++] = coord;
    }

    internal void AddUnload(ChunkCoord coord)
    {
        EnsureUnloadCapacity(UnloadCount + 1);
        _unloadCoords[UnloadCount++] = coord;
    }

    internal void AddStateChange(ResidencyStateChange change)
    {
        EnsureStateChangeCapacity(StateChangeCount + 1);
        _stateChanges[StateChangeCount++] = change;
    }

    private void EnsureLoadCapacity(int required)
    {
        if (_loadCoords.Length < required)
        {
            Array.Resize(ref _loadCoords, NextCapacity(_loadCoords.Length, required));
        }
    }

    private void EnsureUnloadCapacity(int required)
    {
        if (_unloadCoords.Length < required)
        {
            Array.Resize(ref _unloadCoords, NextCapacity(_unloadCoords.Length, required));
        }
    }

    private void EnsureStateChangeCapacity(int required)
    {
        if (_stateChanges.Length < required)
        {
            Array.Resize(ref _stateChanges, NextCapacity(_stateChanges.Length, required));
        }
    }

    private static int NextCapacity(int current, int required)
    {
        int next = current == 0 ? 8 : current * 2;
        return Math.Max(next, required);
    }
}

/// <summary>
/// 根据 active/border 矩形与内存预算生成驻留增删计划。
/// </summary>
public sealed class ResidencyPlanner
{
    private readonly WorldStreamingConfig _config;
    private readonly ResidencyPlan _plan = new();

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

        _plan.Clear();
        int remainingOps = _config.MaxStreamOpsPerFrame;

        for (int y = border.MinCy; y <= border.MaxCy; y++)
        {
            for (int x = border.MinCx; x <= border.MaxCx; x++)
            {
                ChunkCoord coord = new(x, y);
                if (table.TryGetInfo(coord, out ChunkResidencyInfo info) && info.State != ChunkResidencyState.Detached)
                {
                    ChunkResidencyState desired = active.Contains(coord) ? ChunkResidencyState.Active : ChunkResidencyState.Border;
                    if (info.State != desired)
                    {
                        _plan.AddStateChange(new ResidencyStateChange(coord, desired));
                    }

                    continue;
                }

                if (remainingOps == 0)
                {
                    continue;
                }

                _plan.AddLoad(coord);
                remainingOps--;
            }
        }

        foreach (KeyValuePair<ChunkCoord, ChunkResidencyInfo> entry in table)
        {
            if (border.Contains(entry.Key) || entry.Value.State is ChunkResidencyState.Cached or ChunkResidencyState.Detached)
            {
                continue;
            }

            _plan.AddStateChange(new ResidencyStateChange(entry.Key, ChunkResidencyState.Cached));
        }

        if (remainingOps > 0 && budget.OverCap)
        {
            ReadOnlySpan<ChunkCoord> evictions = budget.SelectEvictions(table, border, budget.EvictionTargetBytes);
            for (int i = 0; i < evictions.Length && remainingOps > 0; i++)
            {
                _plan.AddUnload(evictions[i]);
                _plan.AddStateChange(new ResidencyStateChange(evictions[i], ChunkResidencyState.Detached));
                remainingOps--;
            }
        }

        return _plan;
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
