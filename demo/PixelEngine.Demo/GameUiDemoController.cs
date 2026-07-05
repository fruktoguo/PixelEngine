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
    private static readonly UiPathId HudHeatPath = Path("hud.heat");

    private IGameUiService? _ui;
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

        _ui.SetValue(HudScreenHandle, HudHealthPath, new UiValue(1.0));
        _ui.SetValue(HudScreenHandle, HudHeatPath, new UiValue(0.0));
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
