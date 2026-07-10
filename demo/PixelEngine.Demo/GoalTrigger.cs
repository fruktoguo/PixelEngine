using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 关卡终点触发器，检测玩家 AABB 是否进入目标区域。
/// </summary>
public sealed class GoalTrigger : Behaviour
{
    private PlayerController? _player;
    private MaterialId _celebrationMaterial;
    private bool _materialResolved;
    private float _pulse;
    private string _victoryStatus = string.Empty;

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
    public float Width { get; set; } = 28f;

    /// <summary>
    /// 触发区域高度。
    /// </summary>
    public float Height { get; set; } = 42f;

    /// <summary>
    /// 达成目标时播放的音效 cue。
    /// </summary>
    public string GoalAudioCue { get; set; } = "goal_reached.wav";

    /// <summary>
    /// 达成目标时喷出的材质名。
    /// </summary>
    public string CelebrationMaterialName { get; set; } = "sand";

    /// <summary>
    /// 达成目标时喷出的粒子数量。
    /// </summary>
    public int CelebrationParticleCount { get; set; } = 36;

    /// <summary>
    /// 达成目标时喷出的粒子速度。
    /// </summary>
    public float CelebrationParticleSpeed { get; set; } = 95f;

    /// <summary>
    /// Fog-of-war 揭示半径，单位 cell。
    /// </summary>
    public float RevealRadius { get; set; } = 96f;

    /// <summary>
    /// 待机点光源半径，单位 cell。
    /// </summary>
    public float IdleLightRadius { get; set; } = 42f;

    /// <summary>
    /// 达成目标后点光源半径，单位 cell。
    /// </summary>
    public float ReachedLightRadius { get; set; } = 96f;

    /// <summary>
    /// 点光源 BGRA 颜色。
    /// </summary>
    public uint LightColorBgra { get; set; } = 0xFF_80_F0_FF;

    /// <summary>
    /// 通关菜单宽度，单位像素。
    /// </summary>
    public float VictoryMenuWidth { get; set; } = 360f;

    /// <summary>
    /// 通关菜单高度，单位像素。
    /// </summary>
    public float VictoryMenuHeight { get; set; } = 150f;

    /// <summary>
    /// 玩家是否已经触达该目标。
    /// </summary>
    public bool Reached { get; private set; }

    /// <summary>
    /// 最近一次阻塞原因；为空表示脚本已就绪。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        _player = null;
        Reached = false;
        _victoryStatus = string.Empty;
        ResolveMaterial();
        _ = TryResolvePlayer();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f)
        {
            return;
        }

        ResolveMaterial();
        _pulse += dt;
        float centerX = X + (Width * 0.5f);
        float centerY = Y + (Height * 0.5f);
        float pulseIntensity = 0.65f + (MathF.Sin(_pulse * 4f) * 0.12f);
        Context.Lighting.AddPointLight(
            centerX,
            centerY,
            Reached ? ReachedLightRadius : IdleLightRadius,
            LightColorBgra,
            Reached ? 1.25f : pulseIntensity);
        Context.Lighting.RevealAround(centerX, centerY, RevealRadius, 180);

        if (Reached || !TryResolvePlayer())
        {
            return;
        }

        if (Intersects(_player!.State))
        {
            MarkReached(centerX, centerY);
        }
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        if (!Reached)
        {
            return;
        }

        float x = MathF.Max(12f, (gui.Width - VictoryMenuWidth) * 0.5f);
        float y = MathF.Max(12f, (gui.Height - VictoryMenuHeight) * 0.5f);
        gui.SetNextWindow(x, y, VictoryMenuWidth, VictoryMenuHeight, GuiCondition.FirstUseEver);
        if (!gui.BeginWindow("demo-victory-menu", "通关", GuiWindowFlags.NoResize | GuiWindowFlags.NoSavedSettings))
        {
            gui.EndWindow();
            return;
        }

        gui.TextColored("矿洞出口已抵达", 0xFF_80_F0_80);
        gui.Text("目标完成");
        if (gui.Button("重开关卡"))
        {
            RuntimeControlResult result = Context.Runtime.RequestRestartCurrentScene();
            _victoryStatus = result.Message;
        }

        gui.SameLine();
        if (gui.Button("退出"))
        {
            RuntimeControlResult result = Context.Runtime.RequestShutdown();
            _victoryStatus = result.Message;
        }

        if (!string.IsNullOrEmpty(_victoryStatus))
        {
            gui.Separator();
            gui.Text(_victoryStatus);
        }

        gui.EndWindow();
    }

    private void ResolveMaterial()
    {
        if (_materialResolved)
        {
            return;
        }

        _celebrationMaterial = Context.Materials.Resolve(CelebrationMaterialName);
        _materialResolved = _celebrationMaterial.IsValid;
        BlockedReason = _materialResolved ? string.Empty : $"材质未解析：{CelebrationMaterialName}";
    }

    private bool TryResolvePlayer()
    {
        if (_player is not null)
        {
            return true;
        }

        if (Entity.TryGetComponent(out PlayerController localPlayer))
        {
            _player = localPlayer;
            return true;
        }

        if (Context.Scene.TryGetFirstComponent(out PlayerController? scenePlayer))
        {
            _player = scenePlayer;
            return true;
        }

        BlockedReason = "场景中未找到 PlayerController。";
        return false;
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
        Reached = true;
        if (_celebrationMaterial.IsValid && CelebrationParticleCount > 0)
        {
            Context.Particles.Burst(centerX, centerY, _celebrationMaterial, CelebrationParticleCount, CelebrationParticleSpeed);
        }

        if (!string.IsNullOrWhiteSpace(GoalAudioCue))
        {
            Context.Audio.PlayAt(GoalAudioCue, centerX, centerY);
        }
    }
}
