using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 关卡材质喷口脚本，按固定间隔向世界写入材质并生成粒子反馈。
/// </summary>
public sealed class MaterialEmitter : Behaviour
{
    private MaterialId _material = MaterialId.Invalid;
    private float _timer;
    private float _audioTimer;
    private int _burstIndex;

    /// <summary>
    /// 喷口中心 X 坐标。
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// 喷口中心 Y 坐标。
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// 要喷出的稳定材质名。
    /// </summary>
    public string MaterialName { get; set; } = "water";

    /// <summary>
    /// 写入世界的圆形半径，单位 cell。
    /// </summary>
    public int Radius { get; set; } = 2;

    /// <summary>
    /// 喷发间隔，单位秒。
    /// </summary>
    public float IntervalSeconds { get; set; } = 0.20f;

    /// <summary>
    /// 每次喷发生成的自由粒子数量。
    /// </summary>
    public int ParticleCount { get; set; } = 8;

    /// <summary>
    /// 自由粒子初速度标量，单位像素/秒。
    /// </summary>
    public float ParticleSpeed { get; set; } = 55f;

    /// <summary>
    /// 自由粒子 lifetime。
    /// </summary>
    public ushort ParticleLifetime { get; set; } = 90;

    /// <summary>
    /// 粒子喷射主方向 X 分量。
    /// </summary>
    public float DirectionX { get; set; }

    /// <summary>
    /// 粒子喷射主方向 Y 分量。
    /// </summary>
    public float DirectionY { get; set; } = 1f;

    /// <summary>
    /// 是否直接向 cell 网格写入材质。
    /// </summary>
    public bool PaintCells { get; set; } = true;

    /// <summary>
    /// 是否生成自由粒子。
    /// </summary>
    public bool SpawnParticles { get; set; } = true;

    /// <summary>
    /// 是否在首帧立即喷发一次。
    /// </summary>
    public bool EmitOnStart { get; set; } = true;

    /// <summary>
    /// 喷发时播放的可选音效 cue；为空则不播放。
    /// </summary>
    public string AudioCue { get; set; } = string.Empty;

    /// <summary>
    /// 音效播放最小间隔，单位秒。
    /// </summary>
    public float AudioCooldownSeconds { get; set; } = 0.60f;

    /// <summary>
    /// 是否为该喷口添加当前帧点光源。
    /// </summary>
    public bool AddLight { get; set; }

    /// <summary>
    /// 点光源半径，单位 cell。
    /// </summary>
    public float LightRadius { get; set; } = 48f;

    /// <summary>
    /// 点光源 BGRA 颜色。
    /// </summary>
    public uint LightColorBgra { get; set; } = 0xFF_40_80_FF;

    /// <summary>
    /// 点光源强度。
    /// </summary>
    public float LightIntensity { get; set; } = 0.85f;

    /// <summary>
    /// 最近一次阻塞原因；为空表示脚本已就绪。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveMaterial();
        _timer = EmitOnStart ? 0f : Math.Max(0f, IntervalSeconds);
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f)
        {
            return;
        }

        ResolveMaterial();
        if (!_material.IsValid)
        {
            return;
        }

        if (AddLight)
        {
            Context.Lighting.AddPointLight(X, Y, LightRadius, LightColorBgra, LightIntensity);
        }

        _audioTimer = MathF.Max(0f, _audioTimer - dt);
        _timer -= dt;
        if (_timer > 0f)
        {
            return;
        }

        Emit();
        _timer += Math.Max(0.001f, IntervalSeconds);
    }

    private void ResolveMaterial()
    {
        if (_material.IsValid)
        {
            return;
        }

        _material = Context.Materials.Resolve(MaterialName);
        BlockedReason = _material.IsValid ? string.Empty : $"材质未解析：{MaterialName}";
    }

    private void Emit()
    {
        int centerX = (int)MathF.Round(X);
        int centerY = (int)MathF.Round(Y);
        if (PaintCells)
        {
            Context.Cells.Paint(centerX, centerY, Math.Max(0, Radius), _material);
        }

        if (SpawnParticles && ParticleCount > 0)
        {
            SpawnParticleFan();
        }

        if (!string.IsNullOrWhiteSpace(AudioCue) && ShouldPlayAudio())
        {
            Context.Audio.PlayAt(AudioCue, X, Y);
            _audioTimer = Math.Max(0f, AudioCooldownSeconds);
        }

        _burstIndex++;
    }

    private void SpawnParticleFan()
    {
        float length = MathF.Sqrt((DirectionX * DirectionX) + (DirectionY * DirectionY));
        float baseX = length > float.Epsilon ? DirectionX / length : 0f;
        float baseY = length > float.Epsilon ? DirectionY / length : 1f;
        float normalX = -baseY;
        float normalY = baseX;
        int count = Math.Max(0, ParticleCount);
        for (int i = 0; i < count; i++)
        {
            float spread = count == 1 ? 0f : ((i / (float)(count - 1)) - 0.5f) * 0.9f;
            float jitter = (((_burstIndex + i) % 3) - 1) * 0.08f;
            float vx = (baseX + (normalX * (spread + jitter))) * ParticleSpeed;
            float vy = (baseY + (normalY * (spread + jitter))) * ParticleSpeed;
            Context.Particles.Spawn(new ParticleSpawnDesc(X, Y, vx, vy, _material, ParticleLifetime));
        }
    }

    private bool ShouldPlayAudio()
    {
        return AudioCooldownSeconds <= 0f || _audioTimer <= 0f;
    }
}
