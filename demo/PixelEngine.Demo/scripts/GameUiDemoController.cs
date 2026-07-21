using PixelEngine.Scripting;
using RuntimeUiStableId = PixelEngine.UI.UiStableId;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 游戏大 UI 控制器，使用公开脚本 UI 服务驱动主菜单、HUD、暂停、设置与结算页面。
/// </summary>
public sealed class GameUiDemoController : Behaviour
{
    internal const string MainMenuScreen = "main-menu";
    internal const string SettingsScreen = "settings";
    internal const string InventoryScreen = "inventory";
    internal const string DialogScreen = "dialog";
    internal const string HudScreen = "hud";
    internal const string TelemetryScreen = "telemetry";
    internal const string PixelOverlayScreen = "pixel-overlay";
    internal const string PhysicalOverlayScreen = "physical-overlay";
    internal const string PauseScreen = "pause";
    internal const string ResultScreen = "result";

    private static readonly string[] HudModelPaths =
    [
        "hud.mode_text",
        "hud.seed_text",
        "hud.region_text",
        "hud.run_state_text",
        "hud.depth_cells",
        "hud.health",
        "hud.ammo",
        "hud.cooldown",
        "hud.heat",
        "hud.reload",
        "hud.crystals",
        "hud.time",
        "hud.hazard",
        "hud.score",
        "hud.distance",
        "hud.longitude",
        "hud.depth",
        "hud.elevation",
    ];

    private static readonly string[] MenuModelPaths =
    [
        "menu.campaign_selected",
        "menu.sandbox_selected",
        "menu.mode_text",
    ];

    private static readonly string[] TelemetryModelPaths =
    [
        "hud.weapon",
        "hud.overheated",
        "hud.material_slot",
        "hud.brush_radius",
        "hud.explosions",
        "hud.shots",
        "hud.collapse_islands",
        "hud.collapse_scan",
        "hud.fps",
        "hud.frame_p99",
        "hud.frame_low1",
        "hud.jitter",
        "hud.particles",
        "hud.lights",
        "hud.bodies",
        "hud.fx",
    ];

    private static readonly string[] ResultModelPaths =
    [
        "result.won",
        "result.crystals",
        "result.time",
        "result.score",
        "result.reason",
        "result.title_text",
        "result.reason_text",
        "result.seed_text",
        "result.region_text",
        "result.depth_cells",
        "result.elapsed_seconds",
    ];

    internal static ReadOnlySpan<string> MenuModelPathNames => MenuModelPaths;

    internal static ReadOnlySpan<string> HudModelPathNames => HudModelPaths;

    internal static ReadOnlySpan<string> TelemetryModelPathNames => TelemetryModelPaths;

    internal static ReadOnlySpan<string> ResultModelPathNames => ResultModelPaths;

    private static readonly UiActionId StartGameAction = Action("start_game");
    private static readonly UiActionId SelectCampaignAction = Action("select_campaign");
    private static readonly UiActionId SelectSandboxAction = Action("select_sandbox");
    private static readonly UiActionId OpenSettingsAction = Action("open_settings");
    private static readonly UiActionId OpenInventoryAction = Action("open_inventory");
    private static readonly UiActionId OpenDialogAction = Action("open_dialog");
    private static readonly UiActionId BackMainAction = Action("back_main");
    private static readonly UiActionId CloseDialogAction = Action("close_dialog");
    private static readonly UiActionId PauseGameAction = Action("pause_game");
    private static readonly UiActionId ToggleTelemetryAction = Action("toggle_telemetry");
    private static readonly UiActionId ResumeGameAction = Action("resume_game");
    private static readonly UiActionId RestartGameAction = Action("restart_game");
    private static readonly UiActionId QuitGameAction = Action("quit_game");
    private static readonly UiActionId ToggleAudioAction = Action("toggle_audio");
    private static readonly UiActionId ToggleVSyncAction = Action("toggle_vsync");
    private static readonly UiPathId SettingsAudioPath = Path("settings.audio");
    private static readonly UiPathId SettingsVSyncPath = Path("settings.vsync");
    private static readonly UiPathId MenuCampaignSelectedPath = Path("menu.campaign_selected");
    private static readonly UiPathId MenuSandboxSelectedPath = Path("menu.sandbox_selected");
    private static readonly UiPathId MenuModeTextPath = Path("menu.mode_text");
    private static readonly UiPathId HudModeTextPath = Path("hud.mode_text");
    private static readonly UiPathId HudSeedTextPath = Path("hud.seed_text");
    private static readonly UiPathId HudRegionTextPath = Path("hud.region_text");
    private static readonly UiPathId HudRunStateTextPath = Path("hud.run_state_text");
    private static readonly UiPathId HudDepthCellsPath = Path("hud.depth_cells");
    private static readonly UiPathId HudHealthPath = Path("hud.health");
    private static readonly UiPathId HudWeaponPath = Path("hud.weapon");
    private static readonly UiPathId HudAmmoPath = Path("hud.ammo");
    private static readonly UiPathId HudCooldownPath = Path("hud.cooldown");
    private static readonly UiPathId HudHeatPath = Path("hud.heat");
    private static readonly UiPathId HudReloadPath = Path("hud.reload");
    private static readonly UiPathId HudOverheatedPath = Path("hud.overheated");
    private static readonly UiPathId HudMaterialSlotPath = Path("hud.material_slot");
    private static readonly UiPathId HudBrushRadiusPath = Path("hud.brush_radius");
    private static readonly UiPathId HudExplosionsPath = Path("hud.explosions");
    private static readonly UiPathId HudShotsPath = Path("hud.shots");
    private static readonly UiPathId HudCollapseIslandsPath = Path("hud.collapse_islands");
    private static readonly UiPathId HudCollapseScanPath = Path("hud.collapse_scan");
    private static readonly UiPathId HudCrystalsPath = Path("hud.crystals");
    private static readonly UiPathId HudTimePath = Path("hud.time");
    private static readonly UiPathId HudHazardPath = Path("hud.hazard");
    private static readonly UiPathId HudScorePath = Path("hud.score");
    private static readonly UiPathId HudDistancePath = Path("hud.distance");
    private static readonly UiPathId HudLongitudePath = Path("hud.longitude");
    private static readonly UiPathId HudDepthPath = Path("hud.depth");
    private static readonly UiPathId HudElevationPath = Path("hud.elevation");
    private static readonly UiPathId HudFpsPath = Path("hud.fps");
    private static readonly UiPathId HudFrameP99Path = Path("hud.frame_p99");
    private static readonly UiPathId HudFrameLow1Path = Path("hud.frame_low1");
    private static readonly UiPathId HudJitterPath = Path("hud.jitter");
    private static readonly UiPathId HudParticlesPath = Path("hud.particles");
    private static readonly UiPathId HudLightsPath = Path("hud.lights");
    private static readonly UiPathId HudBodiesPath = Path("hud.bodies");
    private static readonly UiPathId HudFxPath = Path("hud.fx");
    private static readonly UiPathId ResultWonPath = Path("result.won");
    private static readonly UiPathId ResultCrystalsPath = Path("result.crystals");
    private static readonly UiPathId ResultTimePath = Path("result.time");
    private static readonly UiPathId ResultScorePath = Path("result.score");
    private static readonly UiPathId ResultReasonPath = Path("result.reason");
    private static readonly UiPathId ResultTitleTextPath = Path("result.title_text");
    private static readonly UiPathId ResultReasonTextPath = Path("result.reason_text");
    private static readonly UiPathId ResultSeedTextPath = Path("result.seed_text");
    private static readonly UiPathId ResultRegionTextPath = Path("result.region_text");
    private static readonly UiPathId ResultDepthCellsPath = Path("result.depth_cells");
    private static readonly UiPathId ResultElapsedSecondsPath = Path("result.elapsed_seconds");

