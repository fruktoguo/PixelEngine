using System.Buffers;

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
    SpawnParticle,
    BurstParticles,
    CreateBodyFromRegion,
    ApplyImpulse,
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
            new ParticleSpawnDesc(x, y, 0, 0, material, 0),
            default,
            default,
            speed,
            0);
    }

    public static ScriptCommand CreateBodyFromRegion(int x, int y, int width, int height)
    {
        return new ScriptCommand(ScriptCommandKind.CreateBodyFromRegion, x, y, width, height, default, default, default, default, 0, 0);
    }

    public static ScriptCommand ApplyImpulse(BodyHandle body, float impulseX, float impulseY)
    {
        return new ScriptCommand(ScriptCommandKind.ApplyImpulse, 0, 0, 0, 0, default, default, body, default, impulseX, impulseY);
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

    public ScriptCommandQueue()
    {
        _buckets = new ThreadLocal<Bucket>[TargetCount];
        for (int i = 0; i < _buckets.Length; i++)
        {
            _buckets[i] = new ThreadLocal<Bucket>(static () => new Bucket(), trackAllValues: true);
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
        foreach (Bucket bucket in _buckets[(int)target].Values)
        {
            count += bucket.Count;
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
        foreach (Bucket bucket in _buckets[(int)target].Values)
        {
            written += bucket.DrainTo(destination[written..]);
        }

        return written;
    }

    public void Dispose()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            foreach (Bucket bucket in _buckets[i].Values)
            {
                bucket.Dispose();
            }

            _buckets[i].Dispose();
        }
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
