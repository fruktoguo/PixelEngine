using System.Buffers;
using PixelEngine.Core;

namespace PixelEngine.Scripting;

internal enum ScriptCommandTarget
{
    CellWrite,
    Particle,
    Physics,
}

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
    CreateBodyFromRegion,
    ApplyImpulse,
    ApplyRadialImpulse,
    DestroyBody,
    MoveCharacter,
}

internal readonly record struct ScriptCommand(
    ScriptCommandKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    MaterialId Material,
    ParticleSpawnDesc Particle,
    BodyHandle Body,
    CharacterHandle Character,
    float A,
    float B)
{
    public static ScriptCommand SetCell(int x, int y, MaterialId material)
    {
        return new ScriptCommand(ScriptCommandKind.SetCell, x, y, 0, 0, material, default, default, default, 0, 0);
    }

    public static ScriptCommand Paint(int x, int y, int radius, MaterialId material)
    {
        return new ScriptCommand(ScriptCommandKind.Paint, x, y, radius, 0, material, default, default, default, 0, 0);
    }

    public static ScriptCommand Explode(int x, int y, int radius, float force, float jitter)
    {
        return new ScriptCommand(ScriptCommandKind.Explode, x, y, radius, 0, default, default, default, default, force, jitter);
    }

    public static ScriptCommand DamageCircle(int x, int y, int radius, ushort damage, bool falloff)
    {
        return new ScriptCommand(ScriptCommandKind.DamageCircle, x, y, radius, damage, default, default, default, default, falloff ? 1f : 0f, 0);
    }

    public static ScriptCommand DamageBeam(int x, int y, float dirX, float dirY, int length, ushort damagePerCell)
    {
        return new ScriptCommand(ScriptCommandKind.DamageBeam, x, y, length, damagePerCell, default, default, default, default, dirX, dirY);
    }

    public static ScriptCommand AddHeat(int x, int y, int radius, float deltaCelsius)
    {
        return new ScriptCommand(ScriptCommandKind.AddHeat, x, y, radius, 0, default, default, default, default, deltaCelsius, 0);
    }

    public static ScriptCommand SpawnParticle(in ParticleSpawnDesc particle)
    {
        return new ScriptCommand(ScriptCommandKind.SpawnParticle, 0, 0, 0, 0, default, particle, default, default, 0, 0);
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
            speed,
            0);
    }

    public static ScriptCommand CreateBodyFromRegion(BodyHandle body, int x, int y, int width, int height)
    {
        return new ScriptCommand(ScriptCommandKind.CreateBodyFromRegion, x, y, width, height, default, default, body, default, 0, 0);
    }

    public static ScriptCommand ApplyImpulse(BodyHandle body, float impulseX, float impulseY)
    {
        return new ScriptCommand(ScriptCommandKind.ApplyImpulse, 0, 0, 0, 0, default, default, body, default, impulseX, impulseY);
    }

    public static ScriptCommand ApplyRadialImpulse(int x, int y, int radius, float force)
    {
        return new ScriptCommand(ScriptCommandKind.ApplyRadialImpulse, x, y, radius, 0, default, default, default, default, force, 0);
    }

    public static ScriptCommand DestroyBody(BodyHandle body)
    {
        return new ScriptCommand(ScriptCommandKind.DestroyBody, 0, 0, 0, 0, default, default, body, default, 0, 0);
    }

    public static ScriptCommand MoveCharacter(CharacterHandle character, float dx, float dy)
    {
        return new ScriptCommand(ScriptCommandKind.MoveCharacter, 0, 0, 0, 0, default, default, default, character, dx, dy);
    }
}

internal sealed class ScriptCommandQueue : IDisposable
{
    private const int TargetCount = 3;
    private readonly ThreadLocal<Bucket>[] _buckets;
    private readonly List<Bucket>[] _registeredBuckets;
    private readonly System.Threading.Lock _registryGate = new();

    public ScriptCommandQueue()
    {
        _buckets = new ThreadLocal<Bucket>[TargetCount];
        _registeredBuckets = new List<Bucket>[TargetCount];
        for (int i = 0; i < _buckets.Length; i++)
        {
            int target = i;
            _registeredBuckets[i] = new List<Bucket>(capacity: 1);
            _buckets[i] = new ThreadLocal<Bucket>(() => CreateBucket(target), trackAllValues: false);
        }
    }

    public void Enqueue(ScriptCommandTarget target, in ScriptCommand command)
    {
        ValidateTarget(target);
        _buckets[(int)target].Value!.Enqueue(in command);
    }

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
