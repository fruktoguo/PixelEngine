using System.Xml.Linq;
using PixelEngine.Gui;
using PixelEngine.Rendering;
using PixelEngine.UI;
using Xunit;
using ScriptGameUiService = PixelEngine.Scripting.IGameUiService;
using ScriptIUiModel = PixelEngine.Scripting.IUiModel;
using ScriptUiActionId = PixelEngine.Scripting.UiActionId;
using RuntimeUiEvent = PixelEngine.UI.UiEvent;
using ScriptUiEvent = PixelEngine.Scripting.UiEvent;
using ScriptUiModelName = PixelEngine.Scripting.UiModelName;
using ScriptUiPathId = PixelEngine.Scripting.UiPathId;
using ScriptUiScreenHandle = PixelEngine.Scripting.UiScreenHandle;
using ScriptUiValue = PixelEngine.Scripting.UiValue;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Demo 大 UI 内容资产与脚本导航测试。
/// </summary>
public sealed class DemoUiContentTests
{
    /// <summary>
    /// 验证 Demo UI manifest 声明五类可玩屏幕，且真实可见中文文本落在共享 CJK 字形范围内。
    /// </summary>
    [Fact]
    public void DemoUiManifestDeclaresFivePlayableScreensAndCjkTextIsCovered()
    {
        string uiRoot = DemoUiRoot();
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(uiRoot);

        Assert.Equal(5, manifest.ScreenCount);
        AssertScreen(manifest, "main-menu");
        AssertScreen(manifest, "settings");
        AssertScreen(manifest, "inventory");
        AssertScreen(manifest, "dialog");
        AssertScreen(manifest, "hud");

        string text = string.Concat(manifest.Screens.ToArray().Select(screen => ExtractUiText(screen.FullPath)));
        string glyphText = string.Concat(text.Where(char.IsLetterOrDigit));
        UiFontCoverageResult coverage = FontEngine.ScanCoverage(glyphText);

        Assert.True(manifest.AssetDirectories.HasFontsDirectory);
        Assert.False(coverage.HasMissingGlyphs, $"Demo UI 中文文本不应缺字，missing={coverage.MissingCodePoints}。");
    }

