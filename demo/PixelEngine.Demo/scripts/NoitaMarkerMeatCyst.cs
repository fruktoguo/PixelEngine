using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>复现 Meat biome cyst 的生成、可破坏本体与 pusblob 死亡载荷。</summary>
internal sealed class NoitaMarkerMeatCyst : Behaviour
{
    private float _elapsed;
    private bool _deathPending;
    private uint _sequence;

    public float X { get; private set; }

    public float Y { get; private set; }

    public float Rotation { get; private set; }

    public float Health { get; private set; }

    public float MaxHealth { get; private set; }

    public bool IsPopulated { get; private set; }

    public bool IsDead { get; private set; }

    public int SpawnedPusBlobCount { get; private set; }

    public static bool Supports(string function)
    {
        return function == "spawn_cyst";
    }

    public void Bind(in NoitaWangMarkerAnchor anchor, ulong worldSeed)
    {
        X = anchor.WorldX + 5f;
        Y = anchor.WorldY + 5f;
        _sequence = StableSeed(worldSeed, anchor.WorldX, anchor.WorldY);
        IsPopulated = NextUnit() >= 0.3f;
        Rotation = NextUnit() * MathF.Tau;
        MaxHealth = 1f;
        Health = MaxHealth;
        IsDead = false;
        SpawnedPusBlobCount = 0;
        _deathPending = false;
        Enabled = IsPopulated;
    }

    public bool ApplyDamage(float amount)
    {
        if (!IsPopulated || IsDead || !float.IsFinite(amount) || amount <= 0f)
        {
            return false;
        }

        Health = MathF.Max(0f, Health - amount);
        if (Health <= 0f)
        {
            IsDead = true;
            _deathPending = true;
        }

        return true;
    }

    public bool TryHitSegment(float x0, float y0, float x1, float y1, float damage, out float hitX, out float hitY)
    {
        hitX = X;
        hitY = Y - 3f;
        if (!IsPopulated || IsDead ||
            !TryIntersectAabb(x0, y0, x1, y1, X - 6f, Y - 10f, 12f, 14f, out hitX, out hitY))
        {
            return false;
        }

        _ = ApplyDamage(damage);
        return true;
    }

    protected override void OnUpdate(float dt)
    {
        if (_deathPending)
        {
            Context.World.Explode(X, Y - 3f, 22, 32.5f);
            MaterialId pus = Context.Materials.Resolve("pus");
            if (pus.IsValid)
            {
                Context.Cells.Paint((int)MathF.Round(X), (int)MathF.Round(Y - 3f), 10, pus);
            }

            float angle = NextUnit() * MathF.Tau;
            float speed = 90f + (NextUnit() * 25f);
            Entity blobEntity = Context.Scene.CreateEntity();
            NoitaMarkerPusBlob blob = blobEntity.AddComponent<NoitaMarkerPusBlob>();
            blob.Bind(X, Y - 3f, MathF.Cos(angle) * speed, MathF.Sin(angle) * speed);
            SpawnedPusBlobCount = 1;
            _deathPending = false;
            Enabled = false;
            return;
        }

        if (!IsPopulated || IsDead || !float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        _elapsed += dt;
        Draw();
    }

    private void Draw()
    {
        Point2F center = Context.Camera.WorldToScreen(X, Y - 3f);
        float scale = MathF.Max(1f, Context.Camera.Zoom);
        float width = 12f * scale;
        float height = 14f * scale;
        float pulse = 0.82f + (MathF.Sin(_elapsed * 2.8f) * 0.12f);
        Context.Overlay.SolidRectangle(center.X - (width * 0.5f), center.Y - (height * 0.5f), width, height, ScaleAlpha(0xFF_0F_5A_FFu, pulse));
        Context.Overlay.OutlineRectangle(center.X - (width * 0.5f), center.Y - (height * 0.5f), width, height, 1.5f, 0xFF_50_A0_FFu);
        Context.Lighting.AddPointLight(X, Y - 6f, 100f, 0xFF_0F_5A_FFu, 0.72f * pulse);
    }

    private float NextUnit()
    {
        return (NextRandom() & 0x00FF_FFFFu) / 16777215f;
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

    private static uint StableSeed(ulong worldSeed, long x, long y)
    {
        ulong value = worldSeed ^ (unchecked((ulong)x) * 0x9E37_79B1_85EB_CA87UL);
        value ^= unchecked((ulong)y) * 0xC2B2_AE3D_27D4_EB4FUL;
        return (uint)(value ^ (value >> 32));
    }

    private static bool TryIntersectAabb(
        float x0,
        float y0,
        float x1,
        float y1,
        float minX,
        float minY,
        float width,
        float height,
        out float hitX,
        out float hitY)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float tMin = 0f;
        float tMax = 1f;
        if (!ClipAxis(x0, dx, minX, minX + width, ref tMin, ref tMax) ||
            !ClipAxis(y0, dy, minY, minY + height, ref tMin, ref tMax))
        {
            hitX = 0f;
            hitY = 0f;
            return false;
        }

        hitX = x0 + (dx * tMin);
        hitY = y0 + (dy * tMin);
        return true;
    }

    private static bool ClipAxis(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(direction) <= 0.0001f)
        {
            return origin >= min && origin <= max;
        }

        float inverse = 1f / direction;
        float t0 = (min - origin) * inverse;
        float t1 = (max - origin) * inverse;
        if (t0 > t1)
        {
            (t0, t1) = (t1, t0);
        }

        tMin = MathF.Max(tMin, t0);
        tMax = MathF.Min(tMax, t1);
        return tMin <= tMax;
    }

