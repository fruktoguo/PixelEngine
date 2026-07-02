using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩 Demo 的轻量射击工具，左键朝鼠标方向发射破坏弹。
/// </summary>
public sealed class PlayableProjectileTool : Behaviour
{
    private PlayerController? _player;
    private float _cooldownRemaining;
    private MaterialId _sparkMaterial;
    private bool _materialResolved;

    /// <summary>
    /// 射击最大距离，单位 cell。
    /// </summary>
    public float Range { get; set; } = 180f;

    /// <summary>
    /// 命中爆破半径，单位 cell。
    /// </summary>
    public int ImpactRadius { get; set; } = 9;

    /// <summary>
    /// 命中冲量强度。
    /// </summary>
    public float ImpactForce { get; set; } = 24f;

    /// <summary>
    /// 射击冷却时间，单位秒。
    /// </summary>
    public float CooldownSeconds { get; set; } = 0.16f;

    /// <summary>
    /// 已开火次数。
    /// </summary>
    public int ShotsFired { get; private set; }

    /// <summary>
    /// 最近一次命中 X 坐标。
    /// </summary>
    public float LastHitX { get; private set; }

    /// <summary>
    /// 最近一次命中 Y 坐标。
    /// </summary>
    public float LastHitY { get; private set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveMaterial();
        _player = Entity.TryGetComponent<PlayerController>(out PlayerController player) ? player : null;
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _cooldownRemaining = MathF.Max(0f, _cooldownRemaining - MathF.Max(0f, dt));
        if (_cooldownRemaining > 0f || !Context.Input.WasMousePressed(MouseButton.Left))
        {
            return;
        }

        if (_player is null)
        {
            if (!Entity.TryGetComponent<PlayerController>(out PlayerController player))
            {
                return;
            }

            _player = player;
        }

        ResolveMaterial();
        (float mouseX, float mouseY) = Context.Input.MousePixel;
        Point2F target = Context.Camera.ScreenToWorld(mouseX, mouseY);
        float startX = _player.CenterX;
        float startY = _player.CenterY - 2f;
        float dx = target.X - startX;
        float dy = target.Y - startY;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            return;
        }

        dx /= length;
        dy /= length;
        float hitX = startX + (dx * Range);
        float hitY = startY + (dy * Range);
        if (Context.Solids.Raycast(startX, startY, dx, dy, Range, out RaycastHit hit))
        {
            hitX = hit.X;
            hitY = hit.Y;
        }

        Context.World.Explode(hitX, hitY, Math.Max(1, ImpactRadius), MathF.Max(1f, ImpactForce));
        Context.Lighting.RevealAround(hitX, hitY, ImpactRadius * 2.5f);
        Context.Lighting.AddPointLight(hitX, hitY, ImpactRadius * 3f, 0xFF_60_D8_FF, 0.35f);
        EmitTracer(startX, startY, hitX, hitY);
        LastHitX = hitX;
        LastHitY = hitY;
        ShotsFired++;
        _cooldownRemaining = MathF.Max(0f, CooldownSeconds);
    }

    private void EmitTracer(float startX, float startY, float hitX, float hitY)
    {
        if (!_sparkMaterial.IsValid)
        {
            return;
        }

        float dx = hitX - startX;
        float dy = hitY - startY;
        float length = MathF.Max(1f, MathF.Sqrt((dx * dx) + (dy * dy)));
        dx /= length;
        dy /= length;
        int count = Math.Clamp((int)(length / 18f), 3, 10);
        for (int i = 1; i <= count; i++)
        {
            float t = i / (float)(count + 1);
            ParticleSpawnDesc particle = new(
                startX + ((hitX - startX) * t),
                startY + ((hitY - startY) * t),
                dx * 90f,
                dy * 90f,
                _sparkMaterial,
                14);
            Context.Particles.Spawn(in particle);
        }
    }

    private void ResolveMaterial()
    {
        if (_materialResolved)
        {
            return;
        }

        _sparkMaterial = Context.Materials.Resolve("fire");
        _materialResolved = _sparkMaterial.IsValid;
    }
}
