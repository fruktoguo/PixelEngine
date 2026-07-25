using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>由危险 marker 生成的激光、电击或材料陷阱。</summary>
internal sealed class NoitaMarkerHazard : Behaviour
{
    private const float LaserRange = 112f;
    private const float LaserThickness = 2.5f;
    private PlayerController? _player;
    private PlayerHealth? _health;
    private MaterialId _material = MaterialId.Invalid;
    private float _timer;
    private float _elapsed;

    public string Function { get; private set; } = string.Empty;

    public NoitaMarkerHazardKind Kind { get; private set; }

    public long WorldX { get; private set; }

    public long WorldY { get; private set; }

    public float LastBeamLength { get; private set; }

    public static bool Supports(string function)
    {
        return function is "spawn_lasergun" or "spawn_lasergate_ver" or "spawn_laser_trap" or
            "spawn_electricity_trap" or "spawn_acid" or "spawn_burning_barrel" or "spawn_cloud_trap";
    }

    public void Bind(in NoitaWangMarkerAnchor anchor)
    {
        Function = anchor.Function;
        WorldX = anchor.WorldX;
        WorldY = anchor.WorldY;
        Kind = ResolveKind(anchor.Function);
        LastBeamLength = 0f;
        _timer = 0f;
        Enabled = true;
    }

    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        ResolvePlayer();
        ResolveMaterial();
        _elapsed += dt;
        _timer -= dt;
        if (Kind is NoitaMarkerHazardKind.LaserHorizontal or NoitaMarkerHazardKind.LaserVertical)
        {
            UpdateLaser(dt);
        }
        else if (Kind == NoitaMarkerHazardKind.Electric)
        {
            UpdateElectric();
        }
        else
        {
            UpdateMaterialTrap();
        }
    }

    private void ResolvePlayer()
    {
        if (_player is null)
        {
            _ = Context.Scene.TryGetFirstComponent(out _player);
        }

        if (_health is null)
        {
            _ = Context.Scene.TryGetFirstComponent(out _health);
        }
    }

    private void ResolveMaterial()
    {
        if (_material.IsValid || Kind is NoitaMarkerHazardKind.LaserHorizontal or
            NoitaMarkerHazardKind.LaserVertical or NoitaMarkerHazardKind.Electric)
        {
            return;
        }

        _material = Context.Materials.Resolve(Kind == NoitaMarkerHazardKind.Acid ? "acid" : "fire");
    }

    private void UpdateLaser(float dt)
    {
        float directionX = Kind == NoitaMarkerHazardKind.LaserHorizontal ? 1f : 0f;
        float directionY = Kind == NoitaMarkerHazardKind.LaserVertical ? 1f : 0f;
        LastBeamLength = Context.Solids.Raycast(WorldX, WorldY, directionX, directionY, LaserRange, out RaycastHit hit) && hit.Hit
            ? MathF.Max(0f, hit.Distance - 1f)
            : LaserRange;
        float endX = WorldX + (directionX * LastBeamLength);
        float endY = WorldY + (directionY * LastBeamLength);
        uint color = ScaleAlpha(0xFF_50_40_FFu, 0.78f + (MathF.Sin(_elapsed * 18f) * 0.18f));
        Point2F start = Context.Camera.WorldToScreen(WorldX, WorldY);
        Point2F end = Context.Camera.WorldToScreen(endX, endY);
        Context.Overlay.Line(start.X, start.Y, end.X, end.Y, MathF.Max(2f, Context.Camera.Zoom * LaserThickness), color);
        Context.Lighting.AddPointLight(WorldX, WorldY, 38f, 0xFF_50_40_FFu, 0.82f);

        if (_player is not null && _health is not null &&
            IntersectsBeam(_player.State, WorldX, WorldY, endX, endY, LaserThickness))
        {
            _health.ApplyExternalDamage(38f * dt);
        }
    }

    private void UpdateElectric()
    {
        Context.Lighting.AddPointLight(WorldX, WorldY, 34f, 0xFF_FF_E0_68u, 0.7f);
        if (_timer > 0f)
        {
            return;
        }

        _timer += 0.75f;
        TransientParticleBurst.Emit(Context, WorldX, WorldY, 10, 44f, 42, 0xFF_FF_F0_80u, 0xCC_80_B8_FFu, 0.7f);
        if (_player is null || _health is null ||
            DistanceSquared(_player.CenterX, _player.CenterY, WorldX, WorldY) > 30f * 30f)
        {
            return;
        }

        float dx = _player.CenterX - WorldX;
        float dy = _player.CenterY - WorldY;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        if (distance <= float.Epsilon ||
            !Context.Solids.Raycast(WorldX, WorldY, dx / distance, dy / distance, distance, out RaycastHit hit) ||
            !hit.Hit)
        {
            _health.ApplyExternalDamage(14f);
        }
    }

    private void UpdateMaterialTrap()
    {
        uint color = Kind == NoitaMarkerHazardKind.Acid ? 0xFF_50_FF_80u : 0xFF_40_80_FFu;
        Context.Lighting.AddPointLight(WorldX, WorldY, 30f, color, 0.58f);
        if (_timer > 0f || !_material.IsValid)
        {
            return;
        }

        _timer += Kind == NoitaMarkerHazardKind.Acid ? 1.1f : 0.8f;
        Context.Cells.Paint((int)WorldX, (int)WorldY + 2, 2, _material);
        Context.Particles.Burst(WorldX, WorldY, _material, 8, 32f);
    }

    internal static bool IntersectsBeam(
        in CharacterState state,
        float startX,
        float startY,
        float endX,
        float endY,
        float thickness)
    {
        float minX = MathF.Min(startX, endX) - thickness;
        float maxX = MathF.Max(startX, endX) + thickness;
        float minY = MathF.Min(startY, endY) - thickness;
        float maxY = MathF.Max(startY, endY) + thickness;
        return state.X < maxX && state.X + state.Width > minX &&
            state.Y < maxY && state.Y + state.Height > minY;
    }

    private static NoitaMarkerHazardKind ResolveKind(string function)
    {
        return function == "spawn_lasergate_ver"
            ? NoitaMarkerHazardKind.LaserVertical
            : function is "spawn_lasergun" or "spawn_laser_trap"
                ? NoitaMarkerHazardKind.LaserHorizontal
                : function == "spawn_electricity_trap"
                    ? NoitaMarkerHazardKind.Electric
                    : function is "spawn_acid" or "spawn_cloud_trap"
                        ? NoitaMarkerHazardKind.Acid
                        : NoitaMarkerHazardKind.Fire;
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

internal enum NoitaMarkerHazardKind : byte
{
    LaserHorizontal,
    LaserVertical,
    Electric,
    Acid,
    Fire,
}
