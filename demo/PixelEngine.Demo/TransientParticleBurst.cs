using System.Runtime.CompilerServices;
using PixelEngine.Core;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

internal static class TransientParticleBurst
{
    private const ushort DefaultVisualLifetime = 60;
    private static readonly ConditionalWeakTable<Scene, TransientParticleBurstSystem> Systems = [];

    public static void Emit(IScriptContext context, float x, float y, int count, float speed, ushort lifetime = DefaultVisualLifetime)
    {
        ArgumentNullException.ThrowIfNull(context);
        count = Math.Clamp(count, 0, 48);
        if (count == 0)
        {
            return;
        }

        ushort safeLifetime = lifetime == 0 ? DefaultVisualLifetime : lifetime;
        TransientParticleBurstSystem system = Systems.GetValue(context.Scene, static scene =>
        {
            TransientParticleBurstSystem created = new();
            scene.RegisterSystem(created);
            return created;
        });
        system.Add(x, y, count, MathF.Max(0f, speed), safeLifetime);
    }

    public static int ActiveCount(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return Systems.TryGetValue(scene, out TransientParticleBurstSystem? system)
            ? system.ActiveCount
            : 0;
    }
}

/// <summary>
/// 纯视觉爆炸烟尘；只提交本帧 overlay 和短光照，不写入权威粒子池或 cell 网格。
/// </summary>
internal sealed class TransientParticleBurstSystem : ISystem
{
    private const float GravityCellsPerSecondSquared = 22f;
    private const float DragPerSecond = 0.72f;
    private const uint CoreColorBgra = 0xE8_F8_F4_C8;
    private const uint SmokeColorBgra = 0xB8_96_92_84;
    private const int MaxBursts = 16;

    private readonly Burst[] _bursts = new Burst[MaxBursts];
    public int ActiveCount { get; private set; }

    /// <summary>
    /// 最近一帧提交的 overlay 命令数量。
    /// </summary>
    public int LastOverlayCommandsSubmitted { get; private set; }

    public void Add(float x, float y, int count, float speedCellsPerSecond, ushort lifetimeTicks)
    {
        int index = ActiveCount < _bursts.Length ? ActiveCount++ : OldestBurstIndex();
        float simHz = (float)EngineConstants.DefaultSimHz;
        _bursts[index] = new Burst
        {
            X = x,
            Y = y,
            Speed = MathF.Max(0f, speedCellsPerSecond),
            Duration = Math.Clamp(lifetimeTicks / simHz, 0.05f, 2.5f),
            Count = Math.Clamp(count, 1, 48),
            Seed = Hash((int)MathF.Round(x * 16f), (int)MathF.Round(y * 16f), count),
        };
    }

    public void OnSimTick(IScriptContext context)
    {
    }

    public void OnFrame(IScriptContext context, float dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        LastOverlayCommandsSubmitted = 0;
        float visualDt = ResolveVisualDeltaSeconds(context, dt);
        int i = 0;
        while (i < ActiveCount)
        {
            _bursts[i].Elapsed += visualDt;
            if (_bursts[i].Elapsed >= _bursts[i].Duration)
            {
                RemoveAtSwapBack(i);
                continue;
            }

            LastOverlayCommandsSubmitted += DrawBurst(context, in _bursts[i]);
            i++;
        }
    }

    private static int DrawBurst(IScriptContext context, in Burst burst)
    {
        float t = burst.Elapsed / burst.Duration;
        float alpha = 1f - t;
        float scale = MathF.Max(1f, context.Camera.Zoom);
        int submitted = 0;
        for (int i = 0; i < burst.Count; i++)
        {
            float angle = ParticleAngle(burst, i);
            float speed = burst.Speed * ParticleSpeedScale(burst, i);
            float distance = speed * burst.Elapsed * MathF.Pow(DragPerSecond, burst.Elapsed);
            float worldX = burst.X + (MathF.Cos(angle) * distance);
            float worldY = burst.Y + (MathF.Sin(angle) * distance) + (0.5f * GravityCellsPerSecondSquared * burst.Elapsed * burst.Elapsed);
            Point2F screen = context.Camera.WorldToScreen(worldX, worldY);
            float size = MathF.Max(1.5f, scale * (2.5f - (1.4f * t)));
            context.Overlay.SolidRectangle(
                screen.X - (size * 0.5f),
                screen.Y - (size * 0.5f),
                size,
                size,
                FadeAlpha(i < 3 ? CoreColorBgra : SmokeColorBgra, alpha));
            submitted++;
        }

        context.Lighting.AddPointLight(burst.X, burst.Y, MathF.Max(4f, burst.Count * 0.45f) * (1.2f - (0.4f * t)), CoreColorBgra, 0.25f * alpha);
        return submitted;
    }

    private static float ResolveVisualDeltaSeconds(IScriptContext context, float fallbackDt)
    {
        float realDt = context.Time.RealDeltaTime;
        return float.IsFinite(realDt) && realDt > 0f
            ? realDt
            : MathF.Max(0f, fallbackDt);
    }

    private static float ParticleAngle(in Burst burst, int index)
    {
        float baseAngle = MathF.Tau * index / burst.Count;
        return baseAngle + ((((Hash(burst.Seed, index, 11) & 255) / 255f) - 0.5f) * 0.5f);
    }

    private static float ParticleSpeedScale(in Burst burst, int index)
    {
        return 0.45f + ((Hash(burst.Seed, index, 29) & 255) / 255f * 0.7f);
    }

    private int OldestBurstIndex()
    {
        int oldest = 0;
        float maxElapsed = _bursts[0].Elapsed;
        for (int i = 1; i < _bursts.Length; i++)
        {
            if (_bursts[i].Elapsed > maxElapsed)
            {
                maxElapsed = _bursts[i].Elapsed;
                oldest = i;
            }
        }

        return oldest;
    }

    private void RemoveAtSwapBack(int index)
    {
        int last = --ActiveCount;
        if (index != last)
        {
            _bursts[index] = _bursts[last];
        }

        _bursts[last] = default;
    }

    private static uint FadeAlpha(uint bgra, float alpha)
    {
        byte original = (byte)(bgra >> 24);
        byte faded = (byte)Math.Clamp((int)MathF.Round(original * Math.Clamp(alpha, 0f, 1f)), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)faded << 24);
    }

    private static int Hash(int x, int y, int salt)
    {
        unchecked
        {
            uint hash = ((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ ((uint)salt * 83492791u);
            hash ^= hash >> 13;
            hash *= 1274126177u;
            return (int)hash;
        }
    }

    private struct Burst
    {
        public float X;
        public float Y;
        public float Speed;
        public float Duration;
        public float Elapsed;
        public int Count;
        public int Seed;
    }
}