    private static uint ScaleAlpha(uint bgra, float multiplier)
    {
        byte alpha = (byte)(bgra >> 24);
        byte scaled = (byte)Math.Clamp((int)MathF.Round(alpha * multiplier), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)scaled << 24);
    }
}

/// <summary>meat cyst 死亡时装载的 pusblob projectile。</summary>
internal sealed class NoitaMarkerPusBlob : Behaviour
{
    private PlayerController? _player;
    private PlayerHealth? _playerHealth;
    private float _vx;
    private float _vy;
    private float _elapsed;

    public float X { get; private set; }

    public float Y { get; private set; }

    public void Bind(float x, float y, float vx, float vy)
    {
        X = x;
        Y = y;
        _vx = vx;
        _vy = vy;
        Enabled = true;
    }

    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        float oldX = X;
        float oldY = Y;
        _elapsed += dt;
        _vy += 10f * dt;
        float moveX = _vx * dt;
        float moveY = _vy * dt;
        float distance = MathF.Sqrt((moveX * moveX) + (moveY * moveY));
        if (distance > 0.001f &&
            Context.Solids.Raycast(X, Y, moveX / distance, moveY / distance, distance, out RaycastHit hit) &&
            hit.Hit)
        {
            Explode();
            return;
        }

        X += moveX;
        Y += moveY;
        ResolvePlayer();
        if (_player is not null && SegmentDistanceSquared(oldX, oldY, X, Y, _player.CenterX, _player.CenterY) <= 25f)
        {
            _playerHealth?.ApplyExternalDamage(8.75f);
            Explode();
            return;
        }

        Point2F point = Context.Camera.WorldToScreen(X, Y);
        float size = MathF.Max(2f, Context.Camera.Zoom * 2f);
        Context.Overlay.SolidRectangle(point.X - size, point.Y - size, size * 2f, size * 2f, 0xFF_78_CD_63u);
        Context.Lighting.AddPointLight(X, Y, 30f, 0xFF_BD_CD_63u, 0.45f);
        if (_elapsed >= (130f / 60f))
        {
            Explode();
        }
    }

    private void Explode()
    {
        Context.World.DamageCircle(X, Y, 14, 10f, falloff: true);
        MaterialId pus = Context.Materials.Resolve("pus");
        if (pus.IsValid)
        {
            Context.Cells.Paint((int)MathF.Round(X), (int)MathF.Round(Y), 9, pus);
        }

        Entity.Destroy();
        Enabled = false;
    }

    private void ResolvePlayer()
    {
        if (_player is null || !_player.Enabled)
        {
            _ = Context.Scene.TryGetFirstComponent(out _player);
        }

        if (_playerHealth is null || !_playerHealth.Enabled)
        {
            _ = Context.Scene.TryGetFirstComponent(out _playerHealth);
        }
    }

    private static float SegmentDistanceSquared(float x0, float y0, float x1, float y1, float px, float py)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float lengthSquared = (dx * dx) + (dy * dy);
        float t = lengthSquared <= 0.0001f
            ? 0f
            : Math.Clamp((((px - x0) * dx) + ((py - y0) * dy)) / lengthSquared, 0f, 1f);
        float offsetX = px - (x0 + (dx * t));
        float offsetY = py - (y0 + (dy * t));
        return (offsetX * offsetX) + (offsetY * offsetY);
    }
}
