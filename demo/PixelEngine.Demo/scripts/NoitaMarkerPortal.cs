using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>由来源 XML 中静态绝对目标构建的 Noita marker Portal。</summary>
internal sealed class NoitaMarkerPortal : Behaviour
{
    private const float TriggerHalfExtent = 15f;
    private PlayerController? _player;
    private float _elapsed;
    private bool _playerWasInside;

    public string Function { get; private set; } = string.Empty;

    public string Origin { get; private set; } = string.Empty;

    public long WorldX { get; private set; }

    public long WorldY { get; private set; }

    public float TargetX { get; private set; }

    public float TargetY { get; private set; }

    public int TeleportCount { get; private set; }

    public static bool Supports(in NoitaWangMarkerAnchor anchor)
    {
        return TryResolveDestination(anchor.Function, anchor.Origin, out _, out _);
    }

    public void Bind(in NoitaWangMarkerAnchor anchor)
    {
        if (!TryResolveDestination(anchor.Function, anchor.Origin, out float targetX, out float targetY))
        {
            throw new InvalidOperationException($"Portal marker 没有静态目标：{anchor.Function} / {anchor.Origin}。");
        }

        Function = anchor.Function;
        Origin = anchor.Origin;
        WorldX = anchor.WorldX;
        WorldY = anchor.WorldY;
        TargetX = targetX;
        TargetY = targetY;
        TeleportCount = 0;
        _playerWasInside = false;
        Enabled = true;
    }

    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        _elapsed += dt;
        if (_player is null)
        {
            _ = Context.Scene.TryGetFirstComponent(out _player);
        }

        DrawPortal();
        if (_player is null)
        {
            return;
        }

        bool inside = IntersectsTrigger(_player.State, WorldX, WorldY, TriggerHalfExtent);
        if (inside && !_playerWasInside)
        {
            _player.TeleportToCenter(TargetX, TargetY);
            Context.Audio.PlayAt("portal.wav", WorldX, WorldY, 0.9f);
            TransientParticleBurst.Emit(Context, WorldX, WorldY, 18, 54f, 72, 0xFF_FF_80_D8u, 0xCC_80_40_FFu, 0.9f);
            TeleportCount++;
        }

        _playerWasInside = inside;
    }

    private void DrawPortal()
    {
        float pulse = 0.78f + (MathF.Sin(_elapsed * 4.5f) * 0.18f);
        Point2F center = Context.Camera.WorldToScreen(WorldX, WorldY);
        float radius = MathF.Max(9f, Context.Camera.Zoom * 15f);
        uint outer = ScaleAlpha(0xFF_FF_70_D0u, pulse);
        uint inner = ScaleAlpha(0xFF_F0_80_FFu, 0.45f + (pulse * 0.25f));
        Context.Overlay.OutlineRectangle(center.X - radius, center.Y - radius, radius * 2f, radius * 2f, 2f, outer);
        Context.Overlay.OutlineRectangle(
            center.X - (radius * 0.58f),
            center.Y - (radius * 0.58f),
            radius * 1.16f,
            radius * 1.16f,
            1.5f,
            inner);
        Context.Lighting.AddPointLight(WorldX, WorldY, 64f, 0xFF_FF_70_D0u, 0.9f * pulse);
    }

    internal static bool TryResolveDestination(
        string function,
        string origin,
        out float targetX,
        out float targetY)
    {
        if (function == "spawn_teleport_back" &&
            origin.EndsWith("scripts/biomes/lake.lua", StringComparison.Ordinal))
        {
            targetX = -12557f;
            targetY = 190f;
            return true;
        }

        if (function == "spawn_buried_eye_teleporter" &&
            origin.EndsWith("scripts/biomes/snowcave.lua", StringComparison.Ordinal))
        {
            targetX = 3895f;
            targetY = 4510f;
            return true;
        }

        if (function == "spawn_teleporter")
        {
            if (origin.EndsWith("scripts/biomes/excavationsite_cube_chamber.lua", StringComparison.Ordinal))
            {
                targetX = 190f;
                targetY = 1525f;
                return true;
            }

            if (origin.EndsWith("scripts/biomes/snowcastle_hourglass_chamber.lua", StringComparison.Ordinal))
            {
                targetX = 190f;
                targetY = 5231f;
                return true;
            }

            if (origin.EndsWith("scripts/biomes/snowcave_secret_chamber.lua", StringComparison.Ordinal))
            {
                targetX = 190f;
                targetY = 3080f;
                return true;
            }
        }

        targetX = 0f;
        targetY = 0f;
        return false;
    }

    internal static bool IntersectsTrigger(
        in CharacterState state,
        float centerX,
        float centerY,
        float halfExtent)
    {
        float left = centerX - halfExtent;
        float top = centerY - halfExtent;
        float size = halfExtent * 2f;
        return state.X < left + size && state.X + state.Width > left &&
            state.Y < top + size && state.Y + state.Height > top;
    }

    private static uint ScaleAlpha(uint bgra, float multiplier)
    {
        byte alpha = (byte)(bgra >> 24);
        byte scaled = (byte)Math.Clamp((int)MathF.Round(alpha * multiplier), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)scaled << 24);
    }
}
