using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using PixelEngine.Core;
using PixelEngine.Physics;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Scripting;

/// <summary>
/// 基于 Simulation 真实后端的脚本上下文，负责把脚本写入命令延迟到 Hosting 相位安全窗口落地。
/// </summary>
public sealed class ScriptSimulationContext : IScriptContext, IDisposable
{
    private readonly ScriptCommandQueue _commands = new();
    private readonly WorldMutationAccumulator _worldMutations = new();
    private readonly CellFacade _cells;
    private readonly WorldEffectsFacade _world;
    private readonly MaterialFacade _materials;
    private readonly ParticleFacade _particles;
    private readonly SolidFacade _solids;
    private readonly BodyFacade? _bodies;
    private readonly CharacterFacade _character;
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
    /// <param name="temperature">温度场；未提供时访问热量写入 API 会抛出明确异常。</param>
    /// <param name="events">脚本事件总线；未提供时访问 <see cref="Events" /> 会抛出明确异常。</param>
    /// <param name="time">时间 facade；未提供时访问 <see cref="Time" /> 会抛出明确异常。</param>
    /// <param name="audio">音频 facade；未提供时访问 <see cref="Audio" /> 会抛出明确异常。</param>
    /// <param name="physics">物理系统 facade；提供时角色移动经其记录诊断，否则直接使用角色控制器解算。</param>
    /// <param name="camera">相机 facade；未提供时访问 <see cref="Camera" /> 会抛出明确异常。</param>
    /// <param name="input">输入 facade；未提供时访问 <see cref="Input" /> 会抛出明确异常。</param>
    /// <param name="lighting">光照 facade；未提供时访问 <see cref="Lighting" /> 会抛出明确异常。</param>
    /// <param name="overlay">overlay facade；未提供时访问 <see cref="Overlay" /> 会抛出明确异常。</param>
    /// <param name="diagnostics">诊断 facade；未提供时访问 <see cref="Diagnostics" /> 会抛出明确异常。</param>
    /// <param name="runtime">运行时控制 facade；未提供时访问 <see cref="Runtime" /> 会抛出明确异常。</param>
    /// <param name="gameUi">游戏大 UI facade；未提供时注入空服务。</param>
    /// <param name="config">配置加载 facade；未提供时访问 <see cref="Config" /> 会抛出明确异常。</param>
    /// <param name="physicsEvents">物理后事件 facade；未提供时使用空后端。</param>
    public ScriptSimulationContext(
        Scene scene,
        CellGrid grid,
        SimulationKernel kernel,
        ParticleSystem particleSystem,
        MaterialTable materials,
        TemperatureField? temperature = null,
        IEventBus? events = null,
        IGameTime? time = null,
        IAudioApi? audio = null,
        PhysicsSystem? physics = null,
        ICameraApi? camera = null,
        IInputApi? input = null,
        ILightingApi? lighting = null,
        IOverlayApi? overlay = null,
        IDiagnosticsApi? diagnostics = null,
        IRuntimeControlApi? runtime = null,
        IGameUiService? gameUi = null,
        IConfigApi? config = null,
        IPhysicsStepEvents? physicsEvents = null)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(particleSystem);
        ArgumentNullException.ThrowIfNull(materials);

