using PixelEngine.Scripting;
using RuntimeUiStableId = PixelEngine.UI.UiStableId;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 游戏大 UI 控制器，使用公开脚本 UI 服务驱动主菜单、HUD 与模态页面。
/// </summary>
public sealed class GameUiDemoController : Behaviour
{
    internal const string MainMenuScreen = "main-menu";
    internal const string SettingsScreen = "settings";
    internal const string InventoryScreen = "inventory";
    internal const string DialogScreen = "dialog";
    internal const string HudScreen = "hud";

    private static readonly UiActionId StartGameAction = Action("start_game");
    private static readonly UiActionId OpenSettingsAction = Action("open_settings");
    private static readonly UiActionId OpenInventoryAction = Action("open_inventory");
    private static readonly UiActionId OpenDialogAction = Action("open_dialog");
    private static readonly UiActionId BackMainAction = Action("back_main");
    private static readonly UiActionId CloseDialogAction = Action("close_dialog");
    private static readonly UiPathId HudHealthPath = Path("hud.health");
    private static readonly UiPathId HudWeaponPath = Path("hud.weapon");
    private static readonly UiPathId HudAmmoPath = Path("hud.ammo");
    private static readonly UiPathId HudCooldownPath = Path("hud.cooldown");
    private static readonly UiPathId HudHeatPath = Path("hud.heat");
    private static readonly UiPathId HudCrystalsPath = Path("hud.crystals");
    private static readonly UiPathId HudTimePath = Path("hud.time");
    private static readonly UiPathId HudHazardPath = Path("hud.hazard");
    private static readonly UiPathId HudScorePath = Path("hud.score");

    private IGameUiService? _ui;
    private PlayerHealth? _health;
    private WeaponController? _weapons;
    private MissionDirector? _mission;
    private RisingHazardDirector? _hazard;
    private bool _subscribed;

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
        StartForService(Context.GameUi);
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        PublishHudState();
    }

    internal void StartForService(IGameUiService ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
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
            CloseModal();
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
        SetHudValue(HudCrystalsPath, 0.0);
        SetHudValue(HudTimePath, 1.0);
        SetHudValue(HudHazardPath, 0.0);
        SetHudValue(HudScorePath, 0.0);
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
        PublishMission();
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

        if (_mission is null && Entity.TryGetComponent(out MissionDirector mission))
        {
            _mission = mission;
        }

        if (_hazard is null && Entity.TryGetComponent(out RisingHazardDirector hazard))
        {
            _hazard = hazard;
        }

        if (_mission is not null && _hazard is not null)
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
                if (_mission is null && behaviour is MissionDirector sceneMission)
                {
                    _mission = sceneMission;
                }
                else if (_hazard is null && behaviour is RisingHazardDirector sceneHazard)
                {
                    _hazard = sceneHazard;
                }

                if (_mission is not null && _hazard is not null)
                {
                    return;
                }
            }
        }
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
            return;
        }

        int selected = Math.Clamp(_weapons.SelectedIndex, 0, catalog.Weapons.Length - 1);
        WeaponDefinition weapon = catalog.Weapons[selected];
        SetHudValue(HudWeaponPath, catalog.Weapons.Length <= 1 ? 0.0 : Ratio(selected, catalog.Weapons.Length - 1));
        SetHudValue(HudAmmoPath, weapon.AmmoMax <= 0 ? 0.0 : Ratio(_weapons.CurrentAmmo, weapon.AmmoMax));
        SetHudValue(HudCooldownPath, weapon.CooldownSeconds <= 0f ? 1.0 : 1.0 - Ratio(_weapons.CooldownRemaining, weapon.CooldownSeconds));
        SetHudValue(HudHeatPath, Ratio(_weapons.Heat, 100f));
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
    }

    private void SetHudValue(UiPathId path, double value)
    {
        _ui!.SetValue(HudScreenHandle, path, new UiValue(Clamp01(value)));
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

    private void OpenModal(string screenId)
    {
        if (_ui is null)
        {
            return;
        }

        CloseModal();
        ModalScreen = _ui.PushModal(screenId);
    }

    private void CloseModal()
    {
        if (_ui is null || ModalScreen.Value == 0)
        {
            return;
        }

        _ui.HideScreen(ModalScreen);
        ModalScreen = default;
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
