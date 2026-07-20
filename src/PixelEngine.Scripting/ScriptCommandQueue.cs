using System.Buffers;
using PixelEngine.Core;

namespace PixelEngine.Scripting;

/// <summary>
/// 脚本命令在相位 1 入队后，按目标子系统分桶延迟到相位 2 消费。
/// </summary>
internal enum ScriptCommandTarget
{
    /// <summary>格子写入与材质修改。</summary>
    CellWrite,
    /// <summary>粒子生成与发射。</summary>
    Particle,
    /// <summary>刚体与角色物理操作。</summary>
    Physics,
}

/// <summary>
/// 脚本侧可提交的模拟/物理命令种类。
/// </summary>
internal enum ScriptCommandKind
{
    SetCell,
    Paint,
    DamageCircle,
    DamageBeam,
    AddHeat,
    Explode,
    SpawnParticle,
    BurstParticles,
    EmitParticles,
    CreateBodyFromRegion,
    ApplyImpulse,
    ApplyRadialImpulse,
    DestroyBody,
    MoveCharacter,
}

/// <summary>
/// 单条脚本命令的紧凑载荷；各工厂方法将语义映射到统一字段布局。
/// </summary>
internal readonly record struct ScriptCommand(
    ScriptCommandKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    MaterialId Material,
    ParticleSpawnDesc Particle,
    ParticleEmit Emit,
    BodyHandle Body,
    CharacterHandle Character,
    float A,
    float B,
    DamageKind DamageKind)
{
    public static ScriptCommand SetCell(int x, int y, MaterialId material)
    {
        return new ScriptCommand(ScriptCommandKind.SetCell, x, y, 0, 0, material, default, default, default, default, 0, 0, default);
    }

    public static ScriptCommand Paint(int x, int y, int radius, MaterialId material)
    {
        return new ScriptCommand(ScriptCommandKind.Paint, x, y, radius, 0, material, default, default, default, default, 0, 0, default);
    }

    public static ScriptCommand Explode(int x, int y, int radius, float force, float jitter)
    {
        return new ScriptCommand(ScriptCommandKind.Explode, x, y, radius, 0, default, default, default, default, default, force, jitter, DamageKind.Impact);
    }

    public static ScriptCommand DamageCircle(int x, int y, int radius, ushort damage, bool falloff, DamageKind kind)
    {
        return new ScriptCommand(ScriptCommandKind.DamageCircle, x, y, radius, damage, default, default, default, default, default, falloff ? 1f : 0f, 0, kind);
    }

    public static ScriptCommand DamageBeam(int x, int y, float dirX, float dirY, int length, ushort damagePerCell, DamageKind kind)
    {
        return new ScriptCommand(ScriptCommandKind.DamageBeam, x, y, length, damagePerCell, default, default, default, default, default, dirX, dirY, kind);
    }

    public static ScriptCommand AddHeat(int x, int y, int radius, float deltaCelsius)
    {
        return new ScriptCommand(ScriptCommandKind.AddHeat, x, y, radius, 0, default, default, default, default, default, deltaCelsius, 0, DamageKind.Heat);
    }

    public static ScriptCommand SpawnParticle(in ParticleSpawnDesc particle)
    {
        return new ScriptCommand(ScriptCommandKind.SpawnParticle, 0, 0, 0, 0, default, particle, default, default, default, 0, 0, default);
    }

    public static ScriptCommand BurstParticles(float x, float y, MaterialId material, int count, float speed)
    {
        return new ScriptCommand(
            ScriptCommandKind.BurstParticles,
            0,
            0,
            count,
            0,
            default,
            new ParticleSpawnDesc(x, y, 0, 0, material, EngineConstants.ParticleMaxLifetimeTicks),
            default,
            default,
            default,
            speed,
            0,
            default);
    }

    public static ScriptCommand EmitParticles(in ParticleEmit emit)
    {
        return new ScriptCommand(ScriptCommandKind.EmitParticles, 0, 0, 0, 0, default, default, emit, default, default, 0, 0, default);
    }

    public static ScriptCommand CreateBodyFromRegion(BodyHandle body, int x, int y, int width, int height)
    {
        return new ScriptCommand(ScriptCommandKind.CreateBodyFromRegion, x, y, width, height, default, default, default, body, default, 0, 0, default);
    }

    public static ScriptCommand ApplyImpulse(BodyHandle body, float impulseX, float impulseY)
    {
        return new ScriptCommand(ScriptCommandKind.ApplyImpulse, 0, 0, 0, 0, default, default, default, body, default, impulseX, impulseY, default);
    }

    public static ScriptCommand ApplyRadialImpulse(int x, int y, int radius, float force)
    {
        return new ScriptCommand(ScriptCommandKind.ApplyRadialImpulse, x, y, radius, 0, default, default, default, default, default, force, 0, default);
    }

    public static ScriptCommand DestroyBody(BodyHandle body)
    {
        return new ScriptCommand(ScriptCommandKind.DestroyBody, 0, 0, 0, 0, default, default, default, body, default, 0, 0, default);
    }

    public static ScriptCommand MoveCharacter(CharacterHandle character, float dx, float dy)
    {
        return new ScriptCommand(ScriptCommandKind.MoveCharacter, 0, 0, 0, 0, default, default, default, default, character, dx, dy, default);
    }
}

