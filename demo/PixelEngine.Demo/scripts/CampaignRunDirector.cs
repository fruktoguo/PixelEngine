using System.Globalization;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 正式运行模式。
/// </summary>
public enum DemoGameMode : byte
{
    /// <summary>带纵深目标、永久死亡与新 seed 轮回的战役。</summary>
    Campaign,

    /// <summary>无胜利条件、死亡后回安全区的无限沙盒。</summary>
    InfiniteSandbox,
}

/// <summary>
/// Noita 复刻战役的一轮运行状态。
/// </summary>
public enum CampaignRunState : byte
{
    /// <summary>等待玩家选择模式并开始。</summary>
    MainMenu,

    /// <summary>新世界已就绪，等待首个 gameplay tick。</summary>
    StartingRun,

    /// <summary>在普通区域探索。</summary>
    Exploring,

    /// <summary>位于层间 Holy Mountain。</summary>
    HolyMountain,

    /// <summary>转向前脚本使用的 Holy Mountain 等值 alias。</summary>
    StillForge = HolyMountain,

    /// <summary>进入最终区域 The Laboratory。</summary>
    Laboratory,

    /// <summary>转向前脚本使用的 The Laboratory 等值 alias。</summary>
    Finale = Laboratory,

    /// <summary>已完成当前纵深路线。</summary>
    Completed,

    /// <summary>当前战役已永久死亡。</summary>
    Dead,

    /// <summary>显示本轮真实统计并等待下一轮。</summary>
    RunSummary,
}

/// <summary>
/// 管理 Campaign / InfiniteSandbox 模式、run seed、纵深状态、永久死亡与新世界轮回。
/// </summary>
public sealed class CampaignRunDirector : Behaviour
{
    private const ulong SeedIncrement = 0x9E37_79B9_7F4A_7C15UL;
    private CampaignConfig? _config;
    private IRuntimeControlApi? _runtime;
    private PlayerController? _player;

    /// <summary>当前选择的正式模式。</summary>
    public DemoGameMode Mode { get; private set; }

    /// <summary>当前 run 生命周期状态。</summary>
    public CampaignRunState State { get; private set; } = CampaignRunState.MainMenu;

    /// <summary>当前权威世界 seed。</summary>
    public ulong RunSeed { get; private set; }

    /// <summary>固定宽度十六进制 seed 文本；只在 run 初始化时分配。</summary>
    public string RunSeedText { get; private set; } = "0000000000000000";

    /// <summary>当前区域索引，范围 0..7。</summary>
    public int CurrentRegionIndex { get; private set; }

    /// <summary>当前相对战役地表的非负纵深，单位 cell。</summary>
    public long CurrentDepthCells { get; private set; }

    /// <summary>本轮抵达的最大纵深，单位 cell。</summary>
    public long DeepestDepthCells { get; private set; }

    /// <summary>本轮抵达的最深区域索引。</summary>
    public int DeepestRegionIndex { get; private set; }

    /// <summary>本轮已经进入过的区域 bit mask。</summary>
    public byte VisitedRegionMask { get; private set; }

    /// <summary>本轮 gameplay 累计时间，单位秒。</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>当前结算是否来自纵深完成。</summary>
    public bool WasCompleted { get; private set; }

    /// <summary>当前结算的稳定可显示原因。</summary>
    public string ResultReason { get; private set; } = string.Empty;

    /// <summary>最近一次请求的新世界 seed；尚未请求时为 0。</summary>
    public ulong RequestedNextSeed { get; private set; }

    /// <summary>是否已接纳原子新世界重建请求。</summary>
    public bool IsRestartRequested { get; private set; }

    /// <summary>当前区域的 canonical Noita biome 显示名。</summary>
    public string CurrentRegionDisplayName => _config?.Regions[CurrentRegionIndex].DisplayName ?? "Mines";

    /// <summary>当前模式的稳定显示名。</summary>
    public string ModeDisplayName => Mode == DemoGameMode.Campaign ? "战役 / Campaign" : "无限沙盒 / Infinite Sandbox";

    /// <summary>当前状态的稳定显示名。</summary>
    public string StateDisplayName => State switch
    {
        CampaignRunState.MainMenu => "主菜单 / Main Menu",
        CampaignRunState.StartingRun => "新一轮 / Starting Run",
        CampaignRunState.Exploring => "探索 / Exploring",
        CampaignRunState.HolyMountain => "Holy Mountain",
        CampaignRunState.Laboratory => "The Laboratory",
        CampaignRunState.Completed => "完成 / Completed",
        CampaignRunState.Dead => "永久死亡 / Dead",
        CampaignRunState.RunSummary => "本轮结算 / Run Summary",
        _ => "未知 / Unknown",
    };