        // 读路径直连 Grid；写路径统一入队，由 Hosting 按相位 Flush 到 Simulation 后端。
        _cells = new CellFacade(_commands, grid, _worldMutations);
        _world = new WorldEffectsFacade(_commands, _worldMutations, hasPhysics: physics is not null);
        _materials = new MaterialFacade(materials);
        _particles = new ParticleFacade(_commands);
        _solids = new SolidFacade(grid);
        _bodies = physics is null ? null : new BodyFacade(_commands, physics);
        _character = new CharacterFacade(_commands, grid, physics, time);
        PhysicsEvents = physicsEvents ?? NoopPhysicsStepEvents.Instance;
        Grid = grid;
        Kernel = kernel;
        Temperature = temperature;
        ParticleSystem = particleSystem;
        EventBackend = events;
        TimeBackend = time;
        AudioBackend = audio;
        CameraBackend = camera;
        InputBackend = input;
        LightingBackend = lighting;
        OverlayBackend = overlay;
        DiagnosticsBackend = diagnostics;
        RuntimeBackend = runtime;
        GameUiBackend = gameUi ?? NoopGameUiService.Instance;
        ConfigBackend = config;
    }

    private IEventBus? EventBackend { get; }

    private IGameTime? TimeBackend { get; }

    private IAudioApi? AudioBackend { get; }

    private ICameraApi? CameraBackend { get; }

    private IInputApi? InputBackend { get; }

    private ILightingApi? LightingBackend { get; }

    private IOverlayApi? OverlayBackend { get; }

    private IDiagnosticsApi? DiagnosticsBackend { get; }

    private IRuntimeControlApi? RuntimeBackend { get; }

    private IGameUiService GameUiBackend { get; set; }

    private IConfigApi? ConfigBackend { get; }

    /// <summary>
    /// CA 内核，供 Hosting 相位驱动在 dirty swap 前落地脚本 cell 命令。
    /// </summary>
    public SimulationKernel Kernel { get; }

    /// <summary>
    /// 温度场；供脚本热量命令在 cell 安全相位落地。
    /// </summary>
    public TemperatureField? Temperature { get; }

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
    public IWorldEffects World => _world;

    /// <inheritdoc />
    public IMaterialQuery Materials => _materials;

    /// <inheritdoc />
    public IParticleSpawner Particles => _particles;

    /// <inheritdoc />
    public ISolidSampler Solids => _solids;

    /// <inheritdoc />
    public IRigidBodyApi Bodies => _bodies ?? throw Unsupported(nameof(Bodies));

    /// <inheritdoc />
    public ICharacterController Character => _character;

    /// <inheritdoc />
    public IPhysicsStepEvents PhysicsEvents { get; }

    /// <inheritdoc />
    public ICameraApi Camera => CameraBackend ?? throw Unsupported(nameof(Camera));

    /// <inheritdoc />
    public IInputApi Input => InputBackend ?? throw Unsupported(nameof(Input));

    /// <inheritdoc />
    public ILightingApi Lighting => LightingBackend ?? throw Unsupported(nameof(Lighting));

    /// <inheritdoc />
    public IOverlayApi Overlay => OverlayBackend ?? throw Unsupported(nameof(Overlay));

    /// <inheritdoc />
    public IDiagnosticsApi Diagnostics => DiagnosticsBackend ?? throw Unsupported(nameof(Diagnostics));

    /// <inheritdoc />
    public IRuntimeControlApi Runtime => RuntimeBackend ?? throw Unsupported(nameof(Runtime));

    /// <inheritdoc />
    public IEventBus Events => EventBackend ?? throw Unsupported(nameof(Events));

    /// <inheritdoc />
    public IAudioApi Audio => AudioBackend ?? throw Unsupported(nameof(Audio));

    /// <inheritdoc />
    public IGameUiService GameUi => GameUiBackend;

    /// <summary>
    /// 在窗口运行时晚于脚本运行时装配时，接入实际的 Game UI 服务。
    /// 仅供 Hosting 初始化边界调用；不会替换脚本场景或其他 facade。
    /// </summary>
    /// <param name="gameUi">已完成初始化的 Game UI 服务。</param>
    public void AttachGameUiService(IGameUiService gameUi)
    {
        ThrowIfDisposed();
        GameUiBackend = gameUi ?? throw new ArgumentNullException(nameof(gameUi));
    }

    /// <inheritdoc />
    public IConfigApi Config => ConfigBackend ?? throw Unsupported(nameof(Config));

    /// <inheritdoc />
    public IGameTime Time => TimeBackend ?? throw Unsupported(nameof(Time));

    /// <inheritdoc />
    public Scene Scene { get; private set; }

    /// <summary>
    /// 将现有脚本上下文切换到新的脚本 Scene，供编辑态 authoring projection 刷新复用同一 Hosting runtime。
    /// </summary>
    /// <param name="scene">新的脚本 Scene。</param>
    public void ReplaceScene(Scene scene)
    {
        ThrowIfDisposed();
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <inheritdoc />
    public void ClearFrameTransientRequests()
    {
        _worldMutations.Flush(EventBackend);
        if (OverlayBackend is ScriptOverlayApi overlay)
        {
            overlay.Clear();
        }
    }

    /// <summary>
    /// 清除 world/session 替换前的延迟命令与脚本句柄映射。
    /// </summary>
    /// <remarks>
    /// 仅由 Hosting 在结束旧 Play session 后、恢复新 session 前调用；普通 gameplay 不应逐帧调用。
    /// </remarks>
    public void ResetRuntimeState()
    {
        ThrowIfDisposed();
        _commands.Clear();
        _worldMutations.Reset();
        _bodies?.Reset();
        _character.Reset();
        ClearFrameTransientRequests();
    }

    /// <summary>
    /// 在 dirty rectangle swap 前落地脚本 cell 写命令，使下一次 CA 可见。
    /// </summary>
    /// <returns>已落地的命令数量。</returns>
    public int FlushCellCommands()
    {
        ThrowIfDisposed();
        // dirty swap 前落地：走相位 1 输入 API，使写入对本帧 CA current dirty 立即可见。
        Span<ScriptCommand> commands = Drain(ScriptCommandTarget.CellWrite);
        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly ScriptCommand command = ref commands[i];
            switch (command.Kind)
            {
                case ScriptCommandKind.SetCell:
                    if (command.Material.Value == 0)
                    {
                        Kernel.ClearCellAtInputPhase(command.X, command.Y);
                    }
                    else
                    {
                        Kernel.EditCellAtInputPhase(command.X, command.Y, command.Material.Value, persistentFlags: 0);
                    }

                    break;
                case ScriptCommandKind.Paint:
                    Paint(command.X, command.Y, command.Width, command.Material.Value);
                    break;
                case ScriptCommandKind.DamageCircle:
                    _ = Kernel.DamageCircle(command.X, command.Y, command.Width, checked((ushort)command.Height), command.A > 0f);
                    break;
                case ScriptCommandKind.DamageBeam:
                    _ = Kernel.DamageBeam(command.X, command.Y, command.A, command.B, command.Width, checked((ushort)command.Height));
                    break;
                case ScriptCommandKind.AddHeat:
                    AddHeat(command.X, command.Y, command.Width, command.A);
                    break;
                case ScriptCommandKind.Explode:
                case ScriptCommandKind.SpawnParticle:
                case ScriptCommandKind.BurstParticles:
                case ScriptCommandKind.EmitParticles:
                case ScriptCommandKind.CreateBodyFromRegion:
                case ScriptCommandKind.ApplyImpulse:
                case ScriptCommandKind.ApplyRadialImpulse:
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
        // 相位 7：自由粒子与抛射请求在此消费，避免与 CA movement 同 tick 争用网格。
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
                case ScriptCommandKind.EmitParticles:
                    EmitParticles(command.Emit);
                    break;
                case ScriptCommandKind.Explode:
                    QueueExplosion(command);
                    break;
                case ScriptCommandKind.SetCell:
                case ScriptCommandKind.Paint:
                case ScriptCommandKind.DamageCircle:
                case ScriptCommandKind.DamageBeam:
                case ScriptCommandKind.AddHeat:
                case ScriptCommandKind.CreateBodyFromRegion:
                case ScriptCommandKind.ApplyImpulse:
                case ScriptCommandKind.ApplyRadialImpulse:
                case ScriptCommandKind.DestroyBody:
                case ScriptCommandKind.MoveCharacter:
                    throw new InvalidOperationException($"脚本粒子命令目标收到不匹配命令：{command.Kind}。");
                default:
                    throw new InvalidOperationException($"未知脚本命令：{command.Kind}。");
            }
        }

        return commands.Length;
    }

    /// <summary>
    /// 在 Physics step 前落地脚本刚体命令。
    /// </summary>
    /// <returns>已消费的命令数量。</returns>
    public int FlushPhysicsCommands()
    {
        ThrowIfDisposed();
        Span<ScriptCommand> commands = Drain(ScriptCommandTarget.Physics);
        if (commands.IsEmpty)
        {
            return 0;
        }

        // Physics step 前落地刚体/角色命令，保证本 tick 解算看到最新冲量与碰撞体。
        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly ScriptCommand command = ref commands[i];
            switch (command.Kind)
            {
                case ScriptCommandKind.CreateBodyFromRegion:
                    (_bodies ?? throw Unsupported(nameof(Bodies))).CreateNow(command.Body, command.X, command.Y, command.Width, command.Height);
                    break;
                case ScriptCommandKind.ApplyImpulse:
                    (_bodies ?? throw Unsupported(nameof(Bodies))).ApplyImpulseNow(command.Body, command.A, command.B);
                    break;
                case ScriptCommandKind.ApplyRadialImpulse:
                    (_bodies ?? throw Unsupported(nameof(Bodies))).ApplyRadialImpulseNow(command.X, command.Y, command.Width, command.A);
                    break;
                case ScriptCommandKind.DestroyBody:
                    (_bodies ?? throw Unsupported(nameof(Bodies))).DestroyNow(command.Body);
                    break;
                case ScriptCommandKind.MoveCharacter:
                    _ = _character.MoveNow(command.Character, command.A, command.B);
                    break;
                case ScriptCommandKind.SetCell:
                case ScriptCommandKind.Paint:
                case ScriptCommandKind.DamageCircle:
                case ScriptCommandKind.DamageBeam:
                case ScriptCommandKind.AddHeat:
                case ScriptCommandKind.Explode:
                case ScriptCommandKind.SpawnParticle:
                case ScriptCommandKind.BurstParticles:
                case ScriptCommandKind.EmitParticles:
                    throw new InvalidOperationException($"脚本 physics 命令目标收到不匹配命令：{command.Kind}。");
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
                    if (material == 0)
                    {
                        Kernel.ClearCellAtInputPhase(x, y);
                    }
                    else
                    {
                        Kernel.EditCellAtInputPhase(x, y, material, persistentFlags: 0);
                    }
                }
            }
        }
    }

    private void AddHeat(int centerX, int centerY, int radius, float deltaCelsius)
    {
        TemperatureField field = Temperature ?? throw Unsupported(nameof(Temperature));
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        float radiusSquared = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            int dy = y - centerY;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                if ((dx * dx) + (dy * dy) > radiusSquared)
                {
                    continue;
                }

                field.AddHeat(x, y, deltaCelsius);
                Kernel.MarkDirty(x, y);
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
        float velocityScale = ParticleVelocityScale();
        for (int i = 0; i < count; i++)
        {
            float angle = angleStep * i;
            ParticleSpawn spawn = new(
                desc.X,
                desc.Y,
                MathF.Cos(angle) * command.A * velocityScale,
                MathF.Sin(angle) * command.A * velocityScale,
                desc.Material.Value,
                ColorVariant: 0,
                ClampLifetime(desc.Lifetime));
            _ = ParticleSystem.TrySpawn(in spawn);
        }
    }

    private void QueueExplosion(ScriptCommand command)
    {
        EjectionRequest request = new(
            command.X,
            command.Y,
            command.Width,
            command.A,
            command.B,
            EjectMask.Powder);
        _ = ParticleSystem.RequestEjection(in request);
    }

    private void EmitParticles(in ParticleEmit emit)
    {
        float velocityScale = ParticleVelocityScale();
        ParticleEmissionRequest request = new(
            emit.X,
            emit.Y,
            emit.Material.Value,
            emit.Count,
            emit.DirAngleRad,
            emit.DirSpreadRad,
            emit.BaseSpeed * velocityScale,
            emit.SpeedJitter * velocityScale,
            emit.LifeTicks);
        _ = ParticleSystem.Emit(in request);
    }

    private ParticleSpawn ToParticleSpawn(ParticleSpawnDesc desc)
    {
        float velocityScale = ParticleVelocityScale();
        return new ParticleSpawn(
            desc.X,
            desc.Y,
            desc.VelocityX * velocityScale,
            desc.VelocityY * velocityScale,
            desc.Material.Value,
            ColorVariant: 0,
            ClampLifetime(desc.Lifetime));
    }

    private float ParticleVelocityScale()
    {
        float fixedStep = TimeBackend?.FixedStep ?? (float)(1.0 / EngineConstants.DefaultSimHz);
        return float.IsFinite(fixedStep) && fixedStep > 0f
            ? fixedStep
            : (float)(1.0 / EngineConstants.DefaultSimHz);
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

    private sealed class CellFacade(
        ScriptCommandQueue commands,
        CellGrid grid,
        WorldMutationAccumulator mutations) : IWorldCellAccess
    {
        public bool IsResident(int x, int y)
        {
            return grid.TryGetMaterial(x, y, out _);
        }

        public MaterialId GetMaterial(int x, int y)
        {
            return new MaterialId(grid.GetMaterial(x, y));
        }

        public CellView Sample(int x, int y)
        {
            ushort material = grid.GetMaterial(x, y);
            ushort maxIntegrity = grid.MaterialProps.MaxIntegrityOf(material);
            byte damage = grid.GetDamage(x, y);
            int integrity = Math.Max(0, maxIntegrity - damage);
            return new CellView(
                new MaterialId(material),
                grid.GetFlags(x, y),
                grid.GetLifetime(x, y),
                (byte)Math.Min(byte.MaxValue, integrity));
        }

        public bool IsSolid(int x, int y)
        {
            return grid.GetCellType(x, y) == CellType.Solid;
        }

        public bool IsRigidOwned(int x, int y)
        {
            return CellFlags.Has(grid.GetFlags(x, y), CellFlags.RigidOwned);
        }

        public void SetCell(int x, int y, MaterialId material)
        {
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.SetCell(x, y, material));
            mutations.Include(
                x,
                y,
                x + 1,
                y + 1,
                material.Value == 0 ? WorldMutationKind.CellRemoval : WorldMutationKind.CellWrite);
        }

        public void Paint(int x, int y, int radius, MaterialId material)
        {
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.Paint(x, y, radius, material));
            mutations.IncludeCircle(
                x,
                y,
                radius,
                material.Value == 0 ? WorldMutationKind.CellRemoval : WorldMutationKind.CellWrite);
        }
    }

    private sealed class WorldEffectsFacade(
        ScriptCommandQueue commands,
        WorldMutationAccumulator mutations,
        bool hasPhysics) : IWorldEffects
    {
        private const float ExplosionDamageScale = 16f;

        public void DamageCircle(float x, float y, int radius, float damage, bool falloff = true, DamageKind kind = DamageKind.Impact)
        {
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidateFinite(damage, nameof(damage));
            ValidateDamageKind(kind, nameof(kind));
            ArgumentOutOfRangeException.ThrowIfNegative(radius);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damage);
            int centerX = (int)MathF.Floor(x);
            int centerY = (int)MathF.Floor(y);
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.DamageCircle(centerX, centerY, radius, ToDamageUShort(damage), falloff, kind));
            mutations.IncludeCircle(centerX, centerY, radius, WorldMutationKind.Damage);
        }

        public void DamageBeam(float x, float y, float dx, float dy, int length, float damagePerCell, DamageKind kind = DamageKind.Beam)
        {
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidateFinite(dx, nameof(dx));
            ValidateFinite(dy, nameof(dy));
            ValidateFinite(damagePerCell, nameof(damagePerCell));
            ValidateDamageKind(kind, nameof(kind));
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damagePerCell);
            if ((dx * dx) + (dy * dy) <= float.Epsilon)
            {
                throw new ArgumentOutOfRangeException(nameof(dx), "DamageBeam 方向不能为零向量。");
            }

            int startX = (int)MathF.Floor(x);
            int startY = (int)MathF.Floor(y);
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.DamageBeam(startX, startY, dx, dy, length, ToDamageUShort(damagePerCell), kind));
            mutations.IncludeBeam(startX, startY, dx, dy, length, WorldMutationKind.Damage);
        }

        public void AddHeat(float x, float y, int radius, float deltaCelsius)
        {
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidateFinite(deltaCelsius, nameof(deltaCelsius));
            ArgumentOutOfRangeException.ThrowIfNegative(radius);
            int centerX = (int)MathF.Floor(x);
            int centerY = (int)MathF.Floor(y);
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.AddHeat(centerX, centerY, radius, deltaCelsius));
            mutations.IncludeCircle(centerX, centerY, radius, WorldMutationKind.Heat);
        }

        public void Explode(float x, float y, int radius, float force)
        {
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidateFinite(force, nameof(force));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(force);
            int centerX = (int)MathF.Floor(x);
            int centerY = (int)MathF.Floor(y);
            // 爆炸拆成 cell 破坏、粒子抛射、可选径向冲量三路命令，分别在不同安全相位 Flush。
            commands.Enqueue(ScriptCommandTarget.CellWrite, ScriptCommand.DamageCircle(centerX, centerY, radius, ToDamageUShort(force * ExplosionDamageScale), falloff: true, DamageKind.Impact));
            mutations.IncludeCircle(centerX, centerY, radius, WorldMutationKind.Damage);
            float jitter = MathF.Max(1f, force * 0.25f);
            commands.Enqueue(ScriptCommandTarget.Particle, ScriptCommand.Explode(centerX, centerY, radius, force, jitter));
            if (hasPhysics)
            {
                commands.Enqueue(ScriptCommandTarget.Physics, ScriptCommand.ApplyRadialImpulse(centerX, centerY, radius, force));
            }
        }

        private static void ValidateFinite(float value, string name)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "参数必须是有限数值。");
            }
        }

        private static ushort ToDamageUShort(float value)
        {
            return (ushort)Math.Clamp((int)MathF.Ceiling(value), 1, ushort.MaxValue);
        }

        private static void ValidateDamageKind(DamageKind kind, string name)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(name, kind, "未知破坏类型。");
            }
        }
    }

    private sealed class WorldMutationAccumulator
    {
        private int _minX;
        private int _minY;
        private int _maxXExclusive;
        private int _maxYExclusive;
        private WorldMutationKind _kinds;

        public void IncludeCircle(int centerX, int centerY, int radius, WorldMutationKind kind)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(radius);
            Include(
                centerX - radius,
                centerY - radius,
                centerX + radius + 1,
                centerY + radius + 1,
                kind);
        }

        public void IncludeBeam(
            int startX,
            int startY,
            float dx,
            float dy,
            int length,
            WorldMutationKind kind)
        {
            float directionLength = MathF.Sqrt((dx * dx) + (dy * dy));
            float endX = startX + (dx / directionLength * length);
            float endY = startY + (dy / directionLength * length);
            Include(
                Math.Min(startX, (int)MathF.Floor(endX)) - 1,
                Math.Min(startY, (int)MathF.Floor(endY)) - 1,
                Math.Max(startX, (int)MathF.Ceiling(endX)) + 2,
                Math.Max(startY, (int)MathF.Ceiling(endY)) + 2,
                kind);
        }

        public void Include(
            int minX,
            int minY,
            int maxXExclusive,
            int maxYExclusive,
            WorldMutationKind kind)
        {
            if (kind == WorldMutationKind.None || maxXExclusive <= minX || maxYExclusive <= minY)
            {
                return;
            }

            if (_kinds == WorldMutationKind.None)
            {
                _minX = minX;
                _minY = minY;
                _maxXExclusive = maxXExclusive;
                _maxYExclusive = maxYExclusive;
                _kinds = kind;
                return;
            }

            _minX = Math.Min(_minX, minX);
            _minY = Math.Min(_minY, minY);
            _maxXExclusive = Math.Max(_maxXExclusive, maxXExclusive);
            _maxYExclusive = Math.Max(_maxYExclusive, maxYExclusive);
            _kinds |= kind;
        }

        public void Flush(IEventBus? events)
        {
            if (_kinds == WorldMutationKind.None)
            {
                return;
            }

            WorldMutationEvent item = new(
                _minX,
                _minY,
                _maxXExclusive,
                _maxYExclusive,
                _kinds);
            if (events is null || events.TryPublish(in item))
            {
                Reset();
            }
        }

        public void Reset()
        {
            _minX = 0;
            _minY = 0;
            _maxXExclusive = 0;
            _maxYExclusive = 0;
            _kinds = WorldMutationKind.None;
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
            MaterialProperty flags = material.PropertyFlags;
            bool emissive = (flags & MaterialProperty.Emissive) != 0 || material.RenderStyle == MaterialRenderStyle.Emissive;
            bool destructible = id.Value != 0 &&
                material.Type is CellType.Solid or CellType.Powder &&
                (flags & MaterialProperty.Indestructible) == 0;
            bool blocksCharacter = material.Type is CellType.Solid or CellType.Powder;
            return new MaterialInfo(
                id,
                material.Name,
                material.Density,
                material.Type == CellType.Solid,
                string.IsNullOrWhiteSpace(material.DisplayName) ? material.Name : material.DisplayName,
                LegendCategoryName(material.LegendCategory),
                material.LegendVisible,
                material.BaseColorBGRA,
                material.MineYield,
                material.Type,
                material.LegendCategory,
                emissive,
                material.Hardness != 0 ? material.Hardness : material.Durability,
                material.MaxIntegrity,
                destructible,
                material.Dispersion,
                blocksCharacter,
                material.Flammability,
                material.AutoIgnitionTemp,
                material.FireHp,
                material.TemperatureOfFire,
                material.GeneratesSmoke,
                material.HeatConduct,
                material.HeatCapacity,
                material.RenderStyle,
                flags);
        }

        private static string LegendCategoryName(MaterialLegendCategory category)
        {
            return category switch
            {
                MaterialLegendCategory.Terrain => "Terrain",
                MaterialLegendCategory.Liquid => "Liquid",
                MaterialLegendCategory.Gas => "Gas",
                MaterialLegendCategory.Destructible => "Destructible",
                MaterialLegendCategory.Hazard => "Hazard",
                MaterialLegendCategory.Resource => "Resource",
                MaterialLegendCategory.Special => "Special",
                _ => nameof(MaterialLegendCategory.Special),
            };
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

        public void Emit(in ParticleEmit emit)
        {
            ValidateFinite(emit.X, nameof(emit.X));
            ValidateFinite(emit.Y, nameof(emit.Y));
            ValidateFinite(emit.DirAngleRad, nameof(emit.DirAngleRad));
            ValidateFinite(emit.DirSpreadRad, nameof(emit.DirSpreadRad));
            ValidateFinite(emit.BaseSpeed, nameof(emit.BaseSpeed));
            ValidateFinite(emit.SpeedJitter, nameof(emit.SpeedJitter));
            ArgumentOutOfRangeException.ThrowIfNegative(emit.Count);
            commands.Enqueue(ScriptCommandTarget.Particle, ScriptCommand.EmitParticles(in emit));
        }

        private static void ValidateFinite(float value, string name)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "粒子发射参数必须为有限数值。");
            }
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

    private sealed class BodyFacade(ScriptCommandQueue commands, PhysicsSystem physics) : IRigidBodyApi
    {
        private const int PendingBodyKey = -1;
        private const int DestroyedBodyKey = -2;
        private readonly List<int> _bodyKeys = [];

        public BodyHandle CreateFromRegion(int x, int y, int width, int height)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            // 脚本句柄先占位 Pending，Flush 成功后再映射到 PhysicsSystem 的 body key。
            BodyHandle handle = new(_bodyKeys.Count);
            _bodyKeys.Add(PendingBodyKey);
            commands.Enqueue(ScriptCommandTarget.Physics, ScriptCommand.CreateBodyFromRegion(handle, x, y, width, height));
            return handle;
        }

        public bool TryGetTransform(BodyHandle handle, out BodyTransform transform)
        {
            if (!TryGetBodyKey(handle, out int bodyKey) || bodyKey < 0 ||
                !physics.TryGetBodyTransform(bodyKey, out Core.Mathematics.Transform2D physicsTransform))
            {
                transform = default;
                return false;
            }

            transform = new BodyTransform(
                physicsTransform.Position.X,
                physicsTransform.Position.Y,
                physicsTransform.Angle);
            return true;
        }

        public void ApplyImpulse(BodyHandle handle, float impulseX, float impulseY)
        {
            ValidateFinite(impulseX, nameof(impulseX));
            ValidateFinite(impulseY, nameof(impulseY));
            _ = GetMappedSlot(handle);
            commands.Enqueue(ScriptCommandTarget.Physics, ScriptCommand.ApplyImpulse(handle, impulseX, impulseY));
        }

        public void Destroy(BodyHandle handle)
        {
            _ = GetMappedSlot(handle);
            commands.Enqueue(ScriptCommandTarget.Physics, ScriptCommand.DestroyBody(handle));
        }

        public void CreateNow(BodyHandle handle, int x, int y, int width, int height)
        {
            ref int slot = ref GetMappedSlot(handle);
            if (slot != PendingBodyKey)
            {
                throw new InvalidOperationException("刚体创建命令对应的脚本句柄已被解析或销毁。");
            }

            try
            {
                slot = physics.CreateBodyFromRegion(x, y, width, height);
            }
            catch (InvalidOperationException)
            {
                slot = DestroyedBodyKey;
            }
        }

        public void ApplyImpulseNow(BodyHandle handle, float impulseX, float impulseY)
        {
            if (TryGetBodyKey(handle, out int bodyKey) && bodyKey >= 0)
            {
                _ = physics.ApplyLinearImpulse(bodyKey, impulseX, impulseY);
            }
        }

        public void ApplyRadialImpulseNow(int x, int y, int radius, float force)
        {
            _ = physics.ApplyRadialImpulse(x, y, radius, force);
        }

        public void DestroyNow(BodyHandle handle)
        {
            ref int slot = ref GetMappedSlot(handle);
            if (slot >= 0)
            {
                _ = physics.DestroyBody(slot);
            }

            slot = DestroyedBodyKey;
        }

        public void Reset()
        {
            _bodyKeys.Clear();
        }

        private bool TryGetBodyKey(BodyHandle handle, out int bodyKey)
        {
            if ((uint)handle.Value >= (uint)_bodyKeys.Count)
            {
                bodyKey = DestroyedBodyKey;
                return false;
            }

            bodyKey = _bodyKeys[handle.Value];
            return bodyKey != DestroyedBodyKey;
        }

        private ref int GetMappedSlot(BodyHandle handle)
        {
            if ((uint)handle.Value >= (uint)_bodyKeys.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), handle, "未知刚体句柄。");
            }

            return ref CollectionsMarshal.AsSpan(_bodyKeys)[handle.Value];
        }

        private static void ValidateFinite(float value, string name)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "参数必须是有限数值。");
            }
        }
    }

    private sealed class CharacterFacade(ScriptCommandQueue commands, CellGrid grid, PhysicsSystem? physics, IGameTime? time) : ICharacterController
    {
        private readonly List<CharacterSlot> _characters = [];

        public CharacterHandle Create(float x, float y, float width, float height)
        {
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidatePositive(width, nameof(width));
            ValidatePositive(height, nameof(height));

            CharacterController controller = new(grid, new Vector2(x, y), new Vector2(width, height));
            physics?.RegisterCharacterProxy(controller);
            CharacterState state = Snapshot(controller, default);
            _characters.Add(new CharacterSlot(controller, state));
            return new CharacterHandle(_characters.Count - 1);
        }

        public CharacterState SetPosition(CharacterHandle handle, float x, float y)
        {
            CharacterSlot slot = GetSlot(handle);
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));

            slot.Controller.SetPosition(new Vector2(x, y));
            CharacterState state = Snapshot(slot.Controller, default);
            slot.State = state;
            return state;
        }

        public CharacterState Move(CharacterHandle handle, float dx, float dy)
        {
            CharacterSlot slot = GetSlot(handle);
            ValidateFinite(dx, nameof(dx));
            ValidateFinite(dy, nameof(dy));

            commands.Enqueue(ScriptCommandTarget.Physics, ScriptCommand.MoveCharacter(handle, dx, dy));
            return slot.State;
        }

        public CharacterState MoveNow(CharacterHandle handle, float dx, float dy)
        {
            CharacterSlot slot = GetSlot(handle);
            Vector2 desired = new(dx, dy);
            CharacterCollisionInfo info;
            if (physics is null)
            {
                slot.Controller.Move(in desired, out info);
            }
            else
            {
                physics.MoveCharacter(slot.Controller, in desired, out info);
            }

            CharacterState state = Snapshot(slot.Controller, in info);
            slot.State = state;
            return state;
        }

        public CharacterState GetState(CharacterHandle handle)
        {
            return GetSlot(handle).State;
        }

        public void Reset()
        {
            _characters.Clear();
        }

        private CharacterSlot GetSlot(CharacterHandle handle)
        {
            return (uint)handle.Value < (uint)_characters.Count
                ? _characters[handle.Value]
                : throw new ArgumentOutOfRangeException(nameof(handle), handle, "未知角色控制器句柄。");
        }

        private CharacterState Snapshot(CharacterController controller, in CharacterCollisionInfo collision)
        {
            float slope = collision == default ? (controller.IsGrounded ? controller.SlopeAngle : 0f) : collision.SlopeAngle;
            bool grounded = collision == default ? controller.IsGrounded : collision.IsGrounded;
            bool wallLeft = collision == default ? controller.IsTouchingWallLeft : collision.HitWallLeft;
            bool wallRight = collision == default ? controller.IsTouchingWallRight : collision.HitWallRight;
            bool ceiling = collision != default && collision.HitCeiling;
            Vector2 requested = collision == default ? default : collision.RequestedDelta;
            Vector2 applied = collision == default ? default : collision.AppliedDelta;
            float dt = time?.FixedStep ?? 0f;
            float velocityX = dt > 0f ? applied.X / dt : applied.X;
            float velocityY = dt > 0f ? applied.Y / dt : applied.Y;
            Vector2 normal = grounded ? new Vector2(-MathF.Sin(slope), -MathF.Cos(slope)) : default;

            return new CharacterState(
                controller.Position.X,
                controller.Position.Y,
                controller.Size.X,
                controller.Size.Y,
                grounded,
                wallLeft,
                wallRight,
                ceiling,
                velocityX,
                velocityY,
                requested.X,
                requested.Y,
                applied.X,
                applied.Y,
                normal.X,
                normal.Y,
                slope);
        }

        private static void ValidateFinite(float value, string name)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "参数必须是有限数值。");
            }
        }

        private static void ValidatePositive(float value, string name)
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(name, value, "参数必须是有限正数。");
            }
        }

        private sealed class CharacterSlot(CharacterController controller, CharacterState state)
        {
            public CharacterController Controller { get; } = controller;

            public CharacterState State { get; set; } = state;
        }
    }
}
