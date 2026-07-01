using System.Buffers;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Scripting;

/// <summary>
/// 基于 Simulation 真实后端的脚本上下文，负责把脚本写入命令延迟到 Hosting 相位安全窗口落地。
/// </summary>
public sealed class ScriptSimulationContext : IScriptContext, IDisposable
{
    private readonly ScriptCommandQueue _commands = new();
    private readonly CellFacade _cells;
    private readonly MaterialFacade _materials;
    private readonly ParticleFacade _particles;
    private readonly SolidFacade _solids;
    private ScriptCommand[] _drainBuffer = ArrayPool<ScriptCommand>.Shared.Rent(16);
    private bool _disposed;

    /// <summary>
    /// 创建脚本 Simulation 上下文。
    /// </summary>
    /// <param name="scene">脚本场景。</param>
    /// <param name="grid">权威 cell 网格访问门面。</param>
    /// <param name="kernel">CA 内核，用于需要 current dirty 的安全写入。</param>
    /// <param name="particleSystem">自由粒子系统。</param>
    /// <param name="materials">材质注册表。</param>
    /// <param name="events">脚本事件总线；未提供时访问 <see cref="Events" /> 会抛出明确异常。</param>
    /// <param name="time">时间 facade；未提供时访问 <see cref="Time" /> 会抛出明确异常。</param>
    /// <param name="audio">音频 facade；未提供时访问 <see cref="Audio" /> 会抛出明确异常。</param>
    public ScriptSimulationContext(
        Scene scene,
        CellGrid grid,
        SimulationKernel kernel,
        ParticleSystem particleSystem,
        MaterialTable materials,
        IEventBus? events = null,
        IGameTime? time = null,
        IAudioApi? audio = null)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(particleSystem);
        ArgumentNullException.ThrowIfNull(materials);