/// <summary>
/// 线程本地分桶的脚本命令队列；相位 1 入队，相位 2 按目标一次性排空。
/// </summary>
internal sealed class ScriptCommandQueue : IDisposable
{
    private const int TargetCount = 3;
    private readonly ThreadLocal<Bucket>[] _buckets;
    private readonly List<Bucket>[] _registeredBuckets;
    private readonly Lock _registryGate = new();

    public ScriptCommandQueue()
    {
        _buckets = new ThreadLocal<Bucket>[TargetCount];
        _registeredBuckets = new List<Bucket>[TargetCount];
        for (int i = 0; i < _buckets.Length; i++)
        {
            int target = i;
            _registeredBuckets[i] = new List<Bucket>(capacity: 1);
            // trackAllValues=false：仅跟踪当前线程桶，降低 ThreadLocal 开销。
            _buckets[i] = new ThreadLocal<Bucket>(() => CreateBucket(target), trackAllValues: false);
        }
    }

    /// <summary>
    /// 将命令入队到指定目标子系统的当前线程桶。
    /// </summary>
    public void Enqueue(ScriptCommandTarget target, in ScriptCommand command)
    {
        ValidateTarget(target);
        _buckets[(int)target].Value!.Enqueue(in command);
    }

    /// <summary>
    /// 统计指定目标下所有已注册桶中的命令总数。
    /// </summary>
    public int Count(ScriptCommandTarget target)
    {
        ValidateTarget(target);
        int count = 0;
        List<Bucket> buckets = _registeredBuckets[(int)target];
        lock (_registryGate)
        {
            for (int i = 0; i < buckets.Count; i++)
            {
                count += buckets[i].Count;
            }
        }

        return count;
    }

    /// <summary>
    /// 排空指定目标的全部命令到调用方缓冲；缓冲不足时抛异常。
    /// </summary>
    public int DrainTo(ScriptCommandTarget target, Span<ScriptCommand> destination)
    {
        ValidateTarget(target);
        int total = Count(target);
        if (destination.Length < total)
        {
            throw new ArgumentException("目标缓冲不足以容纳全部脚本命令。", nameof(destination));
        }

        int written = 0;
        List<Bucket> buckets = _registeredBuckets[(int)target];
        lock (_registryGate)
        {
            for (int i = 0; i < buckets.Count; i++)
            {
                written += buckets[i].DrainTo(destination[written..]);
            }
        }

        return written;
    }

    /// <summary>
    /// 丢弃全部目标下尚未落地的命令；仅允许在 Hosting 的 world/session 替换安全点调用。
    /// </summary>
    public void Clear()
    {
        lock (_registryGate)
        {
            for (int target = 0; target < _registeredBuckets.Length; target++)
            {
                List<Bucket> buckets = _registeredBuckets[target];
                for (int i = 0; i < buckets.Count; i++)
                {
                    buckets[i].Clear();
                }
            }
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            List<Bucket> buckets = _registeredBuckets[i];
            for (int j = 0; j < buckets.Count; j++)
            {
                buckets[j].Dispose();
            }

            buckets.Clear();
            _buckets[i].Dispose();
        }
    }

    /// <summary>
    /// 线程首次访问某目标时创建桶并注册到全局列表，供跨线程 Drain 汇总。
    /// </summary>
    private Bucket CreateBucket(int target)
    {
        Bucket bucket = new();
        lock (_registryGate)
        {
            _registeredBuckets[target].Add(bucket);
        }

        return bucket;
    }

    private static void ValidateTarget(ScriptCommandTarget target)
    {
        if ((uint)target >= TargetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "未知脚本命令目标。");
        }
    }

    /// <summary>
    /// 单线程命令桶；使用 ArrayPool 动态扩容并在 Drain 后清零槽位。
    /// </summary>
    private sealed class Bucket : IDisposable
    {
        private ScriptCommand[] _commands = ArrayPool<ScriptCommand>.Shared.Rent(4);

        public int Count { get; private set; }

        public void Enqueue(in ScriptCommand command)
        {
            EnsureCapacity(Count + 1);
            _commands[Count++] = command;
        }

        public int DrainTo(Span<ScriptCommand> destination)
        {
            int count = Count;
            _commands.AsSpan(0, count).CopyTo(destination);
            _commands.AsSpan(0, count).Clear();
            Count = 0;
            return count;
        }

        public void Clear()
        {
            _commands.AsSpan(0, Count).Clear();
            Count = 0;
        }

        public void Dispose()
        {
            ScriptCommand[] commands = _commands;
            _commands = [];
            Count = 0;
            ArrayPool<ScriptCommand>.Shared.Return(commands, clearArray: true);
        }

        private void EnsureCapacity(int required)
        {
            if (_commands.Length >= required)
            {
                return;
            }

            ScriptCommand[] replacement = ArrayPool<ScriptCommand>.Shared.Rent(Math.Max(required, _commands.Length * 2));
            _commands.AsSpan(0, Count).CopyTo(replacement);
            ArrayPool<ScriptCommand>.Shared.Return(_commands, clearArray: true);
            _commands = replacement;
        }
    }
}
