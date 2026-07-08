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
    internal const string PauseScreen = "pause";
    internal const string ResultScreen = "result";

    private static readonly string[] HudModelPaths =
    [
        "hud.health",
        "hud.weapon",
        "hud.ammo",
        "hud.cooldown",
        "hud.heat",
        "hud.reload",
        "hud.overheated",
        "hud.material_slot",
        "hud.brush_radius",
        "hud.explosions",
        "hud.shots",
        "hud.collapse_islands",
        "hud.collapse_scan",
        "hud.crystals",
        "hud.time",
        "hud.hazard",
        "hud.score",
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
    ];

    internal static ReadOnlySpan<string> HudModelPathNames => HudModelPaths;

    internal static ReadOnlySpan<string> ResultModelPathNames => ResultModelPaths;

    private static readonly UiActionId StartGameAction = Action("start_game");
    private static readonly UiActionId OpenSettingsAction = Action("open_settings");
    private static readonly UiActionId OpenInventoryAction = Action("open_inventory");
    private static readonly UiActionId OpenDialogAction = Action("open_dialog");
    private static readonly UiActionId BackMainAction = Action("back_main");
    private static readonly UiActionId CloseDialogAction = Action("close_dialog");
    private static readonly UiActionId PauseGameAction = Action("pause_game");
    private static readonly UiActionId ResumeGameAction = Action("resume_game");
    private static readonly UiActionId RestartGameAction = Action("restart_game");
    private static readonly UiActionId QuitGameAction = Action("quit_game");
    private static readonly UiActionId ToggleAudioAction = Action("toggle_audio");
    private static readonly UiActionId ToggleVSyncAction = Action("toggle_vsync");
    private static readonly UiPathId SettingsAudioPath = Path("settings.audio");
    private static readonly UiPathId SettingsVSyncPath = Path("settings.vsync");
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

    private IGameUiService? _ui;
    private IRuntimeControlApi? _runtime;
    private PlayerHealth? _health;
    private WeaponController? _weapons;
    private MaterialBrush? _brush;
    private ExplosiveTool? _explosive;
    private PlayableProjectileTool? _projectile;
    private MissionDirector? _mission;
    private RisingHazardDirector? _hazard;
    private bool _subscribed;
    private bool _pausedByUi;
    private bool _resultVisible;
    private bool _settingsAudioEnabled = true;
    private bool _settingsVSyncEnabled = true;
    private string _modalScreenId = string.Empty;
    private MissionState _lastMissionState = MissionState.Playing;

    /// <summary>
    /// 当前主菜单屏幕句柄。
    /// </summary>
    public UiScreenHandle MainScreen { get; private set; }

    /// <summary>
    /// 当前 HUD 屏幕句柄。
    /// </summary>
    public UiScreenHandle HudScreenHandle { get; private set; }

    /// <summary>
    /// 当前模态屏幕句柄；无模态时为 default。
    /// </summary>
    public UiScreenHandle ModalScreen { get; private set; }

    /// <summary>
    /// 最近处理的 UI 动作。
    /// </summary>
    public UiActionId LastAction { get; private set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        StartForService(Context.GameUi, TryResolveRuntime());
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        PublishHudState();
    }

    internal void StartForService(IGameUiService ui)
    {
        StartForService(ui, runtime: null);
    }

    internal void StartForService(IGameUiService ui, IRuntimeControlApi? runtime)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _runtime = runtime;
        if (_subscribed)
        {
            return;
        }

        _ui = ui;
        _ui.UiEventRaised += HandleUiEvent;
        _subscribed = true;
        MainScreen = _ui.ShowScreen(MainMenuScreen);
        HudScreenHandle = _ui.ShowScreen(HudScreen);
        PublishHudDefaults();
        RefreshSettingsStateFromRuntime();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        if (_subscribed && _ui is not null)
        {
            _ui.UiEventRaised -= HandleUiEvent;
            _subscribed = false;
        }
    }

    internal void HandleUiEvent(UiEvent uiEvent)
    {
        LastAction = uiEvent.Action;
        if (uiEvent.Action == StartGameAction)
        {
            HideMainMenu();
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
        SetHudValue(HudWeaponPath, 0.0);
        SetHudValue(HudAmmoPath, 0.0);
        SetHudValue(HudCooldownPath, 1.0);
        SetHudValue(HudHeatPath, 0.0);
        SetHudValue(HudReloadPath, 0.0);
        SetHudValue(HudOverheatedPath, 0.0);
        SetHudValue(HudMaterialSlotPath, 0.0);
        SetHudValue(HudBrushRadiusPath, 0.0);
        SetHudValue(HudExplosionsPath, 0.0);
        SetHudValue(HudShotsPath, 0.0);
        SetHudValue(HudCollapseIslandsPath, 0.0);
        SetHudValue(HudCollapseScanPath, 0.0);
        SetHudValue(HudCrystalsPath, 0.0);
        SetHudValue(HudTimePath, 1.0);
        SetHudValue(HudHazardPath, 0.0);
        SetHudValue(HudScorePath, 0.0);
        SetHudValue(HudFpsPath, 0.0);
        SetHudValue(HudFrameP99Path, 0.0);
        SetHudValue(HudFrameLow1Path, 0.0);
        SetHudValue(HudJitterPath, 0.0);
        SetHudValue(HudParticlesPath, 0.0);
        SetHudValue(HudLightsPath, 0.0);
        SetHudValue(HudBodiesPath, 0.0);
        SetHudValue(HudFxPath, 0.0);
    }

    private void PublishHudState()
    {
        if (_ui is null || HudScreenHandle.Value == 0)
        {
            return;
        }

        ResolveHudSources();
        PublishHealth();
        PublishWeapon();
        PublishTools();
        PublishMission();
        PublishDiagnostics();
    }

    private void ResolveHudSources()
    {
        if (_health is null && Entity.TryGetComponent(out PlayerHealth health))
        {
            _health = health;
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

        if (HasAllHudSources())
        {
            return;
        }

        ScriptEntityInspection[] entities = Context.Scene.CaptureInspectionSnapshot();
        for (int i = 0; i < entities.Length; i++)
        {
            ScriptComponentInspection[] components = entities[i].Components;
            for (int j = 0; j < components.Length; j++)
            {
                Behaviour behaviour = components[j].Behaviour;
                if (_health is null && behaviour is PlayerHealth sceneHealth)
                {
                    _health = sceneHealth;
                }
                else if (_weapons is null && behaviour is WeaponController sceneWeapons)
                {
                    _weapons = sceneWeapons;
                }
                else if (_brush is null && behaviour is MaterialBrush sceneBrush)
                {
                    _brush = sceneBrush;
                }
                else if (_explosive is null && behaviour is ExplosiveTool sceneExplosive)
                {
                    _explosive = sceneExplosive;
                }
                else if (_projectile is null && behaviour is PlayableProjectileTool sceneProjectile)
                {
                    _projectile = sceneProjectile;
                }
                else if (_mission is null && behaviour is MissionDirector sceneMission)
                {
                    _mission = sceneMission;
                }
                else if (_hazard is null && behaviour is RisingHazardDirector sceneHazard)
                {
                    _hazard = sceneHazard;
                }

                if (HasAllHudSources())
                {
                    return;
                }
            }
        }
    }

    private bool HasAllHudSources()
    {
        return _health is not null && _weapons is not null && _brush is not null && _explosive is not null && _projectile is not null && _mission is not null && _hazard is not null;
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
            SetHudValue(HudWeaponPath, 0.0);
            SetHudValue(HudAmmoPath, 0.0);
            SetHudValue(HudCooldownPath, 1.0);
            SetHudValue(HudHeatPath, 0.0);
            SetHudValue(HudReloadPath, 0.0);
            SetHudValue(HudOverheatedPath, 0.0);
            return;
        }

        int selected = Math.Clamp(_weapons.SelectedIndex, 0, catalog.Weapons.Length - 1);
        WeaponDefinition weapon = catalog.Weapons[selected];
        SetHudValue(HudWeaponPath, catalog.Weapons.Length <= 1 ? 0.0 : Ratio(selected, catalog.Weapons.Length - 1));
        SetHudValue(HudAmmoPath, weapon.AmmoMax <= 0 ? 0.0 : Ratio(_weapons.CurrentAmmo, weapon.AmmoMax));
        SetHudValue(HudCooldownPath, weapon.CooldownSeconds <= 0f ? 1.0 : 1.0 - Ratio(_weapons.CooldownRemaining, weapon.CooldownSeconds));
        SetHudValue(HudHeatPath, Ratio(_weapons.Heat, 100f));
        SetHudValue(HudReloadPath, _weapons.IsReloading && weapon.ReloadSeconds > 0f ? Ratio(_weapons.ReloadRemaining, weapon.ReloadSeconds) : 0.0);
        SetHudValue(HudOverheatedPath, _weapons.IsOverheated ? 1.0 : 0.0);
    }

    private void PublishTools()
    {
        if (_brush is null)
        {
            SetHudValue(HudMaterialSlotPath, 0.0);
            SetHudValue(HudBrushRadiusPath, 0.0);
        }
        else
        {
            int slotCount = Math.Max(1, _brush.MaterialSlotCount);
            SetHudValue(HudMaterialSlotPath, slotCount <= 1 ? 0.0 : Ratio(_brush.SelectedIndex, slotCount - 1));
            SetHudValue(HudBrushRadiusPath, Ratio(_brush.Radius, Math.Max(1, _brush.MaxRadius)));
        }

        SetHudValue(HudExplosionsPath, _explosive is null ? 0.0 : Ratio(_explosive.ExplosionCount, 10.0));
        SetHudValue(HudShotsPath, Ratio(_weapons?.PrimaryFireCount ?? _projectile?.ShotsFired ?? 0, 10.0));
        if (_projectile is null)
        {
            SetHudValue(HudCollapseIslandsPath, 0.0);
            SetHudValue(HudCollapseScanPath, 0.0);
            return;
        }

        int scanRadius = Math.Clamp(_projectile.CollapseScanRadius, 4, 320);
        double scanCapacity = ((scanRadius * 2) + 1) * ((scanRadius * 2) + 1);
        SetHudValue(HudCollapseIslandsPath, Ratio(_projectile.CollapsedFloatingIslands, 10.0));
        SetHudValue(HudCollapseScanPath, Ratio(_projectile.LastCollapseSolidCandidates, scanCapacity));
    }

    private void PublishMission()
    {
        MissionDirector? mission = _mission;
        if (mission is null)
        {
            SetHudValue(HudCrystalsPath, 0.0);
            SetHudValue(HudTimePath, 1.0);
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

    private void PublishDiagnostics()
    {
        EngineDiagnosticsSnapshot diagnostics = Context.Diagnostics.Capture();
        SetHudValue(HudFpsPath, Ratio(diagnostics.FramesPerSecond, 120.0));
        SetHudValue(HudFrameP99Path, Ratio(diagnostics.FrameP99Milliseconds, 50.0));
        SetHudValue(HudFrameLow1Path, Ratio(diagnostics.FrameLow1PercentFps, 120.0));
        SetHudValue(HudJitterPath, Ratio(diagnostics.FrameJitterMilliseconds, 20.0));
        SetHudValue(HudParticlesPath, Ratio(diagnostics.FreeParticles, 1000.0));
        SetHudValue(HudLightsPath, Ratio(diagnostics.PointLights, 64.0));
        SetHudValue(HudBodiesPath, Ratio(diagnostics.RigidBodies, 128.0));
        SetHudValue(HudFxPath, Ratio(TransientParticleBurst.ActiveCount(Context.Scene), 16.0));
    }

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

    private void SetHudValue(UiPathId path, double value)
    {
        SetScreenValue(HudScreenHandle, path, new UiValue(Clamp01(value)));
    }

    private void SetScreenValue(UiScreenHandle screen, UiPathId path, in UiValue value)
    {
        if (_ui is null || screen.Value == 0)
        {
            return;
        }

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

    private void OpenPauseMenu()
    {
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
        ClearRuntimeModalState();
        _lastMissionState = MissionState.Playing;
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
