using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 熔岩矿洞逃生出口触发器；仅在任务目标完成后允许通关。
/// </summary>
public sealed class ExtractionTrigger : Behaviour
{
    private PlayerController? _player;
    private MissionDirector? _mission;
    private MaterialId _celebrationMaterial;
    private bool _materialResolved;
    private float _pulse;

    /// <summary>
    /// 触发区域左上角 X 坐标。
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// 触发区域左上角 Y 坐标。
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// 触发区域宽度。
    /// </summary>
    public float Width { get; set; } = 34f;

    /// <summary>
    /// 触发区域高度。
    /// </summary>
    public float Height { get; set; } = 54f;

    /// <summary>
    /// 达成撤离时播放的音效 cue。
    /// </summary>
    public string ExtractionAudioCue { get; set; } = "goal_reached.wav";

    /// <summary>
    /// 达成撤离时喷出的材质名。
    /// </summary>
    public string CelebrationMaterialName { get; set; } = "sand";

    /// <summary>
    /// 达成撤离时喷出的粒子数量。
    /// </summary>
    public int CelebrationParticleCount { get; set; } = 42;

    /// <summary>
    /// 点光源 BGRA 颜色。
    /// </summary>
    public uint LightColorBgra { get; set; } = 0xFF_80_F0_FF;

    /// <summary>
    /// 是否已触发撤离成功。
    /// </summary>
    public bool Reached { get; private set; }

    /// <summary>
    /// 最近一次阻塞原因；为空表示脚本已就绪。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        Reached = false;
        BlockedReason = string.Empty;
        _pulse = 0f;
        ResolveMaterial();
        ResolveSceneComponents();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f)
        {
            return;
        }

        ResolveMaterial();
        ResolveSceneComponents();
        _pulse += dt;
        float centerX = X + (Width * 0.5f);
        float centerY = Y + (Height * 0.5f);
        float intensity = Reached ? 1.25f : 0.55f + (MathF.Sin(_pulse * 4.2f) * 0.14f);
        Context.Lighting.AddPointLight(centerX, centerY, Reached ? 92f : 46f, LightColorBgra, intensity);
        Context.Lighting.RevealAround(centerX, centerY, Reached ? 104f : 72f, 190);

        if (Reached || _player is null || _mission is null || !Intersects(_player.State))
        {
            return;
        }

        if (_mission.CrystalsCollected < Math.Max(1, _mission.RequiredCrystals) || _mission.State != MissionState.Playing)
        {
            BlockedReason = "目标水晶未集齐或任务已结束。";
            return;
        }

        MarkReached(centerX, centerY);
    }

    private void ResolveMaterial()
    {
        if (_materialResolved)
        {
            return;
        }

        _celebrationMaterial = Context.Materials.Resolve(CelebrationMaterialName);
        _materialResolved = _celebrationMaterial.IsValid;
        if (!_materialResolved)
        {
            BlockedReason = $"材质未解析：{CelebrationMaterialName}";
        }
    }

    private void ResolveSceneComponents()
    {
        if (_player is not null && _mission is not null)
        {
            return;
        }

        if (_player is null && Entity.TryGetComponent(out PlayerController localPlayer))
        {
            _player = localPlayer;
        }

        if (_mission is null && Entity.TryGetComponent(out MissionDirector localMission))
        {
            _mission = localMission;
        }

        if (_player is not null && _mission is not null)
        {
            BlockedReason = string.Empty;
            return;
        }

        if (_player is null && Context.Scene.TryGetFirstComponent(out PlayerController? scenePlayer))
        {
            _player = scenePlayer;
        }

        if (_mission is null && Context.Scene.TryGetFirstComponent(out MissionDirector? sceneMission))
        {
            _mission = sceneMission;
        }

        BlockedReason = _player is null || _mission is null
            ? "场景中未找到 PlayerController 或 MissionDirector。"
            : string.Empty;
    }

    private bool Intersects(in CharacterState state)
    {
        return state.X < X + Width &&
            state.X + state.Width > X &&
            state.Y < Y + Height &&
            state.Y + state.Height > Y;
    }

    private void MarkReached(float centerX, float centerY)
    {
        _mission!.MarkExtractionReached();
        if (_mission.State != MissionState.Won)
        {
            return;
        }

        Reached = true;
        if (_celebrationMaterial.IsValid && CelebrationParticleCount > 0)
        {
            Context.Particles.Burst(centerX, centerY, _celebrationMaterial, CelebrationParticleCount, 90f);
        }

        if (!string.IsNullOrWhiteSpace(ExtractionAudioCue))
        {
            Context.Audio.PlayAt(ExtractionAudioCue, centerX, centerY);
        }
    }
}
