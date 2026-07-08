using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 熔岩矿洞逃生任务状态。
/// </summary>
public enum MissionState
{
    /// <summary>
    /// 任务进行中。
    /// </summary>
    Playing,

    /// <summary>
    /// 任务胜利。
    /// </summary>
    Won,

    /// <summary>
    /// 任务失败。
    /// </summary>
    Lost,
}

/// <summary>
/// 可被脚本事件总线传递的采矿收益事件。
/// </summary>
public readonly struct MineYieldEvent(int cellX, int cellY, ushort materialId, ushort amount)
{
    /// <summary>
    /// 采集发生的 cell X 坐标。
    /// </summary>
    public int CellX { get; } = cellX;

    /// <summary>
    /// 采集发生的 cell Y 坐标。
    /// </summary>
    public int CellY { get; } = cellY;

    /// <summary>
    /// 被采集材质的运行时 id。
    /// </summary>
    public ushort MaterialId { get; } = materialId;

    /// <summary>
    /// 本次事件贡献的采集数量。
    /// </summary>
    public ushort Amount { get; } = amount;
}

/// <summary>
/// 熔岩矿洞逃生任务导演，负责水晶进度、时限、水位、胜负和计分。
/// </summary>
public sealed class MissionDirector : Behaviour
{
    private PlayerHealth? _health;
    private WeaponController? _weapon;
    private bool _componentsResolved;
    private bool _externalLavaSurface;
    private int _baselineRespawns;
    private string _menuStatus = string.Empty;

    /// <summary>
    /// 需要收集的目标水晶数量。
    /// </summary>
    public int RequiredCrystals { get; set; } = 3;

    /// <summary>
    /// 任务时限，单位秒。
    /// </summary>
    public float TimeLimitSeconds { get; set; } = 240f;

    /// <summary>
    /// 熔岩初始表面 Y 坐标；坐标越小表示水位越高。
    /// </summary>
    public float InitialLavaSurfaceY { get; set; } = 336f;

    /// <summary>
    /// 熔岩上升速度，单位 cell/秒。
    /// </summary>
    public float LavaRiseCellsPerSecond { get; set; } = 0.45f;

    /// <summary>
    /// 每秒剩余时间的分数权重。
    /// </summary>
    public int TimeScorePerSecond { get; set; } = 10;

    /// <summary>
    /// 每发剩余弹药的分数权重。
    /// </summary>
    public int AmmoScorePerRound { get; set; } = 5;

    /// <summary>
    /// 未受伤通关奖励。
    /// </summary>
    public int UndamagedBonus { get; set; } = 500;

    /// <summary>
    /// 胜负菜单宽度，单位像素。
    /// </summary>
    public float ResultMenuWidth { get; set; } = 380f;

    /// <summary>
    /// 胜负菜单高度，单位像素。
    /// </summary>
    public float ResultMenuHeight { get; set; } = 174f;

    /// <summary>
    /// 当前任务状态。
    /// </summary>
    public MissionState State { get; private set; } = MissionState.Playing;

    /// <summary>
    /// 已收集水晶数量。
    /// </summary>
    public int CrystalsCollected { get; private set; }

    /// <summary>
    /// 任务已运行时间，单位秒。
    /// </summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>
    /// 剩余任务时间，单位秒。
    /// </summary>
    public float RemainingSeconds => MathF.Max(0f, TimeLimitSeconds - ElapsedSeconds);

    /// <summary>
    /// 当前熔岩表面 Y 坐标。
    /// </summary>
    public float LavaSurfaceY { get; private set; }

    /// <summary>
    /// 当前结算分数。
    /// </summary>
    public int Score { get; private set; }

    /// <summary>
    /// 最近一次胜负原因。
    /// </summary>
    public string ResultReason { get; private set; } = string.Empty;