    private IGameUiService? _ui;
    private IRuntimeControlApi? _runtime;
    private PlayerHealth? _health;
    private PlayerController? _player;
    private WeaponController? _weapons;
    private MaterialBrush? _brush;
    private ExplosiveTool? _explosive;
    private PlayableProjectileTool? _projectile;
    private MissionDirector? _mission;
    private RisingHazardDirector? _hazard;
    private GoalTrigger? _goal;
    private CampaignRunDirector? _runDirector;
    private GoalTrigger? _goalProgressSource;
    private float _goalRouteStartCenterX;
    private bool _subscribed;
    private bool _pausedByUi;
    private bool _resultVisible;
    private bool _settingsAudioEnabled = true;
    private bool _settingsVSyncEnabled = true;
    private string _modalScreenId = string.Empty;
    private MissionState _lastMissionState = MissionState.Playing;
    private bool _lastGoalReached;
    private bool _objectiveSearchCompleted;
    private long _lastEscapeFrame = -1;
    private int _publishedMenuMode = -1;
    private int _publishedHudMode = -1;
    private int _publishedHudState = -1;
    private int _publishedHudRegion = -1;
    private ulong _publishedHudSeed = ulong.MaxValue;

    /// <summary>
    /// 当前主菜单屏幕句柄。
    /// </summary>
    public UiScreenHandle MainScreen { get; private set; }

    /// <summary>
    /// 当前 HUD 屏幕句柄。
    /// </summary>
    public UiScreenHandle HudScreenHandle { get; private set; }

    /// <summary>
    /// 当前可选遥测面板句柄；默认不显示。
    /// </summary>
    public UiScreenHandle TelemetryScreenHandle { get; private set; }

    /// <summary>
    /// 当前模态屏幕句柄；无模态时为 default。
    /// </summary>
    public UiScreenHandle ModalScreen { get; private set; }

    /// <summary>
    /// 当前场景实际物化的 Canvas 数量；用于 Demo dogfood 与运行诊断。
    /// </summary>
    public int CanvasCount { get; private set; }

    /// <summary>
    /// 按场景排序解析出的 Constant Pixel Size overlay Canvas。
    /// </summary>
    public UiCanvasHandle PixelOverlayCanvas { get; private set; }

    /// <summary>
    /// 按场景排序解析出的 Constant Physical Size overlay Canvas。
    /// </summary>
    public UiCanvasHandle PhysicalOverlayCanvas { get; private set; }

    /// <summary>
    /// Constant Pixel Size Canvas 上按需显示的缩放诊断屏幕句柄。
    /// </summary>
    public UiScreenHandle PixelOverlayScreenHandle { get; private set; }

    /// <summary>
    /// Constant Physical Size Canvas 上按需显示的缩放诊断屏幕句柄。
    /// </summary>
    public UiScreenHandle PhysicalOverlayScreenHandle { get; private set; }

    /// <summary>
    /// 最近处理的 UI 动作。
    /// </summary>
    public UiActionId LastAction { get; private set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        if (Entity.TryGetComponent(out CampaignRunDirector runDirector))
        {
            _runDirector = runDirector;
        }

        StartForService(Context.GameUi, TryResolveRuntime());
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        HandleEscapeShortcut();
        // 每帧聚合玩法数据源并推送到 HUD 绑定路径
        PublishHudState();
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        _ = gui;
        // 暂停态不执行 OnUpdate；借 GUI 相位继续接收 Esc，且不在这里绘制任何旧式窗口。
        HandleEscapeShortcut();
        if (_runDirector?.State == CampaignRunState.RunSummary)
        {
            PublishCampaignRunState();
        }
    }

    internal void StartForService(IGameUiService ui)
    {
        StartForService(ui, runtime: null);
    }

    internal void StartForService(IGameUiService ui, IRuntimeControlApi? runtime)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (_subscribed)
        {
            if (ReferenceEquals(_ui, ui))
            {
                _runtime = runtime;
                return;
            }

            StopForService();
        }

