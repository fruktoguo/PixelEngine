using PixelEngine.Core;
using PixelEngine.Core.Threading;
using System.Runtime.CompilerServices;

namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 自由粒子的连续缓冲池。活跃粒子始终位于数组前缀，释放使用 swap-remove，稳态不扩容。
/// </summary>
public sealed class ParticleSystem
{
    private static readonly RangeJob IntegrateRangeJob = static (start, end, workerIndex, context) =>
    {
        ParticleSystem system = (ParticleSystem)context!;
        system.IntegrateRange(start, end);
    };

    private readonly Particle[] _particles;
    private readonly ParticleOutcome[] _outcomes;
    private readonly EjectionRequest[] _ejectionRequests;
    private int _spawnedThisTick;
    private int _depositedThisTick;
    private int _killedByLifetimeThisTick;
    private int _droppedThisTick;
    private int _ejectionRequestCount;
    private CellGrid? _activeGrid;

    /// <summary>
    /// 创建指定容量的自由粒子系统。
    /// </summary>
    public ParticleSystem(int capacity = EngineConstants.ParticleCapacityDefault)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _particles = GC.AllocateArray<Particle>(capacity, pinned: true);
        _outcomes = GC.AllocateArray<ParticleOutcome>(capacity, pinned: true);
        _ejectionRequests = GC.AllocateArray<EjectionRequest>(EngineConstants.ParticleEjectMaxPerTick, pinned: true);
    }

    /// <summary>
    /// 当前活跃粒子数量。
    /// </summary>
    public int ActiveCount { get; private set; }

    /// <summary>
    /// 固定粒子容量。
    /// </summary>
    public int Capacity => _particles.Length;

    /// <summary>
    /// 活跃粒子的可写连续前缀视图。
    /// </summary>
    public Span<Particle> Active => _particles.AsSpan(0, ActiveCount);

    /// <summary>
    /// 活跃粒子的只读连续前缀视图。
    /// </summary>
    public ReadOnlySpan<Particle> ActiveReadOnly => _particles.AsSpan(0, ActiveCount);

    /// <summary>
    /// 当前 tick 的诊断计数。
    /// </summary>
    public ParticleSystemStats Stats => new(
        ActiveCount,
        Capacity,
        _spawnedThisTick,
        _depositedThisTick,
        _killedByLifetimeThisTick,
        _droppedThisTick);

    /// <summary>
    /// 清空本 tick 的增量诊断计数，不改变活跃粒子。
    /// </summary>
    public void ResetTickStats()
    {
        _spawnedThisTick = 0;
        _depositedThisTick = 0;
        _killedByLifetimeThisTick = 0;
        _droppedThisTick = 0;
    }

    /// <summary>
    /// 相位 1：入队一个 cell→particle 抛射请求。请求队列满时返回 false。
    /// </summary>
    public bool RequestEjection(in EjectionRequest request)
    {
        if (request.Radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Radius, "抛射半径不能为负。");
        }

        if (_ejectionRequestCount >= _ejectionRequests.Length)
        {
            _droppedThisTick++;
            return false;
        }

        _ejectionRequests[_ejectionRequestCount++] = request;
        return true;
    }

    /// <summary>
    /// 尝试生成一个自由粒子。容量满时返回 false，并计入 dropped 诊断，不扩容。
    /// </summary>
    public bool TrySpawn(in ParticleSpawn spawn)
    {
        if (ActiveCount >= _particles.Length)
        {
            _droppedThisTick++;
            return false;
        }

        _particles[ActiveCount++] = spawn.ToParticle();
        _spawnedThisTick++;
        return true;
    }

    /// <summary>
    /// 以 swap-remove 释放指定活跃粒子槽位。
    /// </summary>
    public void RemoveAtSwapBack(int index)
    {
        if ((uint)index >= (uint)ActiveCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int last = --ActiveCount;
        if (index != last)
        {
            _particles[index] = _particles[last];
            _outcomes[index] = _outcomes[last];
        }

        _particles[last] = default;
        _outcomes[last] = default;
    }

    /// <summary>
    /// 相位 3a：单线程执行粒子弹道积分与沉积候选归类。只写粒子槽位和 outcome，不写网格。
    /// </summary>
    public void IntegrateAndAdvance(CellGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _activeGrid = grid;
        try
        {
            IntegrateRange(0, ActiveCount);
        }
        finally
        {
            _activeGrid = null;
        }
    }

    /// <summary>
    /// 相位 3a：使用 JobSystem 并行执行粒子弹道积分与沉积候选归类。只读网格、不写网格。
    /// </summary>
    public void IntegrateAndAdvance(JobSystem jobs, CellGrid grid)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(grid);
        if (ActiveCount == 0)
        {
            return;
        }

        _activeGrid = grid;
        try
        {
            jobs.ParallelRange(ActiveCount, 256, IntegrateRangeJob, this);
        }
        finally
        {
            _activeGrid = null;
        }
    }

    /// <summary>
    /// 相位 3b：把 WantsDeposit 粒子写回网格；成功沉积或死亡粒子用 swap-remove 释放。
    /// </summary>
    public void ResolveDeposits(SimulationKernel kernel, CellGrid grid)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(grid);

        int i = 0;
        while (i < ActiveCount)
        {
            ParticleOutcome outcome = _outcomes[i];
            if (outcome.Kind == ParticleOutcomeKind.Flying)
            {
                i++;
                continue;
            }

            if (outcome.Kind == ParticleOutcomeKind.Dead)
            {
                _killedByLifetimeThisTick++;
                RemoveAtSwapBack(i);
                continue;
            }

            if (TryDepositAt(kernel, grid, i, outcome.X, outcome.Y))
            {
                _depositedThisTick++;
                RemoveAtSwapBack(i);
                continue;
            }

            if (_particles[i].Life == 0)
            {
                _killedByLifetimeThisTick++;
                RemoveAtSwapBack(i);
                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// 相位 7：执行已入队的 cell→particle 抛射请求，读 cell 并经 SimulationKernel 清空网格。
    /// </summary>
    public void RunEjectionPass(SimulationKernel kernel, CellGrid grid)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(grid);

        int ejected = 0;
        for (int requestIndex = 0; requestIndex < _ejectionRequestCount && ejected < EngineConstants.ParticleEjectMaxPerTick; requestIndex++)
        {
            EjectionRequest request = _ejectionRequests[requestIndex];
            int radiusSq = request.Radius * request.Radius;
            for (int y = request.CenterY - request.Radius; y <= request.CenterY + request.Radius && ejected < EngineConstants.ParticleEjectMaxPerTick; y++)
            {
                for (int x = request.CenterX - request.Radius; x <= request.CenterX + request.Radius && ejected < EngineConstants.ParticleEjectMaxPerTick; x++)
                {
                    int dx = x - request.CenterX;
                    int dy = y - request.CenterY;
                    if ((dx * dx) + (dy * dy) > radiusSq ||
                        !grid.TryGetMaterial(x, y, out ushort material) ||
                        material == 0 ||
                        (request.Mask & MaskFor(grid.MaterialProps.TypeOf(material))) == 0)
                    {
                        continue;
                    }

                    ParticleSpawn spawn = CreateEjectionSpawn(request, x, y, dx, dy, material);
                    if (!TrySpawn(in spawn))
                    {
                        continue;
                    }

                    ushort clearedMaterial = kernel.ReadAndClearCell(x, y, out _, out _);
                    if (clearedMaterial != material)
                    {
                        RemoveAtSwapBack(ActiveCount - 1);
                        _spawnedThisTick--;
                        throw new InvalidOperationException("粒子抛射阶段的 CellGrid 与 SimulationKernel 驻留 chunk 不一致。");
                    }

                    ejected++;
                }
            }
        }

        Array.Clear(_ejectionRequests, 0, _ejectionRequestCount);
        _ejectionRequestCount = 0;
    }

    /// <summary>
    /// 清空全部活跃粒子并重置 tick 诊断。
    /// </summary>
    public void Clear()
    {
        _particles.AsSpan(0, ActiveCount).Clear();
        _outcomes.AsSpan(0, ActiveCount).Clear();
        ActiveCount = 0;
        Array.Clear(_ejectionRequests, 0, _ejectionRequestCount);
        _ejectionRequestCount = 0;
        ResetTickStats();
    }

    private void IntegrateRange(int start, int end)
    {
        CellGrid grid = _activeGrid ?? throw new InvalidOperationException("粒子积分缺少 CellGrid 上下文。");
        ref Particle particleBase = ref _particles[0];
        ref ParticleOutcome outcomeBase = ref _outcomes[0];
        for (int i = start; i < end; i++)
        {
            ref Particle particleSlot = ref Unsafe.Add(ref particleBase, i);
            Particle particle = particleSlot;
            float oldX = particle.X;
            float oldY = particle.Y;
            float moveVx = particle.Vx;
            float moveVy = particle.Vy;
            particle.X += particle.Vx;
            particle.Y += particle.Vy;
            particle.Vy += EngineConstants.ParticleGravityPerTick;
            if (particle.Life > 0)
            {
                particle.Life--;
            }

            particleSlot = particle;
            Unsafe.Add(ref outcomeBase, i) = ClassifyOutcome(grid, oldX, oldY, moveVx, moveVy, particle);
        }
    }

    private static ParticleOutcome ClassifyOutcome(CellGrid grid, float oldX, float oldY, float moveVx, float moveVy, Particle particle)
    {
        if (particle.Life == 0)
        {
            return ParticleOutcome.Dead;
        }

        int oldCellX = FloorToCell(oldX);
        int oldCellY = FloorToCell(oldY);
        int newCellX = FloorToCell(particle.X);
        int newCellY = FloorToCell(particle.Y);
        int dx = newCellX - oldCellX;
        int dy = newCellY - oldCellY;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        int lastOpenX = oldCellX;
        int lastOpenY = oldCellY;

        for (int step = 1; step <= steps; step++)
        {
            float t = step / (float)steps;
            int sx = FloorToCell(oldX + ((particle.X - oldX) * t));
            int sy = FloorToCell(oldY + ((particle.Y - oldY) * t));
            if (sx == lastOpenX && sy == lastOpenY)
            {
                continue;
            }

            if (!grid.TryGetMaterial(sx, sy, out ushort material) || material != 0)
            {
                return ParticleOutcome.WantsDeposit(lastOpenX, lastOpenY);
            }

            lastOpenX = sx;
            lastOpenY = sy;
        }

        float speedSq = (moveVx * moveVx) + (moveVy * moveVy);
        return speedSq <= EngineConstants.ParticleDepositSpeedEpsilon * EngineConstants.ParticleDepositSpeedEpsilon
            ? ParticleOutcome.WantsDeposit(newCellX, newCellY)
            : ParticleOutcome.Flying;
    }

    private bool TryDepositAt(SimulationKernel kernel, CellGrid grid, int particleIndex, int x, int y)
    {
        Particle particle = _particles[particleIndex];
        if (!grid.TryGetMaterial(x, y, out ushort targetMaterial))
        {
            return false;
        }

        if (targetMaterial == 0)
        {
            kernel.DepositCell(x, y, particle.Material, persistentFlags: 0);
            return true;
        }

        if (grid.MaterialProps.DensityOf(particle.Material) > grid.MaterialProps.DensityOf(targetMaterial))
        {
            _ = kernel.ReadAndClearCell(x, y, out _, out _);
            kernel.DepositCell(x, y, particle.Material, persistentFlags: 0);
            particle.Material = targetMaterial;
            particle.Vx = 0;
            particle.Vy = 0;
            _particles[particleIndex] = particle;
            _outcomes[particleIndex] = ParticleOutcome.WantsDeposit(x, y);
            return false;
        }

        return TryDepositNeighbor(kernel, grid, particle, x, y - 1) ||
            TryDepositNeighbor(kernel, grid, particle, x - 1, y) ||
            TryDepositNeighbor(kernel, grid, particle, x + 1, y) ||
            TryDepositNeighbor(kernel, grid, particle, x, y + 1);
    }

    private static bool TryDepositNeighbor(SimulationKernel kernel, CellGrid grid, Particle particle, int x, int y)
    {
        if (!grid.TryGetMaterial(x, y, out ushort material) || material != 0)
        {
            return false;
        }

        kernel.DepositCell(x, y, particle.Material, persistentFlags: 0);
        return true;
    }

    private static ParticleSpawn CreateEjectionSpawn(EjectionRequest request, int x, int y, int dx, int dy, ushort material)
    {
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        float nx = length > 0 ? dx / length : 1;
        float ny = length > 0 ? dy / length : 0;
        float jitter = request.ImpulseJitter == 0 ? 0 : HashToUnitByte(x, y, material) / 255f * request.ImpulseJitter;
        float speed = request.ImpulseSpeed + jitter;
        return new ParticleSpawn(
            x + 0.5f,
            y + 0.5f,
            nx * speed,
            ny * speed,
            material,
            HashToUnitByte(x, y, material),
            EngineConstants.ParticleMaxLifetimeTicks);
    }

    private static EjectMask MaskFor(CellType type)
    {
        return type switch
        {
            CellType.Powder => EjectMask.Powder,
            CellType.Liquid => EjectMask.Liquid,
            CellType.Gas => EjectMask.Gas,
            CellType.Fire => EjectMask.Fire,
            CellType.Solid => EjectMask.Solid,
            CellType.Empty => EjectMask.None,
            _ => EjectMask.None,
        };
    }

    private static byte HashToUnitByte(int x, int y, ushort material)
    {
        uint hash = ((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ (material * 83492791u);
        hash ^= hash >> 13;
        hash *= 1274126177u;
        return (byte)(hash >> 24);
    }

    private static int FloorToCell(float value)
    {
        return (int)MathF.Floor(value);
    }
}
