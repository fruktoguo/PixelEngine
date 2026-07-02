using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Scripting;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 窗口短跑的固定输入脚本，用于在真实窗口相位链路中自动触发玩法动作。
/// </summary>
internal sealed class DemoWindowScriptedInput(ScriptInputApi input, ScriptCameraApi camera) : IEnginePhaseDriver
{
    private readonly ScriptInputApi _input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly ScriptCameraApi _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly Key[] _keys = new Key[3];
    private readonly MouseButton[] _buttons = new MouseButton[1];

    /// <summary>
    /// 已注入输入的帧数。
    /// </summary>
    public int FramesInjected { get; private set; }

    /// <summary>
    /// 材质笔刷目标世界坐标。
    /// </summary>
    public Point2F BrushTargetWorld { get; } = new(40f, 240f);

    /// <summary>
    /// 爆破工具目标世界坐标。
    /// </summary>
    public Point2F ExplosionTargetWorld { get; } = new(90f, 240f);

    /// <summary>
    /// 木桥擦除目标世界坐标。
    /// </summary>
    public Point2F BridgeCutTargetWorld { get; } = new(209f, 250f);

    /// <summary>
    /// 注册输入注入相位；该 hook 应在 Silk 输入采样之后注册，以便覆盖采样结果。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.InputAndTime, Inject);
    }

    private void Inject(EngineTickContext context)
    {
        _ = context;
        int frame = FramesInjected++;
        int keyCount = 0;
        int buttonCount = 0;
        Point2F target = BrushTargetWorld;
        float wheelY = 0f;

        if (frame == 2)
        {
            _keys[keyCount++] = Key.Digit6;
            wheelY = 1f;
        }
        else if (frame is >= 3 and <= 5)
        {
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame == 7)
        {
            target = ExplosionTargetWorld;
            _buttons[buttonCount++] = MouseButton.Middle;
        }
        else if (frame is >= 9 and <= 16)
        {
            target = new Point2F(BridgeCutTargetWorld.X, BridgeCutTargetWorld.Y + (frame - 12));
            _buttons[buttonCount++] = MouseButton.Right;
        }
        else if (frame is >= 18 and <= 36)
        {
            _keys[keyCount++] = Key.D;
            if (frame == 20)
            {
                _keys[keyCount++] = Key.Space;
            }
        }
        else if (frame == 70)
        {
            _keys[keyCount++] = Key.Escape;
        }

        Point2F screen = _camera.WorldToScreen(target.X, target.Y);
        _input.Update(
            _keys.AsSpan(0, keyCount),
            _buttons.AsSpan(0, buttonCount),
            screen.X,
            screen.Y,
            wheelY);
    }
}

/// <summary>
/// Demo 脚本化窗口短跑的运行态探针，累计会被后续帧覆盖的瞬时结果。
/// </summary>
internal sealed class DemoWindowScriptedProbe(
    PhysicsSystem physics,
    ParticleSystem particles,
    ScriptLightingSynchronizer lighting) : IEnginePhaseDriver
{
    private readonly PhysicsSystem _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly ScriptLightingSynchronizer _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));

    /// <summary>
    /// 短跑期间观测到的最大刚体销毁数量。
    /// </summary>
    public int MaxDestroyedBodies { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大刚体创建数量。
    /// </summary>
    public int MaxCreatedBodies { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大自由粒子数量。
    /// </summary>
    public int MaxParticles { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大点光数量。
    /// </summary>
    public int MaxLights { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大音频抽取事件数量。
    /// </summary>
    public long MaxAudioDrained { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大音频播放数量。
    /// </summary>
    public long MaxAudioPlayed { get; private set; }

    /// <summary>
    /// 注册物理与音频之后的采样相位。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.PhysicsSync, Capture);
        phases.Register(EnginePhase.BuildRenderBuffer, CaptureAudio);
    }

    private void Capture(EngineTickContext context)
    {
        _ = context;
        MaxDestroyedBodies = Math.Max(MaxDestroyedBodies, _physics.LastDestructionResult.DestroyedBodies);
        MaxCreatedBodies = Math.Max(MaxCreatedBodies, _physics.LastDestructionResult.CreatedBodies);
        MaxParticles = Math.Max(MaxParticles, _particles.ActiveCount);
        MaxLights = Math.Max(MaxLights, _lighting.PointLights.Length);
    }

    private void CaptureAudio(EngineTickContext context)
    {
        MaxAudioDrained = Math.Max(MaxAudioDrained, context.Context.Counters.AudioDrained);
        MaxAudioPlayed = Math.Max(MaxAudioPlayed, context.Context.Counters.AudioPlayed);
    }
}