        _cells = new CellFacade(_commands, grid);
        _materials = new MaterialFacade(materials);
        _particles = new ParticleFacade(_commands);
        _solids = new SolidFacade(grid);
        Grid = grid;
        Kernel = kernel;
        ParticleSystem = particleSystem;
        EventBackend = events;
        TimeBackend = time;
        AudioBackend = audio;
    }

    private IEventBus? EventBackend { get; }

    private IGameTime? TimeBackend { get; }

    private IAudioApi? AudioBackend { get; }

    /// <summary>
    /// CA 内核，供 Hosting 相位驱动在 dirty swap 前落地脚本 cell 命令。
    /// </summary>
    public SimulationKernel Kernel { get; }

    /// <summary>
    /// 世界 cell 访问门面，供脚本命令落地时写入 working dirty。
    /// </summary>
    public CellGrid Grid { get; }

    /// <summary>
    /// 自由粒子系统，供 Hosting 相位驱动在相位 7 落地脚本粒子命令。
    /// </summary>
    public ParticleSystem ParticleSystem { get; }

    /// <inheritdoc />
    public IWorldCellAccess Cells => _cells;

    /// <inheritdoc />
    public IMaterialQuery Materials => _materials;

    /// <inheritdoc />
    public IParticleSpawner Particles => _particles;

    /// <inheritdoc />
    public ISolidSampler Solids => _solids;

    /// <inheritdoc />
    public IRigidBodyApi Bodies => throw Unsupported(nameof(Bodies));

    /// <inheritdoc />
    public ICharacterController Character => throw Unsupported(nameof(Character));

    /// <inheritdoc />
    public ICameraApi Camera => throw Unsupported(nameof(Camera));

    /// <inheritdoc />
    public IInputApi Input => throw Unsupported(nameof(Input));

    /// <inheritdoc />
    public IEventBus Events => EventBackend ?? throw Unsupported(nameof(Events));

    /// <inheritdoc />
    public IAudioApi Audio => AudioBackend ?? throw Unsupported(nameof(Audio));

    /// <inheritdoc />
    public IGameTime Time => TimeBackend ?? throw Unsupported(nameof(Time));

    /// <inheritdoc />
    public Scene Scene { get; }

    /// <summary>
    /// 在 dirty rectangle swap 前落地脚本 cell 写命令，使下一次 CA 可见。
    /// </summary>
    /// <returns>已落地的命令数量。</returns>
    public int FlushCellCommands()
    {
        ThrowIfDisposed();
        Span<ScriptCommand> commands = Drain(ScriptCommandTarget.CellWrite);
        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly ScriptCommand command = ref commands[i];
            switch (command.Kind)
            {
                case ScriptCommandKind.SetCell:
                    Grid.SetMaterial(command.X, command.Y, command.Material.Value);
                    break;
                case ScriptCommandKind.Paint:
                    Paint(command.X, command.Y, command.Width, command.Material.Value);
                    break;
                case ScriptCommandKind.SpawnParticle:
                case ScriptCommandKind.BurstParticles:
                case ScriptCommandKind.CreateBodyFromRegion:
                case ScriptCommandKind.ApplyImpulse:
                case ScriptCommandKind.DestroyBody:
                case ScriptCommandKind.MoveCharacter:
                    throw new InvalidOperationException($"脚本 cell 命令目标收到不匹配命令：{command.Kind}。");
                default:
                    throw new InvalidOperationException($"未知脚本命令：{command.Kind}。");
            }
        }

        return commands.Length;
    }

    /// <summary>
    /// 在相位 7 落地脚本自由粒子生成命令。
    /// </summary>
    /// <returns>已消费的命令数量。</returns>
    public int FlushParticleCommands()
    {
        ThrowIfDisposed();
        Span<ScriptCommand> commands = Drain(ScriptCommandTarget.Particle);
        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly ScriptCommand command = ref commands[i];
            switch (command.Kind)
            {
                case ScriptCommandKind.SpawnParticle:
                    ParticleSpawn spawn = ToParticleSpawn(command.Particle);
                    _ = ParticleSystem.TrySpawn(in spawn);
                    break;
                case ScriptCommandKind.BurstParticles:
                    SpawnBurst(command);
                    break;
                case ScriptCommandKind.SetCell:
                case ScriptCommandKind.Paint:
                case ScriptCommandKind.CreateBodyFromRegion:
                case ScriptCommandKind.ApplyImpulse:
                case ScriptCommandKind.DestroyBody:
                case ScriptCommandKind.MoveCharacter:
                    throw new InvalidOperationException($"脚本粒子命令目标收到不匹配命令：{command.Kind}。");
                default:
                    throw new InvalidOperationException($"未知脚本命令：{command.Kind}。");
            }
        }

        return commands.Length;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _commands.Dispose();
        ScriptCommand[] drainBuffer = _drainBuffer;
        _drainBuffer = [];
        ArrayPool<ScriptCommand>.Shared.Return(drainBuffer, clearArray: true);
        _disposed = true;
    }

    private Span<ScriptCommand> Drain(ScriptCommandTarget target)
    {
        int count = _commands.Count(target);
        EnsureDrainCapacity(count);
        int written = _commands.DrainTo(target, _drainBuffer.AsSpan(0, count));
        return _drainBuffer.AsSpan(0, written);
    }

    private void EnsureDrainCapacity(int count)
    {
        if (_drainBuffer.Length >= count)
        {
            return;
        }

        ScriptCommand[] replacement = ArrayPool<ScriptCommand>.Shared.Rent(count);
        ArrayPool<ScriptCommand>.Shared.Return(_drainBuffer, clearArray: true);
        _drainBuffer = replacement;
    }

    private void Paint(int centerX, int centerY, int radius, ushort material)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        int radiusSquared = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    Grid.SetMaterial(x, y, material);
                }
            }
        }
    }

    private void SpawnBurst(ScriptCommand command)
    {
        int count = command.Width;
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return;
        }

        ParticleSpawnDesc desc = command.Particle;
        float angleStep = MathF.Tau / count;
        for (int i = 0; i < count; i++)
        {
            float angle = angleStep * i;
            ParticleSpawn spawn = new(
                desc.X,
                desc.Y,
                MathF.Cos(angle) * command.A,
                MathF.Sin(angle) * command.A,
                desc.Material.Value,
                ColorVariant: 0,
                ClampLifetime(desc.Lifetime));
            _ = ParticleSystem.TrySpawn(in spawn);
        }
    }

    private static ParticleSpawn ToParticleSpawn(ParticleSpawnDesc desc)
    {
        return new ParticleSpawn(
            desc.X,
            desc.Y,
            desc.VelocityX,
            desc.VelocityY,
            desc.Material.Value,
            ColorVariant: 0,
            ClampLifetime(desc.Lifetime));
    }

    private static byte ClampLifetime(ushort lifetime)
    {
        return lifetime > byte.MaxValue ? byte.MaxValue : (byte)lifetime;
    }

    private static NotSupportedException Unsupported(string service)
    {
        return new NotSupportedException($"当前 ScriptSimulationContext 未注入 {service} 后端。");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class CellFacade(ScriptCommandQueue commands, CellGrid grid) : IWorldCellAccess
    {
        public MaterialId GetMaterial(int x, int y)
        {
            return new MaterialId(grid.GetMaterial(x, y));
        }

        public CellView Sample(int x, int y)
        {
            return new CellView(new MaterialId(grid.GetMaterial(x, y)), grid.FlagsAt(x, y), grid.LifetimeAt(x, y));
        }

        public bool IsSolid(int x, int y)
        {
            return grid.GetCellType(x, y) == CellType.Solid;
        }

        public void SetCell(int x, int y, MaterialId material)
        {
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.SetCell(x, y, material));
        }

        public void Paint(int x, int y, int radius, MaterialId material)
        {
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.Paint(x, y, radius, material));
        }
    }

    private sealed class MaterialFacade(MaterialTable materials) : IMaterialQuery
    {
        public MaterialId Resolve(string name)
        {
            return TryResolve(name, out MaterialId id) ? id : MaterialId.Invalid;
        }

        public bool TryResolve(string name, out MaterialId id)
        {
            if (materials.TryGetId(name, out ushort raw))
            {
                id = new MaterialId(raw);
                return true;
            }

            id = MaterialId.Invalid;
            return false;
        }

        public MaterialInfo GetInfo(MaterialId id)
        {
            ref readonly MaterialDef material = ref materials.Get(id.Value);
            return new MaterialInfo(id, material.Name, material.Density, material.Type == CellType.Solid);
        }
    }

    private sealed class ParticleFacade(ScriptCommandQueue commands) : IParticleSpawner
    {
        public void Spawn(in ParticleSpawnDesc desc)
        {
            commands.Enqueue(ScriptCommandTarget.Particle, ScriptCommand.SpawnParticle(in desc));
        }

        public void Burst(float x, float y, MaterialId material, int count, float speed)
        {
            commands.Enqueue(ScriptCommandTarget.Particle, ScriptCommand.BurstParticles(x, y, material, count, speed));
        }
    }

    private sealed class SolidFacade(CellGrid grid) : ISolidSampler
    {
        public bool Raycast(float x, float y, float dx, float dy, float maxDist, out RaycastHit hit)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(dx) || !float.IsFinite(dy) ||
                !float.IsFinite(maxDist) || maxDist < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDist), "Raycast 参数必须是有限值，且 maxDist 不能为负。");
            }

            float length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length <= float.Epsilon)
            {
                hit = default;
                return false;
            }

            float stepX = dx / length;
            float stepY = dy / length;
            int steps = (int)MathF.Ceiling(maxDist);
            for (int i = 0; i <= steps; i++)
            {
                float distance = MathF.Min(i, maxDist);
                int cellX = (int)MathF.Floor(x + (stepX * distance));
                int cellY = (int)MathF.Floor(y + (stepY * distance));
                if (IsSolidCell(cellX, cellY))
                {
                    hit = new RaycastHit(true, cellX, cellY, distance, new MaterialId(grid.GetMaterial(cellX, cellY)));
                    return true;
                }
            }

            hit = default;
            return false;
        }

        public bool SampleSolidAabb(float x, float y, float width, float height)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height) ||
                width < 0 || height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "AABB 参数必须是有限值，且尺寸不能为负。");
            }

            int minX = (int)MathF.Floor(x);
            int minY = (int)MathF.Floor(y);
            int maxX = (int)MathF.Ceiling(x + width) - 1;
            int maxY = (int)MathF.Ceiling(y + height) - 1;
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (IsSolidCell(cx, cy))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsSolidCell(int x, int y)
        {
            return grid.GetCellType(x, y) == CellType.Solid;
        }
    }
}
