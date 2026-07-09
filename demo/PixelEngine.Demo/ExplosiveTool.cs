using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 爆破工具脚本，使用公开世界复合效果 API 触发抗性感知破坏、碎屑抛射与刚体冲量。
/// </summary>
public sealed class ExplosiveTool : Behaviour
{
    private float _cooldownRemaining;
    private ExplosionFlashEffect _flash;

    /// <summary>
    /// 爆炸半径，单位 cell。
    /// </summary>
    public int Radius { get; set; } = 72;

    /// <summary>
    /// 爆炸径向冲量强度。
    /// </summary>
    public float Force { get; set; } = 320f;

    /// <summary>
    /// 对地形破坏半径与冲量的倍率；正式 Demo 用它把独立中键爆破纳入 10x 地形修改口径。
    /// </summary>
    public float TerrainEffectScale { get; set; } = 10f;

    /// <summary>
    /// 当前实际提交给 <c>World.Explode</c> 的爆炸半径。
    /// </summary>
    public int EffectiveRadius => Math.Max(1, (int)MathF.Round(Math.Max(1, Radius) * NormalizedTerrainEffectScale));

    /// <summary>
    /// 当前实际提交给 <c>World.Explode</c> 的径向冲量强度。
    /// </summary>
    public float EffectiveForce => MathF.Max(1f, Force * NormalizedTerrainEffectScale);

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
        float safeDt = MathF.Max(0f, dt);
        _ = _flash.Update(Context, safeDt);
        _cooldownRemaining = MathF.Max(0f, _cooldownRemaining - safeDt);
        // 中键冷却结束后在鼠标世界坐标触发 Explode 与闪光反馈
        if (_cooldownRemaining > 0f || !Context.Input.WasMousePressed(MouseButton.Middle))
        {
            return;
        }

        (float mouseX, float mouseY) = Context.Input.MousePixel;
        Point2F world = Context.Camera.ScreenToWorld(mouseX, mouseY);
        int radius = EffectiveRadius;
        float force = EffectiveForce;
        Context.World.Explode(world.X, world.Y, radius, force);
        _flash.Start(world.X, world.Y, radius, 0xFF_30_80_FF);
        _flash.SubmitInitial(Context);
        LastExplosionX = world.X;
        LastExplosionY = world.Y;
        ExplosionCount++;
        _cooldownRemaining = MathF.Max(0f, CooldownSeconds);
    }

    private float NormalizedTerrainEffectScale
    {
        get
        {
            float scale = TerrainEffectScale;
            return float.IsFinite(scale) && scale > 0f ? scale : 1f;
        }
    }
}
