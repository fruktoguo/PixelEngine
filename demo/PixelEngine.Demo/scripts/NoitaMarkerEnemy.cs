using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 由 Noita encounter marker 生成的 Demo 侧敌人；只消费引擎公开 Scene、Solid、Overlay、Light 与玩家 API。
/// </summary>
internal sealed class NoitaMarkerEnemy : Behaviour
{
    private PlayerController? _player;
    private PlayerHealth? _playerHealth;
    private float _contactCooldown;
    private float _elapsed;
    private uint _bodyColor;
    private uint _outlineColor;

    public string Archetype { get; private set; } = string.Empty;

    public float X { get; private set; }

    public float Y { get; private set; }

    public float Health { get; private set; }

    public float MaxHealth { get; private set; }

    public float ContactDamage { get; private set; }

    public float MoveSpeed { get; private set; }

    public float Radius { get; private set; }

    public bool IsDead { get; private set; }

    public void Bind(in NoitaWangMarkerAnchor anchor)
    {
        Archetype = ResolveArchetype(anchor.Function);
        X = anchor.WorldX;
        Y = anchor.WorldY;
        (MaxHealth, ContactDamage, MoveSpeed, Radius, _bodyColor, _outlineColor) = Archetype switch
        {
            "large" => (120f, 22f, 28f, 7f, 0xFF_58_70_D8u, 0xFF_C0_D0_FFu),
            "robot" => (90f, 18f, 34f, 6f, 0xFF_B0_A0_70u, 0xFF_F0_E8_C0u),
            "swarm" => (28f, 8f, 58f, 4f, 0xFF_68_C8_70u, 0xFF_D0_FF_B0u),
            "aquatic" => (36f, 10f, 42f, 5f, 0xFF_D8_98_48u, 0xFF_FF_E0_90u),
            _ => (55f, 14f, 44f, 5f, 0xFF_70_78_D8u, 0xFF_D0_D8_FFu),
        };
        Health = MaxHealth;
        IsDead = false;
        Enabled = true;
    }

    public bool ApplyDamage(float amount)
    {
        if (IsDead || !float.IsFinite(amount) || amount <= 0f)
        {
            return false;
        }

        Health = MathF.Max(0f, Health - amount);
        if (Health > 0f)
        {
            return true;
        }

        IsDead = true;
        Enabled = false;
        return true;
    }

    public bool TryHitSegment(float x0, float y0, float x1, float y1, float damage, out float hitX, out float hitY)
    {
        hitX = X;
        hitY = Y;
        if (IsDead)
        {
            return false;
        }

        float dx = x1 - x0;
        float dy = y1 - y0;
        float lengthSquared = (dx * dx) + (dy * dy);
        float t = lengthSquared <= 0.0001f
            ? 0f
            : Math.Clamp((((X - x0) * dx) + ((Y - y0) * dy)) / lengthSquared, 0f, 1f);
        hitX = x0 + (dx * t);
        hitY = y0 + (dy * t);
        float offsetX = X - hitX;
        float offsetY = Y - hitY;
        if ((offsetX * offsetX) + (offsetY * offsetY) > Radius * Radius)
        {
            return false;
        }

        _ = ApplyDamage(damage);
        return true;
    }

    protected override void OnUpdate(float dt)
    {
        if (IsDead || !float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        ResolvePlayer();
        _elapsed += dt;
        _contactCooldown = MathF.Max(0f, _contactCooldown - dt);
        if (_player is not null)
        {
            float dx = _player.CenterX - X;
            float dy = _player.CenterY - Y;
            float distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared is > 1f and < 65_536f)
            {
                float inverseDistance = 1f / MathF.Sqrt(distanceSquared);
                float nextX = X + (dx * inverseDistance * MoveSpeed * dt);
                float nextY = Y + (dy * inverseDistance * MoveSpeed * dt);
                if (!Context.Solids.SampleSolidAabb(nextX - Radius, nextY - Radius, Radius * 2f, Radius * 2f))
                {
                    X = nextX;
                    Y = nextY;
                }
            }

            float contactRadius = Radius + 5f;
            if (_contactCooldown <= 0f && distanceSquared <= contactRadius * contactRadius)
            {
                _playerHealth?.ApplyExternalDamage(ContactDamage);
                _contactCooldown = 0.7f;
            }
        }

        Draw();
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

    private void Draw()
    {
        Point2F center = Context.Camera.WorldToScreen(X, Y);
        float size = MathF.Max(7f, Radius * 2f * MathF.Max(1f, Context.Camera.Zoom));
        float half = size * 0.5f;
        float pulse = 0.85f + (MathF.Sin(_elapsed * 4f) * 0.1f);
        Context.Overlay.SolidRectangle(center.X - half, center.Y - half, size, size, ScaleAlpha(_bodyColor, pulse));
        Context.Overlay.OutlineRectangle(center.X - half, center.Y - half, size, size, 1.5f, _outlineColor);
        Context.Overlay.SolidRectangle(center.X - half, center.Y - half - 4f, size * (Health / MaxHealth), 2f, 0xFF_40_D8_58);
        Context.Lighting.AddPointLight(X, Y, Radius * 4f, _bodyColor, 0.28f);
    }

    private static string ResolveArchetype(string function)
    {
        return function.Contains("large", StringComparison.Ordinal) || function.Contains("alchemist", StringComparison.Ordinal)
            ? "large"
            : function.Contains("robot", StringComparison.Ordinal) || function.Contains("scavenger", StringComparison.Ordinal)
                ? "robot"
                : function.Contains("fish", StringComparison.Ordinal)
                    ? "aquatic"
                    : function.Contains("crawler", StringComparison.Ordinal) || function.Contains("scorpion", StringComparison.Ordinal)
                        ? "swarm"
                        : "standard";
    }

    private static uint ScaleAlpha(uint bgra, float multiplier)
    {
        byte alpha = (byte)(bgra >> 24);
        byte scaled = (byte)Math.Clamp((int)MathF.Round(alpha * multiplier), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)scaled << 24);
    }
}
