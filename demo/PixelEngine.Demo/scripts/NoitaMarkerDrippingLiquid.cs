using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>按来源 ParticleEmitter 参数生成真实液体粒子的滴水装置。</summary>
internal sealed class NoitaMarkerDrippingLiquid : Behaviour
{
    private MaterialId _material = MaterialId.Invalid;
    private float _realParticleTimer;
    private float _cosmeticTimer;
    private uint _sequence;

    public long WorldX { get; private set; }

    public long WorldY { get; private set; }

    public int RealParticleCount { get; private set; }

    public static bool Supports(string function)
    {
        return function == "spawn_waterspout";
    }

    public void Bind(in NoitaWangMarkerAnchor anchor)
    {
        WorldX = anchor.WorldX;
        WorldY = anchor.WorldY;
        _sequence = StableSeed(anchor.WorldX, anchor.WorldY);
        _realParticleTimer = NextRealInterval();
        _cosmeticTimer = NextCosmeticInterval();
        RealParticleCount = 0;
        Enabled = true;
    }

    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        if (!_material.IsValid)
        {
            _material = Context.Materials.Resolve("water");
            if (!_material.IsValid)
            {
                return;
            }
        }

        _realParticleTimer -= dt;
        if (_realParticleTimer <= 0f)
        {
            _realParticleTimer += NextRealInterval();
            if ((NextRandom() & 1u) != 0u)
            {
                SpawnRealParticle();
            }
        }

        _cosmeticTimer -= dt;
        if (_cosmeticTimer <= 0f)
        {
            _cosmeticTimer += NextCosmeticInterval();
            DrawCosmeticDrop();
        }

        Context.Lighting.AddPointLight(WorldX, WorldY, 18f, 0xFF_E8_90_48u, 0.2f);
    }

    private void SpawnRealParticle()
    {
        float x = WorldX + RandomRange(-3f, 3f);
        float y = WorldY + RandomRange(-1f, 1f);
        float vx = RandomRange(-3f, 3f);
        float vy = RandomRange(-2f, 10f);
        ushort lifetime = (ushort)Math.Clamp((int)MathF.Round(RandomRange(0.6f, 1.3f) * 60f), 1, ushort.MaxValue);
        Context.Particles.Spawn(new ParticleSpawnDesc(x, y, vx, vy, _material, lifetime));
        RealParticleCount++;
    }

    private void DrawCosmeticDrop()
    {
        Point2F point = Context.Camera.WorldToScreen(
            WorldX + RandomRange(-3f, 3f),
            WorldY + RandomRange(-1f, 1f));
        float size = MathF.Max(1.5f, Context.Camera.Zoom * 0.8f);
        Context.Overlay.SolidRectangle(point.X - (size * 0.5f), point.Y, size, size * 2f, 0xC0_FF_98_50u);
    }

    private float NextRealInterval()
    {
        return RandomRange(70f, 100f) / 60f;
    }

    private float NextCosmeticInterval()
    {
        return RandomRange(20f, 60f) / 60f;
    }

    private float RandomRange(float min, float max)
    {
        float t = (NextRandom() & 0x00FF_FFFFu) / 16777215f;
        return min + ((max - min) * t);
    }

    private uint NextRandom()
    {
        uint value = _sequence + 0x9E37_79B9u;
        _sequence = value;
        value ^= value >> 16;
        value *= 0x7FEB_352Du;
        value ^= value >> 15;
        value *= 0x846C_A68Bu;
        return value ^ (value >> 16);
    }

    private static uint StableSeed(long x, long y)
    {
        ulong value = unchecked((ulong)x) * 0x9E37_79B1_85EB_CA87UL;
        value ^= unchecked((ulong)y) * 0xC2B2_AE3D_27D4_EB4FUL;
        return (uint)(value ^ (value >> 32));
    }
}
