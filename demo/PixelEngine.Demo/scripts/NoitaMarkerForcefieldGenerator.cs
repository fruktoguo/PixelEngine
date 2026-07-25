using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>复现 Snowcastle forcefield generator 的近距关闭、能量盾与可破坏本体。</summary>
internal sealed class NoitaMarkerForcefieldGenerator : Behaviour
{
    private const float ShieldRadius = 20f;
    private const float ProximityDisableRadius = 38f;
    private readonly NoitaMarkerEnemy[] _enemyTargets = new NoitaMarkerEnemy[128];
    private PlayerController? _player;
    private float _elapsed;
    private bool _deathPending;

    public float X { get; private set; }

    public float Y { get; private set; }

    public float Health { get; private set; }

    public float MaxHealth { get; private set; }

    public float ShieldEnergy { get; private set; }

    public float MaxShieldEnergy { get; private set; }

    public bool IsShieldActive { get; private set; }

    public bool IsPopulated { get; private set; }

    public bool IsDead { get; private set; }

    public static bool Supports(string function)
    {
        return function == "spawn_forcefield_generator";
    }

    public void Bind(in NoitaWangMarkerAnchor anchor, ulong worldSeed)
    {
        X = anchor.WorldX;
        Y = anchor.WorldY - 2f;
        MaxHealth = 30f;
        Health = MaxHealth;
        MaxShieldEnergy = 3f;
        ShieldEnergy = 2f;
        bool safe = !(anchor.WorldX is >= 125 and <= 249 && anchor.WorldY is >= 5118 and <= 5259) &&
            anchor.WorldY <= 6100;
        IsPopulated = safe && StableUnit(worldSeed, anchor.WorldX, anchor.WorldY) >= (2f / 3f);
        IsShieldActive = IsPopulated;
        IsDead = false;
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
            IsShieldActive = false;
            _deathPending = true;
        }

