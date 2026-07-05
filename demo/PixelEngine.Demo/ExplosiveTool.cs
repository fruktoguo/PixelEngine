using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 爆破工具脚本，使用公开世界复合效果 API 触发抗性感知破坏、碎屑抛射与刚体冲量。
/// </summary>
public sealed class ExplosiveTool : Behaviour
{
    private float _cooldownRemaining;

    /// <summary>
    /// 爆炸半径，单位 cell。
    /// </summary>
    public int Radius { get; set; } = 18;

    /// <summary>
    /// 爆炸径向冲量强度。
    /// </summary>
    public float Force { get; set; } = 42f;

    /// <summary>
    /// 两次爆破之间的冷却时间，单位秒。
    /// </summary>
    public float CooldownSeconds { get; set; } = 0.35f;

    /// <summary>
    /// 最近一次爆炸中心 X 坐标。
    /// </summary>
    public float LastExplosionX { get; private set; }

    /// <summary>
    /// 最近一次爆炸中心 Y 坐标。
    /// </summary>
    public float LastExplosionY { get; private set; }

    /// <summary>
    /// 已请求爆破次数。
    /// </summary>
    public int ExplosionCount { get; private set; }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _cooldownRemaining = MathF.Max(0f, _cooldownRemaining - MathF.Max(0f, dt));
        if (_cooldownRemaining > 0f || !Context.Input.WasMousePressed(MouseButton.Middle))
        {
            return;
        }

        (float mouseX, float mouseY) = Context.Input.MousePixel;
        Point2F world = Context.Camera.ScreenToWorld(mouseX, mouseY);
        int radius = Math.Max(1, Radius);
        float force = MathF.Max(1f, Force);
        Context.World.Explode(world.X, world.Y, radius, force);
        Context.Lighting.RevealAround(world.X, world.Y, radius * 1.5f);
        Context.Lighting.AddPointLight(world.X, world.Y, radius * 2f, 0xFF_30_80_FF, 1.4f);
        LastExplosionX = world.X;
        LastExplosionY = world.Y;
        ExplosionCount++;
        _cooldownRemaining = MathF.Max(0f, CooldownSeconds);
    }
}