    /// <inheritdoc />
    protected override void OnStart()
    {
        CampaignConfig config = CampaignConfig.Load(Context.Config);
        IRuntimeControlApi? runtime = TryResolveRuntime();
        RuntimeControlSnapshot snapshot = runtime?.Capture() ?? default;
        Initialize(config, runtime, in snapshot);
        if (Entity.TryGetComponent(out PlayerController player))
        {
            _player = player;
        }
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (IsRestartRequested)
        {
            PollRestartStatus();
            return;
        }

        if (TransitionTerminalToSummary())
        {
            return;
        }

        if (State == CampaignRunState.StartingRun)
        {
            State = CampaignRunState.Exploring;
        }

        if (!IsGameplayState(State))
        {
            return;
        }

        if (_player is null)
        {
            if (Entity.TryGetComponent(out PlayerController player))
            {
                _player = player;
            }
        }

        if (_player is not null)
        {
            AdvanceRun(_player.CenterY, dt);
        }
    }

    /// <summary>
    /// 在主菜单选择模式；运行中的模式不可被静默替换。
    /// </summary>
    /// <param name="mode">目标模式。</param>
    /// <returns>选择是否生效。</returns>
    public bool SelectMode(DemoGameMode mode)
    {
        if (State != CampaignRunState.MainMenu || !Enum.IsDefined(mode))
        {
            return false;
        }

        Mode = mode;
        return true;
    }

    /// <summary>
    /// 从主菜单开始当前已装配世界中的第一轮。
    /// </summary>
    /// <returns>是否成功进入 StartingRun。</returns>
    public bool StartSelectedRun()
    {
        if (State != CampaignRunState.MainMenu)
        {
            return false;
        }

        ResetRunStatistics();
        State = CampaignRunState.StartingRun;
        _runtime?.ResumeSimulation();
        return true;
    }

    /// <summary>
    /// 处理玩家生命归零。Campaign 进入永久死亡；Sandbox 返回 false 让生命组件安全重生。
    /// </summary>
    /// <returns>若永久死亡已由战役接管则返回 true。</returns>
    public bool HandlePlayerDeath()
    {
        if (Mode != DemoGameMode.Campaign)
        {
            return false;
        }

        if (State is CampaignRunState.Dead or
            CampaignRunState.Completed or
            CampaignRunState.RunSummary)
        {
            return true;
        }

        if (!IsGameplayState(State))
        {
            return false;
        }

        WasCompleted = false;
        ResultReason = "永久死亡 / Run ended";
        State = CampaignRunState.Dead;
        return true;
    }

    /// <summary>
    /// 主动结束当前 Campaign，并进入相同的可验证结算流程。
    /// </summary>
    /// <returns>是否成功结束当前轮。</returns>
    public bool AbandonRun()
    {
        if (Mode != DemoGameMode.Campaign || !IsGameplayState(State))
        {
            return false;
        }

        WasCompleted = false;
        ResultReason = "主动结束 / Run abandoned";
        State = CampaignRunState.Dead;
        return true;
    }

    /// <summary>
    /// 从 RunSummary 派生新 seed，并请求宿主原子替换程序化世界与脚本生命周期。
    /// </summary>
    /// <returns>宿主接纳或拒绝结果。</returns>
    public RuntimeControlResult RequestNextRun()
    {
        if (Mode != DemoGameMode.Campaign || State != CampaignRunState.RunSummary)
        {
            return new RuntimeControlResult(false, "只有 Campaign RunSummary 可以开始新一轮。");
        }

        if (_runtime is null)
        {
            return new RuntimeControlResult(false, "当前宿主不支持运行时世界重建。");
        }

        ulong nextSeed = DeriveNextSeed(RunSeed);
        RuntimeControlResult result = _runtime.RequestRestartCurrentProceduralWorld(nextSeed);
        if (!result.Success)
        {
            return result;
        }

        RequestedNextSeed = nextSeed;
        IsRestartRequested = true;
        State = CampaignRunState.StartingRun;
        _runtime.ResumeSimulation();
        return result;
    }