        return true;
    }

    public bool TryHitSegment(float x0, float y0, float x1, float y1, float damage, out float hitX, out float hitY)
    {
        hitX = X + 8f;
        hitY = Y - 8f;
        if (!IsPopulated || IsDead)
        {
            return false;
        }

        if (IsShieldActive &&
            TryIntersectCircle(x0, y0, x1, y1, X + 8f, Y - 8f, ShieldRadius, out hitX, out hitY) &&
            IsShieldArc(hitX - (X + 8f), hitY - (Y - 8f)))
        {
            ShieldEnergy = MathF.Max(0f, ShieldEnergy - MathF.Max(0.1f, damage / 25f));
            if (ShieldEnergy <= 0f)
            {
                IsShieldActive = false;
            }

            return true;
        }

        if (!TryIntersectAabb(x0, y0, x1, y1, X, Y - 16f, 16f, 16f, out hitX, out hitY))
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
            Context.World.Explode(X + 8f, Y - 8f, 16, 65f);
            _deathPending = false;
            Enabled = false;
            return;
        }

        if (!IsPopulated || IsDead || !float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        _elapsed += dt;
        bool nearby = HasNearbyActor();
        if (nearby)
        {
            IsShieldActive = false;
        }
        else
        {
            ShieldEnergy = MathF.Min(MaxShieldEnergy, ShieldEnergy + (0.25f * dt));
            IsShieldActive = ShieldEnergy > 0f;
        }

        Draw();
    }

    private bool HasNearbyActor()
    {
        float centerX = X + 5f;
        float centerY = Y - 10f;
        if (_player is null || !_player.Enabled)
        {
            _ = Context.Scene.TryGetFirstComponent(out _player);
        }

        if (_player is not null && DistanceSquared(centerX, centerY, _player.CenterX, _player.CenterY) <= ProximityDisableRadius * ProximityDisableRadius)
        {
            return true;
        }

        int count = Context.Scene.CollectComponents(_enemyTargets);
        for (int i = 0; i < count; i++)
        {
            NoitaMarkerEnemy enemy = _enemyTargets[i];
            if (!enemy.IsDead && DistanceSquared(centerX, centerY, enemy.X, enemy.Y) <= ProximityDisableRadius * ProximityDisableRadius)
            {
                return true;
            }
        }

        return false;
    }

    private void Draw()
    {
        Point2F body = Context.Camera.WorldToScreen(X + 8f, Y - 8f);
        float scale = MathF.Max(1f, Context.Camera.Zoom);
        float size = 16f * scale;
        float half = size * 0.5f;
        Context.Overlay.SolidRectangle(body.X - half, body.Y - half, size, size, 0xFF_90_70_58u);
        Context.Overlay.OutlineRectangle(body.X - half, body.Y - half, size, size, 1.5f, 0xFF_E6_BE_96u);
        Context.Overlay.SolidRectangle(body.X - half, body.Y - half - 4f, size * (Health / MaxHealth), 2f, 0xFF_48_D8_60u);
        if (IsShieldActive)
        {
            float radius = ShieldRadius * scale;
            uint color = ScaleAlpha(0xFF_E6_BE_96u, 0.5f + (MathF.Sin(_elapsed * 5f) * 0.08f));
            const int Segments = 28;
            float previousX = body.X + radius;
            float previousY = body.Y;
            for (int i = 1; i <= Segments; i++)
            {
                float angle = MathF.Tau * i / Segments;
                float nextX = body.X + (MathF.Cos(angle) * radius);
                float nextY = body.Y + (MathF.Sin(angle) * radius);
                if (IsShieldArc(MathF.Cos(angle - (MathF.Tau / Segments)), MathF.Sin(angle - (MathF.Tau / Segments))) &&
                    IsShieldArc(MathF.Cos(angle), MathF.Sin(angle)))
                {
                    Context.Overlay.Line(previousX, previousY, nextX, nextY, 1.5f, color);
                }

                previousX = nextX;
                previousY = nextY;
            }

            Context.Lighting.AddPointLight(X + 8f, Y - 8f, 60f, 0xFF_E6_BE_96u, 0.7f);
        }
    }

    private static bool TryIntersectCircle(
        float x0,
        float y0,
        float x1,
        float y1,
        float centerX,
        float centerY,
        float radius,
        out float hitX,
        out float hitY)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float a = (dx * dx) + (dy * dy);
        if (a <= 0.0001f)
        {
            hitX = x0;
            hitY = y0;
            return DistanceSquared(x0, y0, centerX, centerY) <= radius * radius;
        }

        float originX = x0 - centerX;
        float originY = y0 - centerY;
        float b = 2f * ((originX * dx) + (originY * dy));
        float c = (originX * originX) + (originY * originY) - (radius * radius);
        float discriminant = (b * b) - (4f * a * c);
        if (discriminant < 0f)
        {
            hitX = 0f;
            hitY = 0f;
            return false;
        }

        float root = MathF.Sqrt(discriminant);
        float inverse = 0.5f / a;
        float t = (-b - root) * inverse;
        if (t is < 0f or > 1f)
        {
            t = (-b + root) * inverse;
            if (t is < 0f or > 1f)
            {
                hitX = 0f;
                hitY = 0f;
                return false;
            }
        }

        hitX = x0 + (dx * t);
        hitY = y0 + (dy * t);
        return true;
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

    private static float StableUnit(ulong worldSeed, long x, long y)
    {
        ulong value = worldSeed ^ (unchecked((ulong)x) * 0x9E37_79B1_85EB_CA87UL);
        value ^= unchecked((ulong)y) * 0xC2B2_AE3D_27D4_EB4FUL;
        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        value ^= value >> 31;
        return (value & 0x00FF_FFFFUL) / 16777215f;
    }

    private static bool IsShieldArc(float offsetX, float offsetY)
    {
        // 来源 child rotation=-90deg、sector=320deg，等价于在朝下方向保留 40deg 开口。
        float angle = MathF.Atan2(offsetY, offsetX);
        float distanceFromDown = MathF.Abs(NormalizeRadians(angle - (MathF.PI * 0.5f)));
        return distanceFromDown > (20f * MathF.PI / 180f);
    }

    private static float NormalizeRadians(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    private static float DistanceSquared(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        return (dx * dx) + (dy * dy);
    }

    private static uint ScaleAlpha(uint bgra, float multiplier)
    {
        byte alpha = (byte)(bgra >> 24);
        byte scaled = (byte)Math.Clamp((int)MathF.Round(alpha * multiplier), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)scaled << 24);
    }
}
