using System.Xml.Linq;
using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.UI;
using ScriptScene = PixelEngine.Scripting.Scene;
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

        PixelEngine.UI.UiScreenHandle main = host.ShowScreen(manifest.GetRequiredScreen("main-menu").ScreenId, manifest.ResolveDocumentSource("main-menu"));
        PixelEngine.UI.UiScreenHandle hud = host.ShowScreen(manifest.GetRequiredScreen("hud").ScreenId, manifest.ResolveDocumentSource("hud"));
        PixelEngine.UI.UiScreenHandle settings = host.PushModal(manifest.GetRequiredScreen("settings").ScreenId, manifest.ResolveDocumentSource("settings"));
        PixelEngine.UI.UiScreenHandle inventory = host.PushModal(manifest.GetRequiredScreen("inventory").ScreenId, manifest.ResolveDocumentSource("inventory"));
        PixelEngine.UI.UiScreenHandle dialog = host.PushModal(manifest.GetRequiredScreen("dialog").ScreenId, manifest.ResolveDocumentSource("dialog"));
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
        AssertHudPathWritten(ui, "hud.health");
        AssertHudPathWritten(ui, "hud.weapon");
        AssertHudPathWritten(ui, "hud.ammo");
        AssertHudPathWritten(ui, "hud.cooldown");
        AssertHudPathWritten(ui, "hud.heat");
        AssertHudPathWritten(ui, "hud.crystals");
        AssertHudPathWritten(ui, "hud.time");
        AssertHudPathWritten(ui, "hud.hazard");
        AssertHudPathWritten(ui, "hud.score");
    }

    /// <summary>
    /// 验证 Demo Web-first HUD 每 tick 经公开脚本 UI 服务同步真实生命、武器、任务与危险状态。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesRealHudStateThroughScriptServiceEachTick()
    {
        string contentRoot = CreateTemporaryWeaponContent(
            """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" },
                { "id": "laser", "displayName": "Laser", "kind": "laser", "radius": 1, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 7, "heatPerCell": 1, "beamDps": 1, "muzzleCue": "ui_click", "impactCue": "sizzle_lava_water", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out ScriptInputApi input);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            PlayerHealth health = entity.AddComponent<PlayerHealth>();
            _ = entity.AddComponent<WeaponController>();
            MissionDirector mission = entity.AddComponent<MissionDirector>();
            mission.RequiredCrystals = 2;
            mission.TimeLimitSeconds = 30f;
            mission.InitialLavaSurfaceY = 100f;
            mission.LavaRiseCellsPerSecond = 0f;
            RisingHazardDirector hazard = entity.AddComponent<RisingHazardDirector>();
            hazard.StartSurfaceY = 100f;
            hazard.TargetSurfaceY = 70f;
            hazard.RiseSeconds = 1f;
            hazard.LossSurfaceY = 0f;
            hazard.EmitterCount = 1;
            hazard.FillIntervalSeconds = 10f;
            _ = entity.AddComponent<GameUiDemoController>();

            engine.RunHeadlessTicks(1);

            Assert.Equal(1.0, GetHudValue(ui, "hud.health"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.weapon"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.ammo"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.cooldown"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.heat"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.InRange(GetHudValue(ui, "hud.time"), 0.0, 1.0);
            Assert.InRange(GetHudValue(ui, "hud.hazard"), 0.0, 1.0);
            Assert.True(GetHudValue(ui, "hud.score") > 0.0);

            health.MaxHealth = 200f;
            input.Update([Key.Digit2], [], mouseX: 0f, mouseY: 0f, wheelY: 0f);
            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(20, 20, 1, 1)));
            engine.RunHeadlessTicks(1);

            Assert.Equal(0.5, GetHudValue(ui, "hud.health"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.weapon"), precision: 3);
            Assert.Equal(0.5, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.True(GetHudValue(ui, "hud.hazard") > 0.0);
            AssertHudPathWritten(ui, "hud.score");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
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
        foreach (string path in HudPaths())
        {
            backend.SetModelValue(stack[hudIndex].Document, new PixelEngine.UI.UiPathId(UiStableId.Hash(path)), new PixelEngine.UI.UiValue(path == "hud.cooldown" ? 1.0 : 0.25));
        }

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

    private static void AssertHudPathWritten(FakeGameUiService ui, string path)
    {
        Assert.Contains(GameUiDemoController.Path(path), ui.WrittenPaths);
        Assert.True(ui.Values.ContainsKey(GameUiDemoController.Path(path)), $"HUD path 未写入值：{path}");
    }

    private static double GetHudValue(FakeGameUiService ui, string path)
    {
        Assert.True(ui.Values.TryGetValue(GameUiDemoController.Path(path), out ScriptUiValue value), $"HUD path 未写入值：{path}");
        Assert.Equal(PixelEngine.Scripting.UiValueKind.Double, value.Kind);
        return value.AsDouble();
    }

    private static string[] HudPaths()
    {
        return [
            "hud.health",
            "hud.weapon",
            "hud.ammo",
            "hud.cooldown",
            "hud.heat",
            "hud.crystals",
            "hud.time",
            "hud.hazard",
            "hud.score",
        ];
    }

    private static Engine CreateHudEngine(string contentRoot, out ScriptScene scene, out FakeGameUiService ui, out ScriptInputApi input)
    {
        Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .WithContentRoot(contentRoot)
            .Build();
        MaterialTable materials = Materials(
            ("empty", CellType.Empty),
            ("sand", CellType.Powder),
            ("stone", CellType.Solid),
            ("lava", CellType.Liquid),
            ("fire", CellType.Fire),
            ("acid", CellType.Liquid),
            ("ash", CellType.Powder),
            ("crystal", CellType.Solid));
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 96, worldHeightCells: 96, particleCapacity: 64);
        scene = new ScriptScene();
        engine.Context.RegisterService(scene);
        input = new ScriptInputApi();
        ScriptCameraApi camera = new(viewportWidth: 40, viewportHeight: 20, centerX: 20, centerY: 10, zoom: 1);
        ui = new FakeGameUiService();
        engine.Context.RegisterService<IInputApi>(EngineServiceRole.Input, input);
        engine.Context.RegisterService(input);
        engine.Context.RegisterService<ICameraApi>(EngineServiceRole.Camera, camera);
        engine.Context.RegisterService(camera);
        engine.Context.RegisterService<IAudioApi>(EngineServiceRole.AudioService, NoopAudioApi.Instance);
        engine.Context.RegisterService<ScriptGameUiService>(ui);
        _ = engine.AttachScriptingFromServices();
        return engine;
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
                Integrity = definitions[i].Type == CellType.Solid ? (ushort)40 : (ushort)0,
                DestroyedTarget = definitions[i].Type == CellType.Solid ? (ushort)1 : (ushort)0,
                MineYield = definitions[i].Name == "crystal" ? (byte)1 : (byte)0,
            };
        }

        return new MaterialTable(materials);
    }

    private static string CreateTemporaryWeaponContent(string weaponsJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "pixelengine-demo-ui-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "weapons.json"), weaponsJson);
        return directory;
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

        public Dictionary<ScriptUiPathId, ScriptUiValue> Values { get; } = [];

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
            WrittenPaths.Add(path);
            Values[path] = value;
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

    private sealed class NoopAudioApi : IAudioApi
    {
        public static NoopAudioApi Instance { get; } = new();

        public void PlayOneShot(string cue, float volume = 1f)
        {
            _ = cue;
            _ = volume;
        }

        public void PlayAt(string cue, float x, float y, float volume = 1f)
        {
            _ = cue;
            _ = x;
            _ = y;
            _ = volume;
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

        public ManagedFallbackImage LoadImage(string path)
        {
            Assert.True(File.Exists(path), path);
            return new ManagedFallbackImage(321, 1, 1);
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

        public void Image(string id, uint textureHandle, int textureWidth, int textureHeight, float width, float height, uint tintBgra = 0xFF_FF_FF_FF)
        {
            _ = id;
            _ = textureHandle;
            _ = textureWidth;
            _ = textureHeight;
            _ = width;
            _ = height;
            _ = tintBgra;
        }
    }
}
