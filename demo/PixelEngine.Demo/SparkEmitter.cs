using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 火花发射器，使用脚本粒子 API 周期性生成发光 fire 粒子。
/// </summary>
public sealed class SparkEmitter : Behaviour
{
    private MaterialId _sparkMaterial;
    private float _timer;
    private int _burstIndex;

    /// <summary>
    /// 发射器中心 X 坐标。
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// 发射器中心 Y 坐标。
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// 火花材质稳定名称。
    /// </summary>
    public string MaterialName { get; set; } = "fire";

    /// <summary>
    /// 发射间隔，单位秒。
    /// </summary>
    public float IntervalSeconds { get; set; } = 0.08f;

    /// <summary>
    /// 每次发射的火花数量。
    /// </summary>
    public int Count { get; set; } = 5;

    /// <summary>
    /// 火花初速度，单位像素/秒。
    /// </summary>
    public float Speed { get; set; } = 92f;

    /// <summary>
    /// 火花 lifetime。
    /// </summary>
    public ushort Lifetime { get; set; } = 55;

    /// <summary>
    /// 向上发射时的水平散布宽度。
    /// </summary>
    public float Spread { get; set; } = 0.75f;

    /// <summary>
    /// 最近一次阻塞原因；为空表示脚本已就绪。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveMaterial();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f)
        {
            return;
        }

        ResolveMaterial();
        if (!_sparkMaterial.IsValid)
        {
            return;
        }

        _timer -= dt;
        if (_timer > 0f)
        {
            return;
        }

        EmitSparks();
        _timer += Math.Max(0.001f, IntervalSeconds);
    }

    private void ResolveMaterial()
    {
        if (_sparkMaterial.IsValid)
        {
            return;
        }

        _sparkMaterial = Context.Materials.Resolve(MaterialName);
        BlockedReason = _sparkMaterial.IsValid ? string.Empty : $"材质未解析：{MaterialName}";
    }

    private void EmitSparks()
    {
        int count = Math.Max(0, Count);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : (i / (float)(count - 1)) - 0.5f;
            float jitter = (((_burstIndex + i) % 5) - 2) * 0.07f;
            float vx = ((t * Spread) + jitter) * Speed;
            float vy = (-(0.72f + (MathF.Abs(t) * 0.18f))) * Speed;
            Context.Particles.Spawn(new ParticleSpawnDesc(X, Y, vx, vy, _sparkMaterial, Lifetime));
        }

        Context.Lighting.AddPointLight(X, Y, 38f, 0xFF_30_88_FF, 0.70f);
        _burstIndex++;
    }
}
