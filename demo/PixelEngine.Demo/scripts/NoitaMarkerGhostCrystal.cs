using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>复现 Noita ghost crystal 的生成组、绑定幽灵与死亡冰弹环。</summary>
internal sealed class NoitaMarkerGhostCrystal : Behaviour
{
    private const int MaximumGhosts = 4;
    private readonly Entity?[] _ghostEntities = new Entity?[MaximumGhosts];
    private float _elapsed;
    private bool _spawnPending;
    private bool _deathPending;
    private uint _sequence;

    public float X { get; private set; }

    public float Y { get; private set; }

    public float Health { get; private set; }

    public float MaxHealth { get; private set; }

    public bool IsDead { get; private set; }

    public bool IsPopulated { get; private set; }

    public int GhostCount { get; private set; }

    public int IceShardCount { get; private set; }

    public static bool Supports(string function)
    {
        return function == "spawn_ghost_crystal";
    }

    public void Bind(in NoitaWangMarkerAnchor anchor, ulong worldSeed)
    {
        X = anchor.WorldX - 1f;
        Y = anchor.WorldY;
        MaxHealth = 20f;
        Health = MaxHealth;
        IsDead = false;
        IceShardCount = 0;
        _sequence = StableSeed(worldSeed, anchor.WorldX, anchor.WorldY);
        // 来源权重为空 0.5、实体组 1.0；命中实体组时外层生成 1~3 幽灵，水晶自身再绑定 1 个。
        IsPopulated = NextUnit() >= (1f / 3f);
        GhostCount = IsPopulated ? 2 + (int)(NextRandom() % 3u) : 0;
        _spawnPending = IsPopulated;
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
        hitY = Y - 10f;
        if (!IsPopulated || IsDead)
        {
            return false;
        }

        float dx = x1 - x0;
        float dy = y1 - y0;
        float lengthSquared = (dx * dx) + (dy * dy);
        float t = lengthSquared <= 0.0001f
            ? 0f
            : Math.Clamp((((X - x0) * dx) + ((Y - 10f - y0) * dy)) / lengthSquared, 0f, 1f);
        hitX = x0 + (dx * t);
        hitY = y0 + (dy * t);
        float offsetX = X - hitX;
        float offsetY = Y - 10f - hitY;
        if ((offsetX * offsetX) + (offsetY * offsetY) > 10f * 10f)
        {
            return false;
        }

        _ = ApplyDamage(damage);
        return true;
    }

    protected override void OnUpdate(float dt)
    {
        if (_spawnPending)
        {
            SpawnGhosts();
            _spawnPending = false;
        }

        if (_deathPending)
        {
            SpawnDeathRing();
            DestroyGhosts();
            _deathPending = false;
            Enabled = false;
            return;
        }

        if (!IsPopulated || IsDead)
        {
            return;
        }

        _elapsed += float.IsFinite(dt) && dt > 0f ? dt : 0f;
        Draw();
    }

    protected override void OnDestroy()
    {
        DestroyGhosts();
    }

    private void SpawnGhosts()
    {
        for (int i = 0; i < GhostCount; i++)
        {
            Entity ghostEntity = Context.Scene.CreateEntity();
            NoitaMarkerEnemy ghost = ghostEntity.AddComponent<NoitaMarkerEnemy>();
            float angle = MathF.Tau * i / Math.Max(1, GhostCount);
            NoitaWangMarkerAnchor ghostAnchor = new(
                "ghost_crystal",
                "ghost_crystal",
                "ffc8001a",
                "spawn_ghost",
                "data/entities/buildings/ghost_crystal.xml",
                NoitaWangTerrainCatalog.MarkerSemanticBase,
                (long)MathF.Round(X + (MathF.Cos(angle) * 14f)),
                (long)MathF.Round(Y - 10f + (MathF.Sin(angle) * 8f)));
            ghost.Bind(ghostAnchor);
            _ghostEntities[i] = ghostEntity;
        }
    }

    private void SpawnDeathRing()
    {
        for (int i = 0; i < 12; i++)
        {
            float angle = MathF.Tau * i / 12f;
            Entity shardEntity = Context.Scene.CreateEntity();
            NoitaMarkerIceShard shard = shardEntity.AddComponent<NoitaMarkerIceShard>();
            shard.Bind(X, Y - 10f, MathF.Cos(angle) * 50f, MathF.Sin(angle) * 50f);
        }

        IceShardCount = 12;
    }

    private void DestroyGhosts()
    {
        for (int i = 0; i < _ghostEntities.Length; i++)
        {
            _ghostEntities[i]?.Destroy();
            _ghostEntities[i] = null;
        }
    }

    private void Draw()
    {
        Point2F center = Context.Camera.WorldToScreen(X, Y - 10f);
        float scale = MathF.Max(1f, Context.Camera.Zoom);
        float width = 12f * scale;
        float height = 20f * scale;
        float pulse = 0.82f + (MathF.Sin(_elapsed * 3.2f) * 0.12f);
        Context.Overlay.SolidRectangle(center.X - (width * 0.5f), center.Y - (height * 0.5f), width, height, ScaleAlpha(0xFF_E6_78_E6u, pulse));
        Context.Overlay.OutlineRectangle(center.X - (width * 0.5f), center.Y - (height * 0.5f), width, height, 1.5f, 0xFF_F8_C8_F8u);
        Context.Overlay.SolidRectangle(center.X - (width * 0.5f), center.Y - (height * 0.5f) - 4f, width * (Health / MaxHealth), 2f, 0xFF_48_D8_60u);
        Context.Lighting.AddPointLight(X, Y - 6f, 96f, 0xFF_E6_78_E6u, 0.82f * pulse);
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

    private static uint ScaleAlpha(uint bgra, float multiplier)
    {
        byte alpha = (byte)(bgra >> 24);
        byte scaled = (byte)Math.Clamp((int)MathF.Round(alpha * multiplier), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)scaled << 24);
    }
}

/// <summary>ghost crystal 死亡时生成的径向冰弹。</summary>
internal sealed class NoitaMarkerIceShard : Behaviour
{
    private PlayerController? _player;
    private PlayerHealth? _playerHealth;
    private float _vx;
    private float _vy;
    private float _elapsed;

    public void Bind(float x, float y, float vx, float vy)
    {
        X = x;
        Y = y;
        _vx = vx;
        _vy = vy;
        Enabled = true;
    }

    public float X { get; private set; }

    public float Y { get; private set; }

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
        _vx *= MathF.Max(0f, 1f - (0.05f * dt));
        _vy *= MathF.Max(0f, 1f - (0.05f * dt));
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
            _playerHealth?.ApplyExternalDamage(7.5f);
            Explode();
            return;
        }

        Point2F point = Context.Camera.WorldToScreen(X, Y);
        float size = MathF.Max(2f, Context.Camera.Zoom * 2f);
        Context.Overlay.SolidRectangle(point.X - size, point.Y - size, size * 2f, size * 2f, 0xFF_F0_D0_78u);
        Context.Lighting.AddPointLight(X, Y, 20f, 0xFF_8B_CD_63u, 0.42f);
        if (_elapsed >= (80f / 60f))
        {
            Explode();
        }
    }

    private void Explode()
    {
        Context.World.DamageCircle(X, Y, 6, 7.5f, falloff: true);
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
        float t = lengthSquared <= 0.0001f ? 0f : Math.Clamp((((px - x0) * dx) + ((py - y0) * dy)) / lengthSquared, 0f, 1f);
        float offsetX = px - (x0 + (dx * t));
        float offsetY = py - (y0 + (dy * t));
        return (offsetX * offsetX) + (offsetY * offsetY);
    }
}