        _ui = ui;
        _runtime = runtime;
        _ui.UiEventRaised += HandleUiEvent;
        _subscribed = true;
        DiscoverSceneCanvases();
        // 原子世界重建后的新脚本生命周期直接进入 StartingRun；普通启动停在主菜单。
        if (_runDirector is not null && _runDirector.State != CampaignRunState.MainMenu)
        {
            ShowHud();
            PublishCampaignRunState();
        }
        else
        {
            MainScreen = _ui.ShowScreen(MainMenuScreen);
            if (_runDirector is not null)
            {
                PublishMenuState();
            }
        }

        RefreshSettingsStateFromRuntime();
    }

    internal void BindRunDirector(CampaignRunDirector runDirector)
    {
        ArgumentNullException.ThrowIfNull(runDirector);
        _runDirector = runDirector;
        InvalidateRunModelCache();
        PublishMenuState();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        StopForService();
    }

    /// <summary>
    /// 对称结束一次 UI play session，移除本控制器创建的全部 screen 并清空运行时引用。
    /// </summary>
    internal void StopForService()
    {
        IGameUiService? ui = _ui;
        if (_subscribed && ui is not null)
        {
            ui.UiEventRaised -= HandleUiEvent;
        }

        _subscribed = false;
        HideScreenIfVisible(ui, ModalScreen);
        HideScreenIfVisible(ui, TelemetryScreenHandle);
        HideScreenIfVisible(ui, PixelOverlayScreenHandle);
        HideScreenIfVisible(ui, PhysicalOverlayScreenHandle);
        HideScreenIfVisible(ui, HudScreenHandle);
        HideScreenIfVisible(ui, MainScreen);
        ModalScreen = default;
        TelemetryScreenHandle = default;
        HudScreenHandle = default;
        MainScreen = default;
        PixelOverlayScreenHandle = default;
        PhysicalOverlayScreenHandle = default;
        CanvasCount = 0;
        PixelOverlayCanvas = default;
        PhysicalOverlayCanvas = default;
        _ui = null;
        _runtime = null;
        _health = null;
        _player = null;
        _weapons = null;
        _brush = null;
        _explosive = null;
        _projectile = null;
        _mission = null;
        _hazard = null;
        _goal = null;
        _runDirector = null;
        _goalProgressSource = null;
        _goalRouteStartCenterX = 0f;
        _pausedByUi = false;
        _resultVisible = false;
        _modalScreenId = string.Empty;
        _lastMissionState = MissionState.Playing;
        _lastGoalReached = false;
        _objectiveSearchCompleted = false;
        _lastEscapeFrame = -1;
        InvalidateRunModelCache();
        LastAction = default;
    }

    private void DiscoverSceneCanvases()
    {
        IGameUiService ui = _ui!;
        Span<UiCanvasHandle> canvases = stackalloc UiCanvasHandle[8];
        CanvasCount = ui.CopyCanvases(canvases);
        UiCanvasHandle primary = ui.PrimaryCanvas;
        int secondaryIndex = 0;
        for (int i = 0; i < CanvasCount; i++)
        {
            UiCanvasHandle canvas = canvases[i];
            if (canvas == primary)
            {
                continue;
            }

            if (secondaryIndex++ == 0)
            {
                PixelOverlayCanvas = canvas;
            }
            else if (secondaryIndex == 2)
            {
                PhysicalOverlayCanvas = canvas;
            }
        }
    }

    /// <summary>
    /// 处理 UI 服务上报的动作事件，分派到菜单、暂停、设置与运行时控制。
    /// </summary>
    internal void HandleUiEvent(UiEvent uiEvent)
    {
        LastAction = uiEvent.Action;
        // UI 动作分派：主菜单 / 暂停 / 设置模态 / 运行时控制
        if (uiEvent.Action == SelectCampaignAction)
        {
            _ = _runDirector?.SelectMode(DemoGameMode.Campaign);
            PublishMenuState();
            return;
        }

        if (uiEvent.Action == SelectSandboxAction)
        {
            _ = _runDirector?.SelectMode(DemoGameMode.InfiniteSandbox);
            PublishMenuState();
            return;
        }

        if (uiEvent.Action == StartGameAction)
        {
            if (_runDirector is not null && !_runDirector.StartSelectedRun())
            {
                return;
            }

            ShowHud();
            if (HudScreenHandle.Value != 0)
            {
                HideMainMenu();
                if (_runDirector is not null)
                {
                    PublishCampaignRunState();
                }
            }

            return;
        }

        if (uiEvent.Action == ToggleTelemetryAction)
        {
            ToggleTelemetry();
            return;
        }

        if (uiEvent.Action == PauseGameAction)
        {
            OpenPauseMenu();
            return;
        }

        if (uiEvent.Action == ResumeGameAction)
        {
            ResumeGame();
            return;
        }

        if (uiEvent.Action == RestartGameAction)
        {
            RequestRestart();
            return;
        }

        if (uiEvent.Action == QuitGameAction)
        {
            RequestShutdown();
            return;
        }

        if (uiEvent.Action == ToggleAudioAction)
        {
            ToggleAudio();
            return;
        }

        if (uiEvent.Action == ToggleVSyncAction)
        {
            ToggleVSync();
            return;
        }

        if (uiEvent.Action == OpenSettingsAction)
        {
            OpenModal(SettingsScreen);
            return;
        }

        if (uiEvent.Action == OpenInventoryAction)
        {
            OpenModal(InventoryScreen);
            return;
        }

        if (uiEvent.Action == OpenDialogAction)
        {
            OpenModal(DialogScreen);
            return;
        }

        if (uiEvent.Action == BackMainAction || uiEvent.Action == CloseDialogAction)
        {
            CloseModalOrReturnToPause();
        }
    }

    private void PublishHudDefaults()
    {
        if (_ui is null || HudScreenHandle.Value == 0)
        {
            return;
        }

        SetHudValue(HudHealthPath, 1.0);
        SetHudValue(HudAmmoPath, 0.0);
        SetHudValue(HudCooldownPath, 1.0);
        SetHudValue(HudHeatPath, 0.0);
        SetHudValue(HudReloadPath, 0.0);
        SetHudValue(HudCrystalsPath, 0.0);
        SetHudValue(HudTimePath, 1.0);
        SetHudValue(HudHazardPath, 0.0);
        SetHudValue(HudScorePath, 0.0);
        SetHudValue(HudDistancePath, 0.0);
        SetHudValue(HudLongitudePath, 0.5);
        SetHudValue(HudDepthPath, 0.0);
        SetHudValue(HudElevationPath, 0.0);
        SetScreenValue(HudScreenHandle, HudDepthCellsPath, new UiValue(0L));
        SetTelemetryValue(HudWeaponPath, 0.0);
        SetTelemetryValue(HudOverheatedPath, 0.0);
        SetTelemetryValue(HudMaterialSlotPath, 0.0);
        SetTelemetryValue(HudBrushRadiusPath, 0.0);
        SetTelemetryValue(HudExplosionsPath, 0.0);
        SetTelemetryValue(HudShotsPath, 0.0);
        SetTelemetryValue(HudCollapseIslandsPath, 0.0);
        SetTelemetryValue(HudCollapseScanPath, 0.0);
        SetTelemetryValue(HudFpsPath, 0.0);
        SetTelemetryValue(HudFrameP99Path, 0.0);
        SetTelemetryValue(HudFrameLow1Path, 0.0);
        SetTelemetryValue(HudJitterPath, 0.0);
        SetTelemetryValue(HudParticlesPath, 0.0);
        SetTelemetryValue(HudLightsPath, 0.0);
        SetTelemetryValue(HudBodiesPath, 0.0);
        SetTelemetryValue(HudFxPath, 0.0);
    }

    private void PublishHudState()
    {
        if (_ui is null || HudScreenHandle.Value == 0)
        {
            return;
        }

        // HUD 刷新流水线：解析引用 → 健康/武器/工具/沙盒探索/诊断
        ResolveHudSources();
        PublishHealth();
        PublishWeapon();
        PublishTools();
        PublishMission();
        PublishDiagnostics();
    }

    private void ResolveHudSources()
    {
        if (_runDirector is null && Entity.TryGetComponent(out CampaignRunDirector runDirector))
        {
            _runDirector = runDirector;
        }

        if (_health is null && Entity.TryGetComponent(out PlayerHealth health))
        {
            _health = health;
        }

        if (_player is null && Entity.TryGetComponent(out PlayerController player))
        {
            _player = player;
        }

        if (_weapons is null && Entity.TryGetComponent(out WeaponController weapons))
        {
            _weapons = weapons;
        }

        if (_brush is null && Entity.TryGetComponent(out MaterialBrush brush))
        {
            _brush = brush;
        }

        if (_explosive is null && Entity.TryGetComponent(out ExplosiveTool explosive))
        {
            _explosive = explosive;
        }

        if (_projectile is null && Entity.TryGetComponent(out PlayableProjectileTool projectile))
        {
            _projectile = projectile;
        }

        if (_mission is null && Entity.TryGetComponent(out MissionDirector mission))
        {
            _mission = mission;
        }

        if (_hazard is null && Entity.TryGetComponent(out RisingHazardDirector hazard))
        {
            _hazard = hazard;
        }

        if (_goal is null && Entity.TryGetComponent(out GoalTrigger goal))
        {
            _goal = goal;
        }

        if (HasAllHudSources())
        {
            return;
        }

        if (_health is null && Context.Scene.TryGetFirstComponent(out PlayerHealth? sceneHealth))
        {
            _health = sceneHealth;
        }

        if (_player is null && Context.Scene.TryGetFirstComponent(out PlayerController? scenePlayer))
        {
            _player = scenePlayer;
        }

        if (_weapons is null && Context.Scene.TryGetFirstComponent(out WeaponController? sceneWeapons))
        {
            _weapons = sceneWeapons;
        }

        if (_brush is null && Context.Scene.TryGetFirstComponent(out MaterialBrush? sceneBrush))
        {
            _brush = sceneBrush;
        }

        if (_explosive is null && Context.Scene.TryGetFirstComponent(out ExplosiveTool? sceneExplosive))
        {
            _explosive = sceneExplosive;
        }

        if (_projectile is null && Context.Scene.TryGetFirstComponent(out PlayableProjectileTool? sceneProjectile))
        {
            _projectile = sceneProjectile;
        }

        if (_mission is null && Context.Scene.TryGetFirstComponent(out MissionDirector? sceneMission))
        {
            _mission = sceneMission;
        }

        if (_hazard is null && Context.Scene.TryGetFirstComponent(out RisingHazardDirector? sceneHazard))
        {
            _hazard = sceneHazard;
        }

        if (_goal is null && Context.Scene.TryGetFirstComponent(out GoalTrigger? sceneGoal))
        {
            _goal = sceneGoal;
        }

        _objectiveSearchCompleted = true;
    }

    private bool HasAllHudSources()
    {
        return _health is not null &&
            _player is not null &&
            _weapons is not null &&
            _brush is not null &&
            _explosive is not null &&
            _projectile is not null &&
            (_mission is not null || _goal is not null || _objectiveSearchCompleted);
    }

    private void PublishHealth()
    {
        if (_health is null)
        {
            SetHudValue(HudHealthPath, 1.0);
            return;
        }

        SetHudValue(HudHealthPath, Ratio(_health.Health, MathF.Max(1f, _health.MaxHealth)));
    }

    private void PublishWeapon()
    {
        if (_weapons?.Catalog is not { Weapons.Length: > 0 } catalog)
        {
            SetTelemetryValue(HudWeaponPath, 0.0);
            SetHudValue(HudAmmoPath, 0.0);
            SetHudValue(HudCooldownPath, 1.0);
            SetHudValue(HudHeatPath, 0.0);
            SetHudValue(HudReloadPath, 0.0);
            SetTelemetryValue(HudOverheatedPath, 0.0);
            return;
        }

        int selected = Math.Clamp(_weapons.SelectedIndex, 0, catalog.Weapons.Length - 1);
        WeaponDefinition weapon = catalog.Weapons[selected];
        SetTelemetryValue(HudWeaponPath, catalog.Weapons.Length <= 1 ? 0.0 : Ratio(selected, catalog.Weapons.Length - 1));
        SetHudValue(HudAmmoPath, weapon.AmmoMax <= 0 ? 0.0 : Ratio(_weapons.CurrentAmmo, weapon.AmmoMax));
        SetHudValue(HudCooldownPath, weapon.CooldownSeconds <= 0f ? 1.0 : 1.0 - Ratio(_weapons.CooldownRemaining, weapon.CooldownSeconds));
        SetHudValue(HudHeatPath, Ratio(_weapons.Heat, 100f));
        SetHudValue(HudReloadPath, _weapons.IsReloading && weapon.ReloadSeconds > 0f ? Ratio(_weapons.ReloadRemaining, weapon.ReloadSeconds) : 0.0);
        SetTelemetryValue(HudOverheatedPath, _weapons.IsOverheated ? 1.0 : 0.0);
    }

    private void PublishTools()
    {
        if (_brush is null)
        {
            SetTelemetryValue(HudMaterialSlotPath, 0.0);
            SetTelemetryValue(HudBrushRadiusPath, 0.0);
        }
        else
        {
            int slotCount = Math.Max(1, _brush.MaterialSlotCount);
            SetTelemetryValue(HudMaterialSlotPath, slotCount <= 1 ? 0.0 : Ratio(_brush.SelectedIndex, slotCount - 1));
            SetTelemetryValue(HudBrushRadiusPath, Ratio(_brush.Radius, Math.Max(1, _brush.MaxRadius)));
        }

        SetTelemetryValue(HudExplosionsPath, _explosive is null ? 0.0 : Ratio(_explosive.ExplosionCount, 10.0));
        SetTelemetryValue(HudShotsPath, Ratio(_weapons?.PrimaryFireCount ?? _projectile?.ShotsFired ?? 0, 10.0));
        if (_projectile is null)
        {
            SetTelemetryValue(HudCollapseIslandsPath, 0.0);
            SetTelemetryValue(HudCollapseScanPath, 0.0);
            return;
        }

        int scanRadius = Math.Clamp(_projectile.CollapseScanRadius, 4, 320);
        double scanCapacity = ((scanRadius * 2) + 1) * ((scanRadius * 2) + 1);
        SetTelemetryValue(HudCollapseIslandsPath, Ratio(_projectile.CollapsedFloatingIslands, 10.0));
        SetTelemetryValue(HudCollapseScanPath, Ratio(_projectile.LastCollapseSolidCandidates, scanCapacity));
    }

    private void PublishMission()
    {
        if (_runDirector is not null)
        {
            PublishCampaignRunState();
            return;
        }

        MissionDirector? mission = _mission;
        if (mission is null && _goal is null)
        {
            PublishSandboxExploration();
            return;
        }

        if (mission is null)
        {
            double goalProgress = PublishGoal();
            SetHudValue(HudTimePath, 1.0 - goalProgress);
            SetHudValue(HudHazardPath, _hazard is null ? 0.0 : HazardRatio(_hazard.StartSurfaceY, _hazard.CurrentSurfaceY));
            SetHudValue(HudScorePath, 0.0);
            return;
        }

        SetHudValue(HudCrystalsPath, Ratio(mission.CrystalsCollected, Math.Max(1, mission.RequiredCrystals)));
        SetHudValue(HudTimePath, mission.TimeLimitSeconds <= 0f ? 0.0 : Ratio(mission.RemainingSeconds, mission.TimeLimitSeconds));
        SetHudValue(HudHazardPath, HazardRatio(mission.InitialLavaSurfaceY, mission.LavaSurfaceY));
        SetHudValue(HudScorePath, Ratio(mission.Score, 10000.0));
        PublishResultState(mission);
    }

    private void PublishSandboxExploration()
    {
        double x = _player?.CenterX ?? PlayableCavernWorldGenerator.PlayerSpawnX;
        double y = _player?.CenterY ?? PlayableCavernWorldGenerator.PlayerSpawnY;
        double distance = Math.Abs(x - PlayableCavernWorldGenerator.PlayerSpawnX);
        SetHudValue(HudDistancePath, Ratio(distance, 4_096.0));
        SetHudValue(HudLongitudePath, Math.Clamp(0.5 + ((x - PlayableCavernWorldGenerator.PlayerSpawnX) / 8_192.0), 0.0, 1.0));
        SetHudValue(HudDepthPath, Ratio(Math.Max(0.0, y - PlayableCavernWorldGenerator.SafeSurfaceY), 2_048.0));
        SetHudValue(HudElevationPath, Ratio(Math.Max(0.0, PlayableCavernWorldGenerator.SafeSurfaceY - y), 512.0));
        SetHudValue(HudCrystalsPath, 0.0);
        SetHudValue(HudTimePath, 1.0);
        SetHudValue(HudHazardPath, 0.0);
        SetHudValue(HudScorePath, 0.0);
        _lastGoalReached = false;
        _lastMissionState = MissionState.Playing;
    }

    private void PublishCampaignRunState()
    {
        CampaignRunDirector? run = _runDirector;
        if (run is null || _ui is null || HudScreenHandle.Value == 0)
        {
            return;
        }

        double x = _player?.CenterX ?? PlayableCavernWorldGenerator.PlayerSpawnX;
        double distance = Math.Abs(x - PlayableCavernWorldGenerator.PlayerSpawnX);
        SetHudValue(HudDistancePath, Ratio(distance, 4_096.0));
        SetHudValue(HudLongitudePath, Math.Clamp(0.5 + ((x - PlayableCavernWorldGenerator.PlayerSpawnX) / 8_192.0), 0.0, 1.0));
        SetHudValue(HudDepthPath, Ratio(run.CurrentDepthCells, 5_120.0));
        SetHudValue(HudElevationPath, 0.0);
        SetHudValue(HudCrystalsPath, Ratio(CountVisitedRegions(run.VisitedRegionMask), CampaignConfig.RequiredRegionCount));
        SetHudValue(HudTimePath, Ratio(run.ElapsedSeconds, 3_600.0));
        SetHudValue(HudHazardPath, run.State is CampaignRunState.Finale or CampaignRunState.Dead ? 1.0 : 0.0);
        SetHudValue(HudScorePath, Ratio(run.DeepestDepthCells, 5_120.0));
        SetScreenValue(HudScreenHandle, HudDepthCellsPath, new UiValue(run.CurrentDepthCells));
        PublishRunText(run);
        PublishCampaignResult(run);
        _lastGoalReached = false;
        _lastMissionState = MissionState.Playing;
    }

    private void PublishRunText(CampaignRunDirector run)
    {
        int mode = (int)run.Mode;
        if (_publishedHudMode != mode)
        {
            SetScreenText(HudScreenHandle, HudModeTextPath, run.ModeDisplayName);
            _publishedHudMode = mode;
        }

        int state = (int)run.State;
        if (_publishedHudState != state)
        {
            SetScreenText(HudScreenHandle, HudRunStateTextPath, run.StateDisplayName);
            _publishedHudState = state;
        }

        if (_publishedHudRegion != run.CurrentRegionIndex)
        {
            SetScreenText(HudScreenHandle, HudRegionTextPath, run.CurrentRegionDisplayName);
            _publishedHudRegion = run.CurrentRegionIndex;
        }

        if (_publishedHudSeed != run.RunSeed)
        {
            SetScreenText(HudScreenHandle, HudSeedTextPath, run.RunSeedText);
            _publishedHudSeed = run.RunSeed;
        }
    }

    private void PublishCampaignResult(CampaignRunDirector run)
    {
        if (run.State != CampaignRunState.RunSummary)
        {
            return;
        }

        if (!_resultVisible || _modalScreenId != ResultScreen)
        {
            _pausedByUi = false;
            OpenModal(ResultScreen);
            _resultVisible = ModalScreen.Value != 0 && _modalScreenId == ResultScreen;
        }

        if (!_resultVisible)
        {
            return;
        }

        SetScreenValue(ModalScreen, ResultWonPath, new UiValue(run.WasCompleted ? 1.0 : 0.0));
        SetScreenValue(
            ModalScreen,
            ResultCrystalsPath,
            new UiValue(Ratio(CountVisitedRegions(run.VisitedRegionMask), CampaignConfig.RequiredRegionCount)));
        SetScreenValue(ModalScreen, ResultTimePath, new UiValue(Ratio(run.ElapsedSeconds, 3_600.0)));
        SetScreenValue(ModalScreen, ResultScorePath, new UiValue(Ratio(run.DeepestDepthCells, 5_120.0)));
        SetScreenValue(ModalScreen, ResultReasonPath, new UiValue(1.0));
        SetScreenValue(ModalScreen, ResultDepthCellsPath, new UiValue(run.DeepestDepthCells));
        SetScreenValue(ModalScreen, ResultElapsedSecondsPath, new UiValue((double)run.ElapsedSeconds));
        SetScreenText(
            ModalScreen,
            ResultTitleTextPath,
            run.WasCompleted ? "战役完成 / Campaign Complete" : "本轮结束 / Run Ended");
        SetScreenText(ModalScreen, ResultReasonTextPath, run.ResultReason);
        SetScreenText(ModalScreen, ResultSeedTextPath, run.RunSeedText);
        SetScreenText(ModalScreen, ResultRegionTextPath, run.CurrentRegionDisplayName);
    }

    private static int CountVisitedRegions(byte mask)
    {
        int count = 0;
        while (mask != 0)
        {
            count += mask & 1;
            mask >>= 1;
        }

        return count;
    }

    private double PublishGoal()
    {
        GoalTrigger? goal = _goal;
        if (goal is null)
        {
            SetHudValue(HudCrystalsPath, 0.0);
            _lastGoalReached = false;
            return 0.0;
        }

        double progress = GoalProgress(goal);
        SetHudValue(HudCrystalsPath, progress);
        PublishGoalResultState(goal);
        return progress;
    }

    private double GoalProgress(GoalTrigger goal)
    {
        if (goal.Reached)
        {
            return 1.0;
        }

        PlayerController? player = _player;
        if (player is null)
        {
            return 0.0;
        }

        if (!ReferenceEquals(_goalProgressSource, goal))
        {
            _goalProgressSource = goal;
            _goalRouteStartCenterX = player.CenterX;
        }

        float startCenterX = _goalRouteStartCenterX;
        float goalCenterX = goal.X + (goal.Width * 0.5f);
        float distance = goalCenterX - startCenterX;
        return MathF.Abs(distance) <= 0.001f
            ? player.CenterX >= goalCenterX ? 1.0 : 0.0
            : Math.Clamp((player.CenterX - startCenterX) / distance, 0.0f, 1.0f);
    }

    private void PublishDiagnostics()
    {
        EngineDiagnosticsSnapshot diagnostics = Context.Diagnostics.Capture();
        SetTelemetryValue(HudFpsPath, Ratio(diagnostics.FramesPerSecond, 120.0));
        SetTelemetryValue(HudFrameP99Path, Ratio(diagnostics.FrameP99Milliseconds, 50.0));
        SetTelemetryValue(HudFrameLow1Path, Ratio(diagnostics.FrameLow1PercentFps, 120.0));
        SetTelemetryValue(HudJitterPath, Ratio(diagnostics.FrameJitterMilliseconds, 20.0));
        SetTelemetryValue(HudParticlesPath, Ratio(diagnostics.FreeParticles, 1000.0));
        SetTelemetryValue(HudLightsPath, Ratio(diagnostics.PointLights, 64.0));
        SetTelemetryValue(HudBodiesPath, Ratio(diagnostics.RigidBodies, 128.0));
        SetTelemetryValue(HudFxPath, Ratio(TransientParticleBurst.ActiveCount(Context.Scene), 16.0));
    }

    // 任务胜负时暂停模拟并弹出结算模态，持续刷新结果绑定
    private void PublishResultState(MissionDirector mission)
    {
        if (mission.State == MissionState.Playing)
        {
            _lastMissionState = MissionState.Playing;
            return;
        }

        if (!_resultVisible || _lastMissionState != mission.State || _modalScreenId != ResultScreen)
        {
            _runtime?.PauseSimulation();
            _pausedByUi = false;
            OpenModal(ResultScreen);
            _resultVisible = ModalScreen.Value != 0 && _modalScreenId == ResultScreen;
        }

        if (_resultVisible)
        {
            SetScreenValue(ModalScreen, ResultWonPath, new UiValue(mission.State == MissionState.Won ? 1.0 : 0.0));
            SetScreenValue(ModalScreen, ResultCrystalsPath, new UiValue(Ratio(mission.CrystalsCollected, Math.Max(1, mission.RequiredCrystals))));
            SetScreenValue(ModalScreen, ResultTimePath, new UiValue(mission.TimeLimitSeconds <= 0f ? 0.0 : Ratio(mission.RemainingSeconds, mission.TimeLimitSeconds)));
            SetScreenValue(ModalScreen, ResultScorePath, new UiValue(Ratio(mission.Score, 10000.0)));
            SetScreenValue(ModalScreen, ResultReasonPath, new UiValue(string.IsNullOrWhiteSpace(mission.ResultReason) ? 0.0 : 1.0));
        }

        _lastMissionState = mission.State;
    }

    private void PublishGoalResultState(GoalTrigger goal)
    {
        if (!goal.Reached)
        {
            _lastGoalReached = false;
            return;
        }

        if (!_resultVisible || !_lastGoalReached || _modalScreenId != ResultScreen)
        {
            _runtime?.PauseSimulation();
            _pausedByUi = false;
            OpenModal(ResultScreen);
            _resultVisible = ModalScreen.Value != 0 && _modalScreenId == ResultScreen;
        }

        if (_resultVisible)
        {
            SetScreenValue(ModalScreen, ResultWonPath, new UiValue(1.0));
            SetScreenValue(ModalScreen, ResultCrystalsPath, new UiValue(1.0));
            SetScreenValue(ModalScreen, ResultTimePath, new UiValue(0.0));
            SetScreenValue(ModalScreen, ResultScorePath, new UiValue(0.0));
            SetScreenValue(ModalScreen, ResultReasonPath, new UiValue(1.0));
        }

        _lastGoalReached = true;
    }

    private void SetHudValue(UiPathId path, double value)
    {
        SetScreenValue(HudScreenHandle, path, new UiValue(Clamp01(value)));
    }

    private void SetTelemetryValue(UiPathId path, double value)
    {
        // RmlUi 的 model path 是每份文档的严格契约；诊断页隐藏时不能把其字段误写进 HUD 文档。
        SetScreenValue(TelemetryScreenHandle, path, new UiValue(Clamp01(value)));
    }

    private void SetScreenValue(UiScreenHandle screen, UiPathId path, in UiValue value)
    {
        if (_ui is null || screen.Value == 0)
        {
            return;
        }

        _ui.SetValue(screen, path, in value);
    }

    private void SetScreenText(UiScreenHandle screen, UiPathId path, string text)
    {
        if (_ui is null || screen.Value == 0)
        {
            return;
        }

        UiValue value = UiValue.FromStringHandle(_ui.InternString(text));
        _ui.SetValue(screen, path, in value);
    }

    private static double Ratio(double value, double max)
    {
        return !double.IsFinite(value) || !double.IsFinite(max) || max <= 0.0
            ? 0.0
            : Clamp01(value / max);
    }

    private static double HazardRatio(double initialSurfaceY, double currentSurfaceY)
    {
        return !double.IsFinite(initialSurfaceY) || !double.IsFinite(currentSurfaceY)
            ? 0.0
            : Clamp01((initialSurfaceY - currentSurfaceY) / Math.Max(1.0, Math.Abs(initialSurfaceY)));
    }

    private static double Clamp01(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
    }

    private void HideMainMenu()
    {
        if (_ui is null || MainScreen.Value == 0)
        {
            return;
        }

        _ui.HideScreen(MainScreen);
        MainScreen = default;
    }

    private void PublishMenuState()
    {
        CampaignRunDirector? run = _runDirector;
        if (run is null || MainScreen.Value == 0)
        {
            return;
        }

        int mode = (int)run.Mode;
        if (_publishedMenuMode == mode)
        {
            return;
        }

        SetScreenValue(
            MainScreen,
            MenuCampaignSelectedPath,
            new UiValue(run.Mode == DemoGameMode.Campaign ? 1.0 : 0.0));
        SetScreenValue(
            MainScreen,
            MenuSandboxSelectedPath,
            new UiValue(run.Mode == DemoGameMode.InfiniteSandbox ? 1.0 : 0.0));
        SetScreenText(MainScreen, MenuModeTextPath, run.ModeDisplayName);
        _publishedMenuMode = mode;
    }

    private void ShowHud()
    {
        if (_ui is null || HudScreenHandle.Value != 0)
        {
            return;
        }

        HudScreenHandle = _ui.ShowScreen(HudScreen);
        InvalidateHudModelCache();
        PublishHudDefaults();
    }

    private void ToggleTelemetry()
    {
        if (_ui is null || HudScreenHandle.Value == 0)
        {
            return;
        }

        if (TelemetryScreenHandle.Value != 0)
        {
            _ui.HideScreen(TelemetryScreenHandle);
            TelemetryScreenHandle = default;
            HideScalerDiagnostics();
            return;
        }

        HideScalerDiagnostics();
        TelemetryScreenHandle = _ui.ShowScreen(TelemetryScreen);
        if (TelemetryScreenHandle.Value == 0)
        {
            return;
        }

        if (PixelOverlayCanvas.Value != 0)
        {
            PixelOverlayScreenHandle = _ui.ShowScreen(PixelOverlayCanvas, PixelOverlayScreen);
        }

        if (PhysicalOverlayCanvas.Value != 0)
        {
            PhysicalOverlayScreenHandle = _ui.ShowScreen(PhysicalOverlayCanvas, PhysicalOverlayScreen);
        }

        PublishHudDefaults();
    }

    private void HideScalerDiagnostics()
    {
        HideScreenIfVisible(_ui, PixelOverlayScreenHandle);
        HideScreenIfVisible(_ui, PhysicalOverlayScreenHandle);
        PixelOverlayScreenHandle = default;
        PhysicalOverlayScreenHandle = default;
    }

    private static void HideScreenIfVisible(IGameUiService? ui, UiScreenHandle screen)
    {
        if (ui is not null && screen.Value != 0)
        {
            ui.HideScreen(screen);
        }
    }

    private void HandleEscapeShortcut()
    {
        if (_ui is null ||
            _ui.PrimaryCanvas.Value == 0 ||
            !Context.Input.WasPressed(Key.Escape))
        {
            return;
        }

        long frame = Context.Time.FrameCount;
        if (frame == _lastEscapeFrame)
        {
            return;
        }

        _lastEscapeFrame = frame;
        if (_resultVisible)
        {
            return;
        }

        if (_modalScreenId == PauseScreen)
        {
            ResumeGame();
            return;
        }

        if (ModalScreen.Value != 0)
        {
            if (_pausedByUi)
            {
                CloseModalOrReturnToPause();
            }
            else
            {
                CloseModal();
            }

            return;
        }

        OpenPauseMenu();
    }

    private void OpenPauseMenu()
    {
        if (MainScreen.Value != 0)
        {
            return;
        }

        _runtime?.PauseSimulation();
        _pausedByUi = true;
        OpenModal(PauseScreen);
    }

    private void ResumeGame()
    {
        CloseModal();
        _pausedByUi = false;
        _runtime?.ResumeSimulation();
    }

    private void RequestRestart()
    {
        if (_runDirector?.State == CampaignRunState.RunSummary)
        {
            ClearRuntimeModalState();
            RuntimeControlResult result = _runDirector.RequestNextRun();
            if (!result.Success)
            {
                PublishCampaignResult(_runDirector);
            }

            return;
        }

        if (_runDirector?.Mode == DemoGameMode.InfiniteSandbox)
        {
            _health?.Respawn();
            if (_health is null)
            {
                _player?.Respawn();
            }

            ResumeGame();
            return;
        }

        if (_runDirector?.AbandonRun() == true)
        {
            ResumeGame();
            return;
        }

        ClearRuntimeModalState();
        _lastMissionState = MissionState.Playing;
        _lastGoalReached = false;
        _ = _runtime?.RequestRestartCurrentScene();
    }

    private void RequestShutdown()
    {
        ClearRuntimeModalState();
        _ = _runtime?.RequestShutdown();
    }

    private void ToggleAudio()
    {
        bool target = !_settingsAudioEnabled;
        RuntimeControlResult? result = _runtime?.SetAudioEnabled(target);
        if (result is null || result.Value.Success)
        {
            _settingsAudioEnabled = target;
        }
        else
        {
            RefreshSettingsStateFromRuntime();
        }

        PublishSettingsState();
    }

    private void ToggleVSync()
    {
        bool target = !_settingsVSyncEnabled;
        RuntimeControlResult? result = _runtime?.SetVSyncEnabled(target);
        if (result is null || result.Value.Success)
        {
            _settingsVSyncEnabled = target;
        }
        else
        {
            RefreshSettingsStateFromRuntime();
        }

        PublishSettingsState();
    }

    private void ClearRuntimeModalState()
    {
        CloseModal();
        _pausedByUi = false;
        _resultVisible = false;
    }

    private void OpenModal(string screenId)
    {
        if (_ui is null)
        {
            return;
        }

        CloseModal();
        ModalScreen = _ui.PushModal(screenId);
        _modalScreenId = ModalScreen.Value == 0 ? string.Empty : screenId;
        if (_modalScreenId == SettingsScreen)
        {
            RefreshSettingsStateFromRuntime();
            PublishSettingsState();
        }
    }

    private void RefreshSettingsStateFromRuntime()
    {
        if (_runtime is null)
        {
            return;
        }

        RuntimeSettingsSnapshot settings = _runtime.CaptureSettings();
        _settingsAudioEnabled = settings.AudioEnabled;
        _settingsVSyncEnabled = settings.VSyncEnabled;
    }

    private void PublishSettingsState()
    {
        if (ModalScreen.Value == 0 || _modalScreenId != SettingsScreen)
        {
            return;
        }

        SetScreenValue(ModalScreen, SettingsAudioPath, new UiValue(_settingsAudioEnabled ? 1.0 : 0.0));
        SetScreenValue(ModalScreen, SettingsVSyncPath, new UiValue(_settingsVSyncEnabled ? 1.0 : 0.0));
    }

    private void CloseModalOrReturnToPause()
    {
        bool shouldReturnToPause = _pausedByUi && _modalScreenId != PauseScreen;
        CloseModal();
        if (shouldReturnToPause)
        {
            OpenModal(PauseScreen);
        }
    }

    private void CloseModal()
    {
        if (_ui is null || ModalScreen.Value == 0)
        {
            ModalScreen = default;
            _modalScreenId = string.Empty;
            return;
        }

        _ui.HideScreen(ModalScreen);
        ModalScreen = default;
        _modalScreenId = string.Empty;
    }

    private void InvalidateRunModelCache()
    {
        _publishedMenuMode = -1;
        InvalidateHudModelCache();
    }

    private void InvalidateHudModelCache()
    {
        _publishedHudMode = -1;
        _publishedHudState = -1;
        _publishedHudRegion = -1;
        _publishedHudSeed = ulong.MaxValue;
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

    internal static UiActionId Action(string id)
    {
        return new UiActionId(RuntimeUiStableId.Hash(id));
    }

    internal static UiPathId Path(string id)
    {
        return new UiPathId(RuntimeUiStableId.Hash(id));
    }
}