    internal void Initialize(
        CampaignConfig config,
        IRuntimeControlApi? runtime,
        in RuntimeControlSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config.Validate();
        _runtime = runtime;
        bool resumedCampaignRun = snapshot.RestartStatus == RuntimeRestartStatus.Succeeded;
        Mode = resumedCampaignRun ? DemoGameMode.Campaign : ParseMode(config.DefaultMode);
        RunSeed = snapshot.WorldSeed == 0 ? config.InitialRunSeed : snapshot.WorldSeed;
        RunSeedText = RunSeed.ToString("X16", CultureInfo.InvariantCulture);
        ResetRunStatistics();
        if (resumedCampaignRun)
        {
            State = CampaignRunState.StartingRun;
            _runtime?.ResumeSimulation();
        }
        else
        {
            State = CampaignRunState.MainMenu;
            _runtime?.PauseSimulation();
        }
    }

    internal void AdvanceRun(float playerCenterY, float dt)
    {
        CampaignConfig config = _config ?? throw new InvalidOperationException("CampaignRunDirector 尚未初始化。");
        if (float.IsFinite(dt) && dt > 0f)
        {
            ElapsedSeconds += dt;
        }

        long depth = Math.Max(0, (long)MathF.Floor(playerCenterY) - config.SurfaceY);
        CurrentDepthCells = depth;
        DeepestDepthCells = Math.Max(DeepestDepthCells, depth);
        CampaignDepthLocation location = config.ResolveLocation((long)MathF.Floor(playerCenterY));
        CurrentRegionIndex = Math.Clamp(location.RegionIndex, 0, CampaignConfig.RequiredRegionCount - 1);
        DeepestRegionIndex = Math.Max(DeepestRegionIndex, CurrentRegionIndex);
        VisitedRegionMask |= (byte)(1 << CurrentRegionIndex);

        if (Mode == DemoGameMode.InfiniteSandbox)
        {
            State = CampaignRunState.Exploring;
            return;
        }

        long completionDepth = config.CampaignEndDepthCells;
        if (depth >= completionDepth)
        {
            WasCompleted = true;
            ResultReason = "抵达 The Laboratory 终点 / Route completed";
            State = CampaignRunState.Completed;
            return;
        }

        State = location.Kind == CampaignDepthKind.HolyMountain
            ? CampaignRunState.HolyMountain
            : CurrentRegionIndex == CampaignConfig.RequiredRegionCount - 1
                ? CampaignRunState.Laboratory
                : CampaignRunState.Exploring;
    }

    internal static ulong DeriveNextSeed(ulong currentSeed)
    {
        ulong value = currentSeed + SeedIncrement;
        value = (value ^ (value >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D0_49BB_1331_11EBUL;
        value ^= value >> 31;
        return value == 0 ? SeedIncrement : value;
    }

    internal bool TransitionTerminalToSummary()
    {
        if (State is not (CampaignRunState.Dead or CampaignRunState.Completed))
        {
            return false;
        }

        State = CampaignRunState.RunSummary;
        _runtime?.PauseSimulation();
        return true;
    }

    internal void PollRestartStatus()
    {
        RecoverFailedRestartIfNeeded();
    }

    private void ResetRunStatistics()
    {
        CurrentRegionIndex = 0;
        CurrentDepthCells = 0;
        DeepestDepthCells = 0;
        DeepestRegionIndex = 0;
        VisitedRegionMask = 1;
        ElapsedSeconds = 0f;
        WasCompleted = false;
        ResultReason = string.Empty;
        RequestedNextSeed = 0;
        IsRestartRequested = false;
    }

    private void RecoverFailedRestartIfNeeded()
    {
        if (_runtime?.Capture().RestartStatus != RuntimeRestartStatus.Failed)
        {
            return;
        }

        IsRestartRequested = false;
        RequestedNextSeed = 0;
        State = CampaignRunState.RunSummary;
        _runtime.PauseSimulation();
    }

    private static bool IsGameplayState(CampaignRunState state)
    {
        return state is CampaignRunState.StartingRun or
            CampaignRunState.Exploring or
            CampaignRunState.HolyMountain or
            CampaignRunState.Laboratory;
    }

    private static DemoGameMode ParseMode(string mode)
    {
        return string.Equals(mode, "infiniteSandbox", StringComparison.OrdinalIgnoreCase)
            ? DemoGameMode.InfiniteSandbox
            : DemoGameMode.Campaign;
    }

    private IRuntimeControlApi? TryResolveRuntime()
    {
        try
        {
            return Context.Runtime;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