    /// <summary>
    /// 验证五类 Demo UI 屏幕可由 ManagedFallback 同时显示、叠放并产生按钮/复选框事件。
    /// </summary>
    [Fact]
    public void DemoUiScreensRenderAndInteractThroughManagedFallback()
    {
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 720, 480, 1f), UiBackendKind.ManagedFallback));

        UiScreenHandle main = host.ShowScreen(manifest.GetRequiredScreen("main-menu").ScreenId, manifest.ResolveDocumentSource("main-menu"));
        UiScreenHandle hud = host.ShowScreen(manifest.GetRequiredScreen("hud").ScreenId, manifest.ResolveDocumentSource("hud"));
        UiScreenHandle settings = host.PushModal(manifest.GetRequiredScreen("settings").ScreenId, manifest.ResolveDocumentSource("settings"));
        UiScreenHandle inventory = host.PushModal(manifest.GetRequiredScreen("inventory").ScreenId, manifest.ResolveDocumentSource("inventory"));
        UiScreenHandle dialog = host.PushModal(manifest.GetRequiredScreen("dialog").ScreenId, manifest.ResolveDocumentSource("dialog"));
        _ = main;
        _ = hud;
        _ = settings;
        _ = inventory;
        _ = dialog;
        _ = gui.Context.ClickedButtons.Add("设置");
        _ = gui.Context.ToggledCheckboxes.Add("垂直同步");

        host.Composite(default);
        RuntimeUiEvent[] events = new RuntimeUiEvent[8];
        int eventCount = host.DrainEvents(events);

        Assert.Contains("PixelEngine 熔岩矿洞", gui.Context.Texts);
        Assert.Contains("设置", gui.Context.Texts);
        Assert.Contains("背包", gui.Context.Texts);
        Assert.Contains("矿工通讯", gui.Context.Texts);
        Assert.Contains("HUD", gui.Context.Texts);
        Assert.Contains("开始游戏", gui.Context.Buttons);
        Assert.Contains("返回", gui.Context.Buttons);
        Assert.True(eventCount >= 2);
        Assert.Contains(events[..eventCount], e => e.Action == new PixelEngine.UI.UiActionId(UiStableId.Hash("open_settings")));
        Assert.Contains(events[..eventCount], e => e.Action == new PixelEngine.UI.UiActionId(UiStableId.Hash("toggle_vsync")));
        Assert.True(host.Documents.HasModalTop);
        Assert.True(host.PopModal());
        Assert.Equal(4, host.Documents.StackCount);
    }

    /// <summary>
    /// 验证 Demo UI 控制器使用脚本 UI 服务打开主菜单/HUD、切换模态页并返回。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerOpensScreensAndClosesModalsThroughScriptService()
    {
        GameUiDemoController controller = new();
        FakeGameUiService ui = new();

        controller.StartForService(ui);
        ui.Raise(GameUiDemoController.Action("open_settings"));
        ScriptUiScreenHandle settings = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("open_inventory"));
        ScriptUiScreenHandle inventory = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("open_dialog"));
        ScriptUiScreenHandle dialog = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("close_dialog"));
        ui.Raise(GameUiDemoController.Action("start_game"));

        Assert.Equal([GameUiDemoController.MainMenuScreen, GameUiDemoController.HudScreen], ui.ShownScreens);
        Assert.Equal([GameUiDemoController.SettingsScreen, GameUiDemoController.InventoryScreen, GameUiDemoController.DialogScreen], ui.PushedScreens);
        Assert.NotEqual(default, settings);
        Assert.NotEqual(default, inventory);
        Assert.NotEqual(default, dialog);
        Assert.Equal(default, controller.ModalScreen);
        Assert.Equal(default, controller.MainScreen);
        Assert.Contains(GameUiDemoController.Path("hud.health"), ui.WrittenPaths);
        Assert.Contains(GameUiDemoController.Path("hud.heat"), ui.WrittenPaths);
    }

    /// <summary>
    /// 验证禁用游戏大 UI 时，Demo 控制器落到脚本 Noop 服务且不需要改 Demo 逻辑。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerIsSafeWhenGameUiServiceIsNoop()
    {
        GameUiDemoController controller = new();

        controller.StartForService(PixelEngine.Scripting.NoopGameUiService.Instance);
        controller.HandleUiEvent(new ScriptUiEvent(default, default, GameUiDemoController.Action("open_settings"), default));

        Assert.Equal(default, controller.MainScreen);
        Assert.Equal(default, controller.HudScreenHandle);
        Assert.Equal(default, controller.ModalScreen);
        Assert.Equal(GameUiDemoController.Action("open_settings"), controller.LastAction);
    }

    /// <summary>
    /// 验证 GL smoke 开启时，同一批 Demo UI 屏幕能被 RmlUi 后端载入、绑定模型并合成。
    /// </summary>
    [Fact]
    public void DemoUiScreensLoadThroughRmlUiBackendWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine Demo UI RmlUi smoke",
            Width = 720,
            Height = 480,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        using RmlUiBackend backend = new(window);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, window.Width, window.Height, 1f), UiBackendKind.RmlUi));

        Span<UiScreenStackEntry> stack = stackalloc UiScreenStackEntry[5];
        int index = 0;
        int hudIndex = -1;
        int mainIndex = -1;
        foreach (UiManifestScreen screen in manifest.Screens)
        {
            UiDocumentHandle document = backend.LoadDocument(screen.ToDocumentSource());
            stack[index] = new UiScreenStackEntry(new PixelEngine.UI.UiScreenHandle(index + 1), screen.ScreenId, document, Modal: screen.Id is "settings" or "inventory" or "dialog");
            if (screen.Id == "main-menu")
            {
                mainIndex = index;
            }

            if (screen.Id == "hud")
            {
                hudIndex = index;
            }

            index++;
        }

        Assert.True(mainIndex >= 0);
        Assert.True(hudIndex >= 0);
        backend.SetScreenStack(stack.Slice(mainIndex, 1));
        backend.Update(1f / 60f);
        backend.FeedPointerMove(48f, 118f);
        backend.FeedPointerButton(UiPointerButton.Left, isDown: true);
        backend.FeedPointerButton(UiPointerButton.Left, isDown: false);
        backend.Update(1f / 60f);
        Span<RuntimeUiEvent> events = stackalloc RuntimeUiEvent[4];
        int eventCount = backend.DrainEvents(events);
        Assert.Contains(events[..eventCount].ToArray(), e => e.Action == new PixelEngine.UI.UiActionId(UiStableId.Hash("open_settings")));

        backend.SetScreenStack(stack);
        backend.Update(1f / 60f);
        backend.SetModelValue(stack[hudIndex].Document, new PixelEngine.UI.UiPathId(UiStableId.Hash("hud.health")), new PixelEngine.UI.UiValue(0.75));
        backend.SetModelValue(stack[hudIndex].Document, new PixelEngine.UI.UiPathId(UiStableId.Hash("hud.heat")), new PixelEngine.UI.UiValue(0.25));
        Assert.True(backend.InvokeAction(stack[0].Document, new PixelEngine.UI.UiActionId(UiStableId.Hash("open_settings")), PixelEngine.UI.UiValue.FromBoolean(true)));
        backend.Composite(default);
        window.SwapBuffers();
    }

    private static void AssertScreen(UiManifest manifest, string id)
    {
        Assert.True(manifest.TryGetScreen(id, out UiManifestScreen screen), $"缺少 UI screen: {id}");
        Assert.True(File.Exists(screen.FullPath), screen.FullPath);
        Assert.Equal(new UiScreenId(UiStableId.Hash(id)), screen.ScreenId);
    }

    private static string ExtractUiText(string path)
    {
        XDocument document = XDocument.Load(path);
        return string.Concat(document.Descendants().Select(ExtractVisibleElementText));
    }

    private static string ExtractVisibleElementText(XElement element)
    {
        string name = element.Name.LocalName;
        if (name is "style" or "head" or "body")
        {
            return string.Empty;
        }

        string attributes = string.Concat(
            element.Attributes()
                .Where(attribute => attribute.Name.LocalName is "title" or "text" or "label")
                .Select(attribute => attribute.Value));
        string value = !element.HasElements && name is "p" or "span" or "text" or "button"
            ? element.Value
            : string.Empty;
        return attributes + value;
    }

    private static string DemoUiRoot()
    {
        return Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content", "ui");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }

    private sealed class FakeGameUiService : ScriptGameUiService
    {
        private int _nextHandle = 1;

        public event Action<ScriptUiEvent>? UiEventRaised;

        public List<string> ShownScreens { get; } = [];

        public List<string> PushedScreens { get; } = [];

        public List<ScriptUiPathId> WrittenPaths { get; } = [];

        public ScriptUiScreenHandle ShowScreen(string screenId)
        {
            ShownScreens.Add(screenId);
            return new ScriptUiScreenHandle(_nextHandle++);
        }

        public void HideScreen(ScriptUiScreenHandle screen)
        {
            _ = screen;
        }

        public ScriptUiScreenHandle PushModal(string screenId)
        {
            PushedScreens.Add(screenId);
            return new ScriptUiScreenHandle(_nextHandle++);
        }

        public void BindModel(ScriptUiScreenHandle screen, ScriptUiModelName modelName, ScriptIUiModel model)
        {
            _ = screen;
            _ = modelName;
            _ = model;
        }

        public void SetValue(ScriptUiScreenHandle screen, ScriptUiPathId path, in ScriptUiValue value)
        {
            _ = screen;
            _ = value;
            WrittenPaths.Add(path);
        }

        public bool TryGetValue(ScriptUiScreenHandle screen, ScriptUiPathId path, out ScriptUiValue value)
        {
            _ = screen;
            _ = path;
            value = default;
            return false;
        }

        public void Invoke(ScriptUiScreenHandle screen, ScriptUiActionId action, in ScriptUiValue payload)
        {
            _ = screen;
            _ = action;
            _ = payload;
        }

        public void Raise(ScriptUiActionId action)
        {
            UiEventRaised?.Invoke(new ScriptUiEvent(default, default, action, default));
        }
    }

    private sealed class FakeGuiHost : IManagedFallbackGuiHost
    {
        public FakeGuiDrawContext Context { get; } = new();

        public bool IsRunning { get; private set; }

        public void Initialize()
        {
            IsRunning = true;
        }

        public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
        {
            _ = deltaSeconds;
            _ = width;
            _ = height;
            drawGui(Context);
        }
    }

    private sealed class FakeGuiDrawContext : IGuiDrawContext
    {
        public List<string> Texts { get; } = [];

        public List<string> Buttons { get; } = [];

        public HashSet<string> ClickedButtons { get; } = [];

        public HashSet<string> ToggledCheckboxes { get; } = [];

        public int Width => 720;

        public int Height => 480;

        public float DeltaTime => 1f / 60f;

        public bool WantsMouse => false;

        public bool WantsKeyboard => false;

        public void SetNextWindow(float x, float y, float width, float height, GuiDrawCondition condition = GuiDrawCondition.Always)
        {
            _ = x;
            _ = y;
            _ = width;
            _ = height;
            _ = condition;
        }

        public bool BeginWindow(string id, string title, GuiDrawWindowFlags flags = GuiDrawWindowFlags.None)
        {
            _ = id;
            _ = flags;
            Texts.Add(title);
            return true;
        }

        public void EndWindow()
        {
        }

        public void Text(string text)
        {
            Texts.Add(text);
        }

        public void TextColored(string text, uint colorBgra)
        {
            _ = colorBgra;
            Texts.Add(text);
        }

        public void SameLine()
        {
        }

        public void Separator()
        {
        }

        public bool Button(string label)
        {
            Buttons.Add(label);
            return ClickedButtons.Remove(label);
        }

        public bool Checkbox(string label, ref bool value)
        {
            if (!ToggledCheckboxes.Remove(label))
            {
                return false;
            }

            value = !value;
            return true;
        }

        public void ProgressBar(float value01, string? label = null)
        {
            Texts.Add(label ?? value01.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }

        public void ColorSwatch(string id, uint colorBgra, float size = 16f)
        {
            _ = id;
            _ = colorBgra;
            _ = size;
        }
    }
}