    /// <summary>
    /// 最近一次阻塞原因；为空表示任务导演已就绪。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        State = MissionState.Playing;
        CrystalsCollected = 0;
        ElapsedSeconds = 0f;
        LavaSurfaceY = InitialLavaSurfaceY;
        Score = 0;
        ResultReason = string.Empty;
        BlockedReason = string.Empty;
        _externalLavaSurface = false;
        _menuStatus = string.Empty;
        ResolveComponents();
        _baselineRespawns = _health?.RespawnCount ?? 0;
        _ = Context.Events.Subscribe<MineYieldEvent>(OnMineYield);
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f || State != MissionState.Playing)
        {
            return;
        }

        ResolveComponents();
        ElapsedSeconds += dt;
        if (!_externalLavaSurface)
        {
            LavaSurfaceY = InitialLavaSurfaceY - (MathF.Max(0f, LavaRiseCellsPerSecond) * ElapsedSeconds);
        }

        Score = CalculateScore();

        if (ElapsedSeconds >= TimeLimitSeconds)
        {
            MarkLost("time_limit");
            return;
        }

        if (_health is not null && _health.RespawnCount > _baselineRespawns)
        {
            MarkLost("player_death");
            return;
        }

        if (_health is not null && Entity.TryGetComponent(out PlayerController localPlayer) && localPlayer.State.Y + localPlayer.State.Height >= LavaSurfaceY)
        {
            MarkLost("lava_reached_player");
        }
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        if (State == MissionState.Playing)
        {
            return;
        }

        float x = MathF.Max(12f, (gui.Width - ResultMenuWidth) * 0.5f);
        float y = MathF.Max(12f, (gui.Height - ResultMenuHeight) * 0.5f);
        gui.SetNextWindow(x, y, ResultMenuWidth, ResultMenuHeight, GuiCondition.FirstUseEver);
        string title = State == MissionState.Won ? "撤离成功" : "任务失败";
        if (!gui.BeginWindow("mission-result-menu", title, GuiWindowFlags.NoResize | GuiWindowFlags.NoSavedSettings))
        {
            gui.EndWindow();
            return;
        }

        if (State == MissionState.Won)
        {
            gui.TextColored("矿洞撤离成功", 0xFF_80_F0_80);
        }
        else
        {
            gui.TextColored("任务失败", 0xFF_60_60_F0);
        }

        gui.Text($"水晶 {CrystalsCollected}/{Math.Max(1, RequiredCrystals)}   分数 {Score}");
        gui.Text($"剩余时间 {RemainingSeconds:0}s   熔岩水位 {LavaSurfaceY:0}");
        if (!string.IsNullOrWhiteSpace(ResultReason))
        {
            gui.Text($"原因 {ResultReason}");
        }

        if (gui.Button("重开"))
        {
            RuntimeControlResult result = Context.Runtime.RequestRestartCurrentScene();
            _menuStatus = result.Message;
        }

        gui.SameLine();
        if (gui.Button("退出"))
        {
            RuntimeControlResult result = Context.Runtime.RequestShutdown();
            _menuStatus = result.Message;
        }

        if (!string.IsNullOrWhiteSpace(_menuStatus))
        {
            gui.Separator();
            gui.Text(_menuStatus);
        }

        gui.EndWindow();
    }

    /// <summary>
    /// 由出口触发器在满足进入条件时调用。
    /// </summary>
    public void MarkExtractionReached()
    {
        if (State != MissionState.Playing)
        {
            return;
        }

        if (CrystalsCollected < Math.Max(1, RequiredCrystals))
        {
            ResultReason = "missing_crystals";
            return;
        }

        State = MissionState.Won;
        Score = CalculateScore();
        ResultReason = "extraction_reached";
    }

    /// <summary>
    /// 由环境危险导演写入当前熔岩表面高度。
    /// </summary>
    /// <param name="surfaceY">当前熔岩表面 Y 坐标。</param>
    public void SetLavaSurface(float surfaceY)
    {
        if (!float.IsFinite(surfaceY))
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceY), surfaceY, "熔岩表面坐标必须是有限值。");
        }

        LavaSurfaceY = surfaceY;
        _externalLavaSurface = true;
    }

    /// <summary>
    /// 强制判负，供后续环境危险导演或测试触发。
    /// </summary>
    /// <param name="reason">失败原因。</param>
    public void MarkLost(string reason)
    {
        if (State != MissionState.Playing)
        {
            return;
        }

        State = MissionState.Lost;
        Score = CalculateScore();
        ResultReason = string.IsNullOrWhiteSpace(reason) ? "lost" : reason;
    }

    private void OnMineYield(MineYieldEvent item)
    {
        if (State != MissionState.Playing || item.Amount == 0)
        {
            return;
        }

        int required = Math.Max(1, RequiredCrystals);
        CrystalsCollected = Math.Min(required, CrystalsCollected + item.Amount);
        Score = CalculateScore();
    }

    private void ResolveComponents()
    {
        if (_componentsResolved)
        {
            return;
        }

        if (_health is null)
        {
            if (Entity.TryGetComponent(out PlayerHealth health))
            {
                _health = health;
            }
        }

        if (_weapon is null)
        {
            if (Entity.TryGetComponent(out WeaponController weapon))
            {
                _weapon = weapon;
            }
        }

        if (_health is not null && _weapon is not null)
        {
            _componentsResolved = true;
            BlockedReason = string.Empty;
        }
        else
        {
            BlockedReason = "MissionDirector 未找到 PlayerHealth 或 WeaponController；计分会降级为时间与无伤状态。";
        }
    }

    private int CalculateScore()
    {
        int remainingTimeScore = Math.Max(0, (int)MathF.Floor(RemainingSeconds) * Math.Max(0, TimeScorePerSecond));
        int ammoScore = Math.Max(0, _weapon?.TotalRemainingAmmo ?? 0) * Math.Max(0, AmmoScorePerRound);
        int undamagedScore = _health is not null && _health.DamageEventCount == 0 ? Math.Max(0, UndamagedBonus) : 0;
        return remainingTimeScore + ammoScore + undamagedScore;
    }
}
