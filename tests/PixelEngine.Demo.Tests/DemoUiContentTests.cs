using System.Xml.Linq;
using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Testing;
using PixelEngine.UI;
using ScriptScene = PixelEngine.Scripting.Scene;
using Xunit;
using ScriptGameUiService = PixelEngine.Scripting.IGameUiService;
using ScriptIUiModel = PixelEngine.Scripting.IUiModel;
using ScriptUiActionId = PixelEngine.Scripting.UiActionId;
using ScriptUiCanvasHandle = PixelEngine.Scripting.UiCanvasHandle;
using RuntimeUiEvent = PixelEngine.UI.UiEvent;
using ScriptUiEvent = PixelEngine.Scripting.UiEvent;
using ScriptUiModelName = PixelEngine.Scripting.UiModelName;
using ScriptUiPathId = PixelEngine.Scripting.UiPathId;
using ScriptUiScreenHandle = PixelEngine.Scripting.UiScreenHandle;
using ScriptUiStringHandle = PixelEngine.Scripting.UiStringHandle;
using ScriptUiValue = PixelEngine.Scripting.UiValue;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Demo UI 内容与 RmlUi 文档预处理契约测试。
/// 不变式：manifest 资源可解析、样式与文档路径与 content root 对齐、缺资源时行为可预期。
/// </summary>
public sealed class DemoUiContentTests
{
    /// <summary>
    /// 验证 Demo UI manifest 声明十类可玩/缩放 dogfood 屏幕，且真实可见中文文本落在共享 CJK 字形范围内。
    /// </summary>
    [Fact]
    public void DemoUiManifestDeclaresTenPlayableAndScalerDogfoodScreensWithCjkCoverage()
    {
        // Arrange：准备输入与初始状态
        string uiRoot = DemoUiRoot();
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(uiRoot);

        // Assert：验证预期结果
        Assert.Equal(10, manifest.ScreenCount);
        AssertScreen(manifest, "main-menu");
        AssertScreen(manifest, "settings");
        AssertScreen(manifest, "inventory");
        AssertScreen(manifest, "dialog");
        AssertScreen(manifest, "hud");
        AssertScreen(manifest, "telemetry");
        AssertScreen(manifest, "pixel-overlay");
        AssertScreen(manifest, "physical-overlay");
        AssertScreen(manifest, "pause");
        AssertScreen(manifest, "result");

        string text = string.Concat(manifest.Screens.ToArray().Select(screen => ExtractUiText(screen.FullPath)));
        string glyphText = string.Concat(text.Where(char.IsLetterOrDigit));
        UiFontCoverageResult coverage = FontEngine.ScanCoverage(glyphText);

        Assert.True(manifest.AssetDirectories.HasFontsDirectory);
        Assert.False(coverage.HasMissingGlyphs, $"Demo UI 中文文本不应缺字，missing={coverage.MissingCodePoints}。");
    }

    /// <summary>
    /// 验证默认 Demo 场景真实 dogfood .scene v3、多 Canvas 与三种 CanvasScaler，而不是依赖旧 implicit Canvas。
    /// </summary>
    [Fact]
    public void DemoLavaMineSceneUsesExplicitMultiCanvasAndAllScalerModes()
    {
        string scenePath = Path.Combine(
            FindRepositoryRoot(),
            "demo",
            "PixelEngine.Demo",
            "content",
            "scenes",
            "lava-mine.scene");

        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);
        EngineSceneCanvasSet resolved = EngineSceneCanvasResolver.Resolve(document);
        EngineSceneCanvasDefinition[] canvases = resolved.Canvases.ToArray();

        Assert.Equal(EngineSceneDocumentLoader.CurrentFormatVersion, document.FormatVersion);
        Assert.True(resolved.HasExplicitCanvases);
        Assert.Equal(3, canvases.Length);
        Assert.Equal(GameUiCanvasIdentity.FromStableId(4), resolved.PrimaryId);
        Assert.Equal([0, 100, 200], canvases.Select(static canvas => canvas.SortingOrder));
        Assert.Equal(
            [UiScaleMode.ScaleWithScreenSize, UiScaleMode.ConstantPixelSize, UiScaleMode.ConstantPhysicalSize],
            canvases.Select(static canvas => canvas.ScalerSettings.ScaleMode));
        Assert.Null(canvases[1].InitialScreenId);
        Assert.Null(canvases[2].InitialScreenId);
        Assert.True(resolved.Diagnostics.IsEmpty);
    }

    /// <summary>
    /// 验证 Demo 十类 Web-first 屏幕显式声明 headless / ManagedFallback 可消费的屏幕数据契约。
    /// </summary>
    [Fact]
    public void DemoUiScreensDeclareHeadlessManagedFallbackContracts()
    {
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());

        AssertScreenContract(
            manifest,
            GameUiDemoController.MainMenuScreen,
            "demo.webfirst.main-menu/v2",
            GameUiDemoController.MenuModelPathNames.ToArray(),
            ["open_dialog", "open_inventory", "open_settings", "select_campaign", "select_sandbox", "start_game"]);
        AssertScreenContract(
            manifest,
            GameUiDemoController.SettingsScreen,
            "demo.webfirst.settings/v1",
            ["settings.audio", "settings.vsync"],
            ["back_main", "toggle_audio", "toggle_vsync"]);
        AssertScreenContract(
            manifest,
            GameUiDemoController.InventoryScreen,
            "demo.webfirst.inventory/v1",
            [],
            ["back_main"]);
        AssertScreenContract(
            manifest,
            GameUiDemoController.DialogScreen,
            "demo.webfirst.dialog/v1",
            [],
            ["close_dialog", "dialog_next"]);
        AssertScreenContract(
            manifest,
            GameUiDemoController.HudScreen,
            "demo.webfirst.hud/v4",
            GameUiDemoController.HudModelPathNames.ToArray(),
            ["pause_game", "toggle_telemetry"]);
        AssertScreenContract(
            manifest,
            GameUiDemoController.TelemetryScreen,
            "demo.webfirst.telemetry/v1",
            GameUiDemoController.TelemetryModelPathNames.ToArray(),
            ["toggle_telemetry"]);
        AssertScreenContract(
            manifest,
            GameUiDemoController.PixelOverlayScreen,
            "demo.webfirst.pixel-overlay/v1",
            [],
            []);
        AssertScreenContract(
            manifest,
            GameUiDemoController.PhysicalOverlayScreen,
            "demo.webfirst.physical-overlay/v1",
            [],
            []);
        AssertScreenContract(
            manifest,
            GameUiDemoController.PauseScreen,
            "demo.webfirst.pause/v1",
            [],
            ["open_settings", "restart_game", "resume_game", "quit_game"]);
        AssertScreenContract(
            manifest,
            GameUiDemoController.ResultScreen,
            "demo.webfirst.result/v2",
            GameUiDemoController.ResultModelPathNames.ToArray(),
            ["restart_game", "quit_game"]);
    }

    /// <summary>
    /// 验证 Demo HUD 与结算屏声明的数据路径能被真实 ManagedFallback 后端枚举、写入并读回。
    /// </summary>
    [Fact]
    public void DemoManagedFallbackCopiesHudAndResultModelPathsFromContent()
    {
        // Arrange：准备输入与初始状态
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 720, 480, 1f), UiBackendKind.ManagedFallback));

        UI.UiScreenHandle hud = host.ShowScreen(
            manifest.GetRequiredScreen(GameUiDemoController.HudScreen).ScreenId,
            manifest.ResolveDocumentSource(GameUiDemoController.HudScreen));
        UI.UiScreenHandle telemetry = host.ShowScreen(
            manifest.GetRequiredScreen(GameUiDemoController.TelemetryScreen).ScreenId,
            manifest.ResolveDocumentSource(GameUiDemoController.TelemetryScreen));
        UI.UiScreenHandle result = host.PushModal(
            manifest.GetRequiredScreen(GameUiDemoController.ResultScreen).ScreenId,
            manifest.ResolveDocumentSource(GameUiDemoController.ResultScreen));

        string[] hudPaths = GameUiDemoController.HudModelPathNames.ToArray();
        string[] telemetryPaths = GameUiDemoController.TelemetryModelPathNames.ToArray();
        string[] resultPaths = GameUiDemoController.ResultModelPathNames.ToArray();
        AssertManagedModelPaths(host, hud, hudPaths);
        AssertManagedModelPaths(host, telemetry, telemetryPaths);
        AssertManagedModelPaths(host, result, resultPaths);
        foreach (string path in hudPaths)
        {
            AssertManagedDoubleValueRoundTrips(host, hud, path, 0.42);
        }

        foreach (string path in telemetryPaths)
        {
            AssertManagedDoubleValueRoundTrips(host, telemetry, path, 0.63);
        }

        foreach (string path in resultPaths)
        {
            AssertManagedDoubleValueRoundTrips(host, result, path, 0.84);
        }

        host.Composite(default);

        // Assert：验证预期结果
        Assert.Contains("生命", gui.Context.Texts);
        Assert.Contains("场景状态", gui.Context.Texts);
    }

    /// <summary>
    /// 验证默认 Web-first 产品文案同时声明纵深 Campaign 与无终点 InfiniteSandbox。
    /// </summary>
    [Fact]
    public void DemoDefaultLoopTextDescribesCampaignAndInfiniteSandboxModes()
    {
        // Arrange：准备输入与初始状态
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        string[] hudText = UiVisibleText(manifest.GetRequiredScreen(GameUiDemoController.HudScreen).FullPath);
        string[] resultText = UiVisibleText(manifest.GetRequiredScreen(GameUiDemoController.ResultScreen).FullPath);
        string[] defaultLoopText = [.. hudText, .. resultText];

        string[] mainText = UiVisibleText(manifest.GetRequiredScreen(GameUiDemoController.MainMenuScreen).FullPath);
        defaultLoopText = [.. mainText, .. defaultLoopText];

        // Assert：验证预期结果
        Assert.Contains("战役", defaultLoopText);
        Assert.Contains("无限沙盒", defaultLoopText);
        Assert.Contains("八个区域 · 七座静界锻台", defaultLoopText);
        Assert.Contains("战役深入源核，沙盒无限延伸", defaultLoopText);
        Assert.Contains("每一轮由独立世界 Seed 生成", defaultLoopText);
        Assert.Contains("战役 / Campaign", defaultLoopText);
        Assert.Contains("探索 / Exploring", defaultLoopText);
        Assert.Contains("Seed", defaultLoopText);
        Assert.Contains("深度 cell", defaultLoopText);
        Assert.Contains("本轮结束 / Run Ended", defaultLoopText);
        Assert.Contains("永久死亡 / Run ended", defaultLoopText);
        Assert.Contains("开始新轮", defaultLoopText);
        Assert.DoesNotContain(defaultLoopText, text => text.Contains("右侧出口", StringComparison.Ordinal));
        Assert.DoesNotContain("可选任务时间", defaultLoopText);
        Assert.DoesNotContain("上涨熔岩压力", defaultLoopText);
        Assert.DoesNotContain("水晶", defaultLoopText);
        Assert.DoesNotContain("水位", defaultLoopText);
    }

    /// <summary>
    /// 验证正常产品态使用独立菜单简报与 HUD 信息区，缩放标尺只存在于按需诊断屏而不混入主循环。
    /// </summary>
    [Fact]
    public void DemoMenuAndHudUseSeparateProductLayoutRegionsWithoutScalerCalibrationCopy()
    {
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        string mainPath = manifest.GetRequiredScreen(GameUiDemoController.MainMenuScreen).FullPath;
        string hudPath = manifest.GetRequiredScreen(GameUiDemoController.HudScreen).FullPath;
        XDocument main = XDocument.Load(mainPath);
        XDocument hud = XDocument.Load(hudPath);

        _ = Assert.Single(main.Descendants(), element => (string?)element.Attribute("id") == "menu_scrim");
        _ = Assert.Single(main.Descendants(), element => (string?)element.Attribute("id") == "briefing");
        XElement start = Assert.Single(main.Descendants(), element => (string?)element.Attribute("id") == "start_game");
        Assert.Equal("start_game", (string?)start.Attribute("data-event-click"));

        _ = Assert.Single(hud.Descendants(), element => (string?)element.Attribute("id") == "status_panel");
        _ = Assert.Single(hud.Descendants(), element => (string?)element.Attribute("id") == "objective_panel");
        _ = Assert.Single(hud.Descendants(), element => (string?)element.Attribute("id") == "hud_context");
        XElement telemetry = Assert.Single(hud.Descendants(), element => (string?)element.Attribute("id") == "hud_telemetry");
        Assert.Equal("toggle_telemetry", (string?)telemetry.Attribute("data-event-click"));

        string normalLoopText = string.Concat(UiVisibleText(mainPath)) + string.Concat(UiVisibleText(hudPath));
        Assert.DoesNotContain("Constant Pixel Size", normalLoopText, StringComparison.Ordinal);
        Assert.DoesNotContain("Constant Physical Size", normalLoopText, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证背包与对话屏幕绑定默认六武器目录和无限沙盒提示，不退回固定出口路线。
    /// </summary>
    [Fact]
    public void DemoInventoryAndDialogTextMatchesDefaultWeaponCatalogAndSandbox()
    {
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        WeaponCatalog catalog = LoadDefaultWeaponCatalog();
        string[] inventoryText = UiVisibleText(manifest.GetRequiredScreen(GameUiDemoController.InventoryScreen).FullPath);
        string[] dialogText = UiVisibleText(manifest.GetRequiredScreen(GameUiDemoController.DialogScreen).FullPath);

        Assert.Equal(6, catalog.Weapons.Length);
        for (int i = 0; i < catalog.Weapons.Length; i++)
        {
            string expectedPrefix = $"{i + 1} {catalog.Weapons[i].DisplayName}";
            Assert.Contains(inventoryText, text => text.StartsWith(expectedPrefix, StringComparison.Ordinal));
        }

        Assert.Contains(inventoryText, text => text.Contains("开路与触发坍塌", StringComparison.Ordinal));
        Assert.Contains(inventoryText, text => text.Contains("改变局部地形", StringComparison.Ordinal));
        Assert.Contains("山脊、湖盆和洞穴由同一个世界 seed 延展。", dialogText);
        Assert.Contains("走过的地形会保留挖掘、爆破与建造结果。", dialogText);
        Assert.DoesNotContain(dialogText, text => text.Contains("出口", StringComparison.Ordinal));
        Assert.DoesNotContain("手枪", inventoryText);
        Assert.DoesNotContain("激光炮", inventoryText);
        Assert.DoesNotContain("手榴弹", inventoryText);
        Assert.DoesNotContain("水位", dialogText);
        Assert.DoesNotContain("水晶", dialogText);
    }

    /// <summary>
    /// 验证设置屏幕说明真实运行态 AudioSystem / Present VSync 开关，不退回无语义占位面板。
    /// </summary>
    [Fact]
    public void DemoSettingsTextDescribesRuntimeAudioAndVSyncControls()
    {
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        UiManifestScreen settings = manifest.GetRequiredScreen(GameUiDemoController.SettingsScreen);
        string[] settingsText = UiVisibleText(settings.FullPath);

        Assert.Contains("音频总开关会改写运行时 AudioSystem。", settingsText);
        Assert.Contains("Present VSync 会改写当前窗口交换策略。", settingsText);
        Assert.Contains("Present VSync", settingsText);
        Assert.Contains("音频总开关", settingsText);
        XDocument document = XDocument.Load(settings.FullPath);
        string[] modelPaths = ExtractAttributeValues(document, "path", "data-model");
        Assert.Contains("settings.vsync", modelPaths);
        Assert.Contains("settings.audio", modelPaths);
        Assert.DoesNotContain("音效", settingsText);
        Assert.DoesNotContain("占位", settingsText);

        XElement settingsRoot = document.Root ?? throw new InvalidDataException("settings.xhtml 缺少根元素。");
        XDocument mainDocument = XDocument.Load(
            manifest.GetRequiredScreen(GameUiDemoController.MainMenuScreen).FullPath);
        XElement mainRoot = mainDocument.Root ?? throw new InvalidDataException("main-menu.xhtml 缺少根元素。");
        float settingsRight = ReadInlinePixelStyle(settingsRoot, "left") +
            ReadInlinePixelStyle(settingsRoot, "width");
        float mainLeft = ReadInlinePixelStyle(mainRoot, "left");
        Assert.Equal(32f, mainLeft - settingsRight);

        string styleSheet = string.Concat(document.Descendants("style").Select(static style => style.Value));
        string mainStyleSheet = string.Concat(mainDocument.Descendants("style").Select(static style => style.Value));
        Assert.Contains("#settings_title { position: absolute", styleSheet, StringComparison.Ordinal);
        Assert.Contains("#settings_audio_hint { top: 62px; }", styleSheet, StringComparison.Ordinal);
        Assert.Contains("#settings_vsync { top: 132px; }", styleSheet, StringComparison.Ordinal);
        Assert.Contains("#settings_audio { top: 178px; }", styleSheet, StringComparison.Ordinal);
        Assert.Contains(
            "#menu_kicker, #main_title, #main_hint { position: absolute",
            mainStyleSheet,
            StringComparison.Ordinal);
        Assert.Contains("#menu_kicker { top: 20px;", mainStyleSheet, StringComparison.Ordinal);
        Assert.Contains("#main_title { top: 42px;", mainStyleSheet, StringComparison.Ordinal);
        Assert.Contains("#main_hint { top: 84px;", mainStyleSheet, StringComparison.Ordinal);
        Assert.Contains("#briefing_title { position: absolute; left: 36px; top: 224px;", mainStyleSheet, StringComparison.Ordinal);
        Assert.Contains("#main_route { top: 248px; }", mainStyleSheet, StringComparison.Ordinal);
        Assert.Contains("#main_goal { top: 268px; }", mainStyleSheet, StringComparison.Ordinal);
        Assert.Contains("#main_hazard { top: 288px; }", mainStyleSheet, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证八类 Demo UI 屏幕可由 ManagedFallback 同时显示、叠放并产生按钮/复选框事件。
    /// </summary>
    [Fact]
    public void DemoUiScreensRenderAndInteractThroughManagedFallback()
    {
        // Arrange：准备输入与初始状态
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 720, 480, 1f), UiBackendKind.ManagedFallback));

        UI.UiScreenHandle main = host.ShowScreen(manifest.GetRequiredScreen("main-menu").ScreenId, manifest.ResolveDocumentSource("main-menu"));
        UI.UiScreenHandle hud = host.ShowScreen(manifest.GetRequiredScreen("hud").ScreenId, manifest.ResolveDocumentSource("hud"));
        UI.UiScreenHandle telemetry = host.ShowScreen(manifest.GetRequiredScreen("telemetry").ScreenId, manifest.ResolveDocumentSource("telemetry"));
        UI.UiScreenHandle settings = host.PushModal(manifest.GetRequiredScreen("settings").ScreenId, manifest.ResolveDocumentSource("settings"));
        UI.UiScreenHandle inventory = host.PushModal(manifest.GetRequiredScreen("inventory").ScreenId, manifest.ResolveDocumentSource("inventory"));
        UI.UiScreenHandle dialog = host.PushModal(manifest.GetRequiredScreen("dialog").ScreenId, manifest.ResolveDocumentSource("dialog"));
        UI.UiScreenHandle pause = host.PushModal(manifest.GetRequiredScreen("pause").ScreenId, manifest.ResolveDocumentSource("pause"));
        UI.UiScreenHandle result = host.PushModal(manifest.GetRequiredScreen("result").ScreenId, manifest.ResolveDocumentSource("result"));
        _ = main;
        _ = hud;
        _ = telemetry;
        _ = settings;
        _ = inventory;
        _ = dialog;
        _ = pause;
        _ = result;
        _ = gui.Context.ClickedButtons.Add("设置");
        _ = gui.Context.ToggledCheckboxes.Add("Present VSync");

        host.Composite(default);
        RuntimeUiEvent[] events = new RuntimeUiEvent[8];
        int eventCount = host.DrainEvents(events);

        // Assert：验证预期结果
        Assert.Contains("无限沙盒", gui.Context.Buttons);
        Assert.Contains("设置", gui.Context.Texts);
        Assert.Contains("背包", gui.Context.Texts);
        Assert.Contains("勘探记录", gui.Context.Texts);
        Assert.Contains("HUD", gui.Context.Texts);
        Assert.Contains("运行诊断", gui.Context.Texts);
        Assert.Contains("暂停", gui.Context.Texts);
        Assert.Contains("运行结算", gui.Context.Texts);
        Assert.Contains("开始", gui.Context.Buttons);
        Assert.Contains("继续", gui.Context.Buttons);
        Assert.Contains("开始新轮", gui.Context.Buttons);
        Assert.Contains("返回", gui.Context.Buttons);
        Assert.True(eventCount >= 2);
        Assert.Contains(events[..eventCount], e => e.Action == new UI.UiActionId(UiStableId.Hash("open_settings")));
        Assert.Contains(events[..eventCount], e => e.Action == new UI.UiActionId(UiStableId.Hash("toggle_vsync")));
        Assert.True(host.Documents.HasModalTop);
        Assert.True(host.PopModal());
        Assert.Equal(7, host.Documents.StackCount);
    }

    /// <summary>
    /// 验证真实 Demo 屏幕使用的 CSS margin shorthand 会进入 ManagedFallback 垂直布局子集。
    /// </summary>
    [Fact]
    public void DemoManagedFallbackConsumesProductScreenMarginShorthand()
    {
        // Arrange：准备输入与初始状态
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        UiManifestScreen dialog = manifest.GetRequiredScreen(GameUiDemoController.DialogScreen);
        string xhtml = File.ReadAllText(dialog.FullPath);
        // Assert：验证预期结果
        Assert.Contains("p { margin: 4px 0px; }", xhtml, StringComparison.Ordinal);

        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 720, 480, 1f), UiBackendKind.ManagedFallback));

        _ = host.ShowScreen(dialog.ScreenId, manifest.ResolveDocumentSource(GameUiDemoController.DialogScreen));
        host.Composite(default);

        Assert.Contains("勘探记录", gui.Context.Texts);
        Assert.Contains("继续", gui.Context.Buttons);
        Assert.Contains(4f, gui.Context.VerticalSpacings);
    }

    /// <summary>
    /// 验证 Web-first HUD 的交互面板 capture 会真正截断 Demo 玩法输入，HUD 外透明区域则 pass-through 到玩法脚本。
    /// </summary>
    [Fact]
    public void DemoWebFirstHudCaptureBlocksGameplayInputAndTransparentAreaPassesThrough()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out _, out ScriptInputApi input);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            MaterialBrush brush = entity.AddComponent<MaterialBrush>();
            WeaponController weapons = entity.AddComponent<WeaponController>();
            using GameUiHost hudHost = CreateDemoHudHost();
            RoutedUiInputSource uiInput = new();
            UiInputRouter router = new(hudHost, uiInput);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            // Assert：验证不变式与预期结果
            Assert.Equal(0, weapons.PrimaryFireCount);
            Assert.Equal(0, brush.SelectedIndex);
            Assert.Equal(4, brush.Radius);

            UiInputCapture captured = RouteScriptInputThroughGameUi(
                router,
                uiInput,
                input,
                new UiPointerState(20f, 20f, 0f, 1f, LeftDown: true, RightDown: false, MiddleDown: false),
                [Key.Digit6],
                [MouseButton.Left]);
            engine.RunHeadlessTicks(1);

            Assert.True(captured.HitsUi);
            Assert.False(captured.AllowWorldMouse);
            Assert.False(captured.AllowWorldKeyboard);
            Assert.False(input.WasPressed(Key.Digit6));
            Assert.False(input.WasMousePressed(MouseButton.Left));
            Assert.Equal(0, weapons.PrimaryFireCount);
            Assert.Equal(0, brush.SelectedIndex);
            Assert.Equal(4, brush.Radius);

            UiInputCapture passedThrough = RouteScriptInputThroughGameUi(
                router,
                uiInput,
                input,
                new UiPointerState(36f, 4f, 0f, 1f, LeftDown: true, RightDown: false, MiddleDown: false),
                [Key.Digit6],
                [MouseButton.Left]);
            engine.RunHeadlessTicks(1);

            Assert.False(passedThrough.HitsUi);
            Assert.True(passedThrough.AllowWorldMouse);
            Assert.True(passedThrough.AllowWorldKeyboard);
            Assert.True(input.WasPressed(Key.Digit6));
            Assert.True(input.WasMousePressed(MouseButton.Left));
            Assert.Equal(1, weapons.PrimaryFireCount);
            Assert.Equal(5, brush.SelectedIndex);
            Assert.Equal(5, brush.Radius);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Demo UI 控制器使用脚本 UI 服务打开主菜单/HUD、切换模态页并返回。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerOpensScreensAndClosesModalsThroughScriptService()
    {
        // Arrange：准备输入与初始状态
        GameUiDemoController controller = new();
        FakeGameUiService ui = new();

        controller.StartForService(ui);
        ScriptUiStringHandle title = ui.InternString("晶体 3/3");
        // Assert：验证预期结果
        Assert.NotEqual(default, controller.MainScreen);
        Assert.Equal(default, controller.HudScreenHandle);
        Assert.Equal([GameUiDemoController.MainMenuScreen], ui.ShownScreens);
        Assert.Equal(title, ui.InternString("晶体 3/3"));
        Assert.NotEqual(title, ui.InternString("暂停"));
        ui.Raise(GameUiDemoController.Action("open_settings"));
        ScriptUiScreenHandle settings = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("open_inventory"));
        ScriptUiScreenHandle inventory = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("open_dialog"));
        ScriptUiScreenHandle dialog = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("close_dialog"));
        ui.Raise(GameUiDemoController.Action("start_game"));
        Assert.Equal(default, controller.TelemetryScreenHandle);
        Assert.DoesNotContain(GameUiDemoController.Path("hud.weapon"), ui.WrittenPaths);
        ui.Raise(GameUiDemoController.Action("toggle_telemetry"));

        Assert.Equal(
            [GameUiDemoController.MainMenuScreen, GameUiDemoController.HudScreen, GameUiDemoController.TelemetryScreen],
            ui.ShownScreens);
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
        AssertHudPathWritten(ui, "hud.reload");
        AssertHudPathWritten(ui, "hud.overheated");
        AssertHudPathWritten(ui, "hud.material_slot");
        AssertHudPathWritten(ui, "hud.brush_radius");
        AssertHudPathWritten(ui, "hud.explosions");
        AssertHudPathWritten(ui, "hud.shots");
        AssertHudPathWritten(ui, "hud.collapse_islands");
        AssertHudPathWritten(ui, "hud.collapse_scan");
        AssertHudPathWritten(ui, "hud.crystals");
        AssertHudPathWritten(ui, "hud.time");
        AssertHudPathWritten(ui, "hud.hazard");
        AssertHudPathWritten(ui, "hud.score");
        AssertHudPathWritten(ui, "hud.distance");
        AssertHudPathWritten(ui, "hud.longitude");
        AssertHudPathWritten(ui, "hud.depth");
        AssertHudPathWritten(ui, "hud.elevation");
        AssertHudPathWritten(ui, "hud.fps");
        AssertHudPathWritten(ui, "hud.frame_p99");
        AssertHudPathWritten(ui, "hud.frame_low1");
        AssertHudPathWritten(ui, "hud.jitter");
        AssertHudPathWritten(ui, "hud.particles");
        AssertHudPathWritten(ui, "hud.lights");
        AssertHudPathWritten(ui, "hud.bodies");
        AssertHudPathWritten(ui, "hud.fx");
        Assert.DoesNotContain(GameUiDemoController.ResultScreen, ui.PushedScreens);
    }

    /// <summary>
    /// 验证模式选择事件会更新正式 run director 与主菜单模型，并由开始事件切换到 Sandbox HUD。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerSelectsCampaignOrSandboxAndStartsSelectedRun()
    {
        CampaignConfig config = CampaignConfig.BuiltinDefault;
        FakeRuntimeControlApi runtime = new();
        RuntimeControlSnapshot snapshot = runtime.Capture() with { WorldSeed = config.InitialRunSeed };
        CampaignRunDirector run = new();
        run.Initialize(config, runtime, in snapshot);
        GameUiDemoController controller = new();
        FakeGameUiService ui = new();
        controller.BindRunDirector(run);

        controller.StartForService(ui, runtime);

        Assert.Equal(1, runtime.PauseCount);
        Assert.Equal(1.0, GetUiValue(ui, "menu.campaign_selected"), precision: 3);
        Assert.Equal(0.0, GetUiValue(ui, "menu.sandbox_selected"), precision: 3);

        ui.Raise(GameUiDemoController.Action("select_sandbox"));

        Assert.Equal(DemoGameMode.InfiniteSandbox, run.Mode);
        Assert.Equal(0.0, GetUiValue(ui, "menu.campaign_selected"), precision: 3);
        Assert.Equal(1.0, GetUiValue(ui, "menu.sandbox_selected"), precision: 3);
        AssertUiPathWritten(ui, "menu.mode_text");

        ui.Raise(GameUiDemoController.Action("start_game"));

        Assert.Equal(CampaignRunState.StartingRun, run.State);
        Assert.Equal(1, runtime.ResumeCount);
        Assert.Equal(default, controller.MainScreen);
        Assert.NotEqual(default, controller.HudScreenHandle);
        Assert.Contains(GameUiDemoController.MainMenuScreen, ui.ShownScreens);
        Assert.Contains(GameUiDemoController.HudScreen, ui.ShownScreens);
        AssertUiPathWritten(ui, "hud.mode_text");
        AssertUiPathWritten(ui, "hud.seed_text");
        AssertUiPathWritten(ui, "hud.run_state_text");
    }

    /// <summary>
    /// 验证 Demo 脚本通过公开零分配 CopyCanvases API 发现 primary 与两个排序后的 overlay Canvas。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerDiscoversExplicitSceneCanvasesThroughPublicApi()
    {
        GameUiDemoController controller = new();
        FakeGameUiService ui = new()
        {
            PrimaryCanvas = new ScriptUiCanvasHandle(11),
            Canvases =
            [
                new ScriptUiCanvasHandle(11),
                new ScriptUiCanvasHandle(12),
                new ScriptUiCanvasHandle(13),
            ],
        };

        controller.StartForService(ui);

        Assert.Equal(3, controller.CanvasCount);
        Assert.Equal(new ScriptUiCanvasHandle(12), controller.PixelOverlayCanvas);
        Assert.Equal(new ScriptUiCanvasHandle(13), controller.PhysicalOverlayCanvas);
        Assert.Equal(default, controller.PixelOverlayScreenHandle);
        Assert.Equal(default, controller.PhysicalOverlayScreenHandle);

        ui.Raise(GameUiDemoController.Action("start_game"));
        ui.Raise(GameUiDemoController.Action("toggle_telemetry"));
        Assert.Contains((new ScriptUiCanvasHandle(12), GameUiDemoController.PixelOverlayScreen), ui.ShownCanvasScreens);
        Assert.Contains((new ScriptUiCanvasHandle(13), GameUiDemoController.PhysicalOverlayScreen), ui.ShownCanvasScreens);
        Assert.NotEqual(default, controller.PixelOverlayScreenHandle);
        Assert.NotEqual(default, controller.PhysicalOverlayScreenHandle);

        ui.Raise(GameUiDemoController.Action("toggle_telemetry"));
        Assert.Equal(default, controller.PixelOverlayScreenHandle);
        Assert.Equal(default, controller.PhysicalOverlayScreenHandle);

        controller.StopForService();
        Assert.Equal(0, controller.CanvasCount);
        Assert.Equal(default, controller.PixelOverlayCanvas);
        Assert.Equal(default, controller.PhysicalOverlayCanvas);
    }

    /// <summary>
    /// 验证 Play→Stop 会对称移除菜单、HUD、遥测和模态屏，第二次 Play 使用全新句柄且事件只处理一次。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerCanStopAndStartAgainWithoutLeakingScreens()
    {
        GameUiDemoController controller = new();
        FakeGameUiService ui = new();

        controller.StartForService(ui);
        ScriptUiScreenHandle firstMain = controller.MainScreen;
        ui.Raise(GameUiDemoController.Action("start_game"));
        ScriptUiScreenHandle firstHud = controller.HudScreenHandle;
        ui.Raise(GameUiDemoController.Action("toggle_telemetry"));
        ScriptUiScreenHandle firstTelemetry = controller.TelemetryScreenHandle;
        ui.Raise(GameUiDemoController.Action("open_settings"));
        ScriptUiScreenHandle firstModal = controller.ModalScreen;

        controller.StopForService();

        Assert.Equal(default, controller.MainScreen);
        Assert.Equal(default, controller.HudScreenHandle);
        Assert.Equal(default, controller.TelemetryScreenHandle);
        Assert.Equal(default, controller.ModalScreen);
        Assert.Equal(default, controller.LastAction);
        Assert.Contains(firstMain, ui.HiddenScreens);
        Assert.Contains(firstHud, ui.HiddenScreens);
        Assert.Contains(firstTelemetry, ui.HiddenScreens);
        Assert.Contains(firstModal, ui.HiddenScreens);

        ui.Raise(GameUiDemoController.Action("start_game"));
        Assert.Equal(default, controller.MainScreen);

        controller.StartForService(ui);
        ScriptUiScreenHandle secondMain = controller.MainScreen;
        Assert.NotEqual(firstMain, secondMain);
        ui.Raise(GameUiDemoController.Action("start_game"));
        ScriptUiScreenHandle secondHud = controller.HudScreenHandle;
        Assert.NotEqual(firstHud, secondHud);
        Assert.Equal(2, ui.ShownScreens.Count(screen => screen == GameUiDemoController.MainMenuScreen));
        Assert.Equal(2, ui.ShownScreens.Count(screen => screen == GameUiDemoController.HudScreen));

        controller.StopForService();
        Assert.Contains(secondMain, ui.HiddenScreens);
        Assert.Contains(secondHud, ui.HiddenScreens);
    }

    /// <summary>
    /// 验证 Editor 临时 Play 恢复脚本快照后，完整脚本生命周期仍会再次派发 OnStart 并重建游戏 UI。
    /// </summary>
    [Fact]
    public void EditorTemporaryPlayCanStartGameUiOnEverySession()
    {
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out _);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            GameUiDemoController controller = entity.AddComponent<GameUiDemoController>();
            using EngineWorldSnapshotStore snapshots = new(engine);
            EngineEditorPlaySessionService play = new(engine, snapshots);

            EditorPlaySessionResult firstEnter = play.EnterPlayTemporary();
            engine.RunHeadlessTicks(1);
            ScriptUiScreenHandle firstMain = controller.MainScreen;
            Assert.Equal(default, controller.HudScreenHandle);
            ui.Raise(GameUiDemoController.Action("start_game"));
            ScriptUiScreenHandle firstHud = controller.HudScreenHandle;
            EditorPlaySessionResult firstExit = play.ExitPlay();

            Assert.True(firstEnter.Succeeded, firstEnter.Message);
            Assert.True(firstExit.Succeeded, firstExit.Message);
            Assert.NotEqual(default, firstMain);
            Assert.NotEqual(default, firstHud);
            Assert.False(controller.Faulted, controller.LastException?.ToString());
            Assert.True(controller.Enabled);
            Assert.Equal(default, controller.MainScreen);
            Assert.Equal(default, controller.HudScreenHandle);

            EditorPlaySessionResult secondEnter = play.EnterPlayTemporary();
            engine.RunHeadlessTicks(1);

            Assert.True(secondEnter.Succeeded, secondEnter.Message);
            Assert.False(controller.Faulted, controller.LastException?.ToString());
            Assert.True(controller.Enabled);
            Assert.NotEqual(default, controller.MainScreen);
            Assert.Equal(default, controller.HudScreenHandle);
            ui.Raise(GameUiDemoController.Action("start_game"));
            Assert.NotEqual(default, controller.HudScreenHandle);
            Assert.NotEqual(firstMain, controller.MainScreen);
            Assert.NotEqual(firstHud, controller.HudScreenHandle);
            Assert.Equal(2, ui.ShownScreens.Count(screenId => screenId == GameUiDemoController.MainMenuScreen));
            Assert.Equal(2, ui.ShownScreens.Count(screenId => screenId == GameUiDemoController.HudScreen));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证暂停、设置返回、继续、重开与退出按钮经脚本 UI 服务路由到运行时控制 facade。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerRoutesPauseRestartAndQuitThroughRuntimeFacade()
    {
        // Arrange：准备输入与初始状态
        GameUiDemoController controller = new();
        FakeGameUiService ui = new();
        FakeRuntimeControlApi runtime = new();

        controller.StartForService(ui, runtime);
        ui.Raise(GameUiDemoController.Action("start_game"));
        ui.Raise(GameUiDemoController.Action("pause_game"));
        ScriptUiScreenHandle firstPause = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("open_settings"));
        ScriptUiScreenHandle settings = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("back_main"));
        ScriptUiScreenHandle returnedPause = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("resume_game"));
        ui.Raise(GameUiDemoController.Action("pause_game"));
        ScriptUiScreenHandle secondPause = controller.ModalScreen;
        ui.Raise(GameUiDemoController.Action("restart_game"));
        ui.Raise(GameUiDemoController.Action("quit_game"));

        // Assert：验证预期结果
        Assert.Equal([
            GameUiDemoController.PauseScreen,
            GameUiDemoController.SettingsScreen,
            GameUiDemoController.PauseScreen,
            GameUiDemoController.PauseScreen,
        ], ui.PushedScreens);
        Assert.NotEqual(default, firstPause);
        Assert.NotEqual(default, settings);
        Assert.NotEqual(default, returnedPause);
        Assert.NotEqual(default, secondPause);
        Assert.Contains(firstPause, ui.HiddenScreens);
        Assert.Contains(settings, ui.HiddenScreens);
        Assert.Contains(returnedPause, ui.HiddenScreens);
        Assert.Contains(secondPause, ui.HiddenScreens);
        Assert.Equal(default, controller.ModalScreen);
        Assert.Equal(2, runtime.PauseCount);
        Assert.Equal(1, runtime.ResumeCount);
        Assert.Equal(1, runtime.RestartCount);
        Assert.Equal(1, runtime.ShutdownCount);
    }

    /// <summary>
    /// 验证设置页音频/VSync 复选框经脚本 UI 服务路由到运行时设置 facade，并回写设置 model path。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerRoutesSettingsTogglesThroughRuntimeFacade()
    {
        // Arrange：准备输入与初始状态
        GameUiDemoController controller = new();
        FakeGameUiService ui = new();
        FakeRuntimeControlApi runtime = new();

        controller.StartForService(ui, runtime);
        ui.Raise(GameUiDemoController.Action("open_settings"));

        // Assert：验证预期结果
        Assert.Equal(1.0, GetUiValue(ui, "settings.audio"), precision: 3);
        Assert.Equal(1.0, GetUiValue(ui, "settings.vsync"), precision: 3);

        ui.Raise(GameUiDemoController.Action("toggle_audio"));
        ui.Raise(GameUiDemoController.Action("toggle_vsync"));

        Assert.False(runtime.AudioEnabled);
        Assert.False(runtime.VSyncEnabled);
        Assert.Equal(1, runtime.AudioToggleCount);
        Assert.Equal(1, runtime.VSyncToggleCount);
        Assert.Equal(0.0, GetUiValue(ui, "settings.audio"), precision: 3);
        Assert.Equal(0.0, GetUiValue(ui, "settings.vsync"), precision: 3);

        ui.Raise(GameUiDemoController.Action("toggle_audio"));
        ui.Raise(GameUiDemoController.Action("toggle_vsync"));

        Assert.True(runtime.AudioEnabled);
        Assert.True(runtime.VSyncEnabled);
        Assert.Equal(2, runtime.AudioToggleCount);
        Assert.Equal(2, runtime.VSyncToggleCount);
        Assert.Equal(1.0, GetUiValue(ui, "settings.audio"), precision: 3);
        Assert.Equal(1.0, GetUiValue(ui, "settings.vsync"), precision: 3);
    }

    /// <summary>
    /// 验证结算屏重开/退出按钮经脚本 UI 服务路由到运行时控制 facade，并清理结算模态状态。
    /// </summary>
    [Fact]
    public void DemoResultModalRoutesRestartAndQuitThroughRuntimeFacade()
    {
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            AssertResultActionRoutes(contentRoot, GameUiDemoController.Action("restart_game"), expectRestart: true);
            AssertResultActionRoutes(contentRoot, GameUiDemoController.Action("quit_game"), expectRestart: false);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证真实 result.xhtml 按钮经 ManagedFallback 产生事件后，再由 Demo UI 控制器路由到运行时 facade。
    /// </summary>
    [Fact]
    public void DemoResultButtonsRouteThroughManagedFallbackEventsToRuntimeFacade()
    {
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            AssertResultButtonRoutesThroughManagedFallback(contentRoot, "开始新轮", "restart_game", expectRestart: true);
            AssertResultButtonRoutesThroughManagedFallback(contentRoot, "退出", "quit_game", expectRestart: false);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 MissionDirector 的真实失败分支会经 Web-first 结算屏只推送一次失败状态。
    /// </summary>
    [Theory]
    [InlineData("time_limit", false)]
    [InlineData("lava_reached_player", true)]
    public void DemoGameUiControllerPublishesMissionFailureResultOnceThroughScriptService(string expectedReason, bool triggerByLava)
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            FakeRuntimeControlApi runtime = new();
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out _, runtime);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            _ = entity.AddComponent<PlayerHealth>();
            _ = entity.AddComponent<WeaponController>();
            MissionDirector mission = entity.AddComponent<MissionDirector>();
            mission.RequiredCrystals = 2;
            mission.TimeLimitSeconds = triggerByLava ? 30f : 0f;
            mission.InitialLavaSurfaceY = triggerByLava ? 0f : 100f;
            mission.LavaRiseCellsPerSecond = 0f;
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            engine.RunHeadlessTicks(3);

            // Assert：验证不变式与预期结果
            Assert.Equal(MissionState.Lost, mission.State);
            Assert.Equal(expectedReason, mission.ResultReason);
            Assert.Equal([GameUiDemoController.ResultScreen], ui.PushedScreens);
            Assert.Equal(1, runtime.PauseCount);
            Assert.Equal(0.0, GetUiValue(ui, "result.won"), precision: 3);
            Assert.Equal(1.0, GetUiValue(ui, "result.reason"), precision: 3);
            Assert.InRange(GetUiValue(ui, "result.time"), 0.0, 1.0);
            Assert.Contains(GameUiDemoController.Path("result.reason"), ui.WrittenPaths);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 MissionDirector 的真实胜利分支会经 Web-first 结算屏只推送一次胜利状态。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesMissionVictoryResultOnceThroughScriptService()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            FakeRuntimeControlApi runtime = new();
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out _, runtime);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            _ = entity.AddComponent<PlayerHealth>();
            _ = entity.AddComponent<WeaponController>();
            MissionDirector mission = entity.AddComponent<MissionDirector>();
            mission.RequiredCrystals = 1;
            mission.TimeLimitSeconds = 30f;
            mission.InitialLavaSurfaceY = 100f;
            mission.LavaRiseCellsPerSecond = 0f;
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            // Assert：验证不变式与预期结果
            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(20, 20, 1, 1)));
            engine.RunHeadlessTicks(1);
            mission.MarkExtractionReached();
            engine.RunHeadlessTicks(4);

            Assert.Equal(MissionState.Won, mission.State);
            Assert.Equal([GameUiDemoController.ResultScreen], ui.PushedScreens);
            Assert.Equal(1, runtime.PauseCount);
            Assert.Equal(1.0, GetUiValue(ui, "result.won"), precision: 3);
            Assert.Equal(1.0, GetUiValue(ui, "result.crystals"), precision: 3);
            Assert.Equal(1.0, GetUiValue(ui, "result.reason"), precision: 3);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证默认横向闯关使用的 GoalTrigger 不依赖旧 MissionDirector，也会驱动 Web-first 结算屏。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesGoalTriggerResultWithoutMissionDirector()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            FakeRuntimeControlApi runtime = new();
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out _, runtime);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            _ = entity.AddComponent<PlayerHealth>();
            _ = entity.AddComponent<WeaponController>();
            GoalTrigger goal = entity.AddComponent<GoalTrigger>();
            goal.X = 8f;
            goal.Y = 8f;
            goal.Width = 28f;
            goal.Height = 28f;
            goal.CelebrationParticleCount = 0;
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(4);
            engine.RunHeadlessTicks(3);

            // Assert：验证不变式与预期结果
            Assert.True(goal.Reached);
            Assert.DoesNotContain(scene.CaptureInspectionSnapshot(), inspected =>
                inspected.Components.Any(component => component.Behaviour is MissionDirector));
            Assert.Equal([GameUiDemoController.ResultScreen], ui.PushedScreens);
            Assert.Equal(1, runtime.PauseCount);
            Assert.Equal(1.0, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.Equal(1.0, GetUiValue(ui, "result.won"), precision: 3);
            Assert.Equal(1.0, GetUiValue(ui, "result.crystals"), precision: 3);
            Assert.Equal(0.0, GetUiValue(ui, "result.time"), precision: 3);
            Assert.Equal(0.0, GetUiValue(ui, "result.score"), precision: 3);
            Assert.Equal(1.0, GetUiValue(ui, "result.reason"), precision: 3);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证默认横向闯关无 MissionDirector 时，HUD 用玩家到 GoalTrigger 的横向距离表达出口进度和路线余量。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesGoalRouteProgressWithoutMissionDirector()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out _);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            _ = entity.AddComponent<PlayerHealth>();
            _ = entity.AddComponent<WeaponController>();
            GoalTrigger goal = entity.AddComponent<GoalTrigger>();
            goal.X = 112f;
            goal.Y = 8f;
            goal.Width = 28f;
            goal.Height = 28f;
            goal.CelebrationParticleCount = 0;
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);

            // Assert：验证不变式与预期结果
            Assert.Equal(0.0, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.time"), precision: 3);

            player.SpawnX = 67.5f;
            player.Respawn();
            engine.RunHeadlessTicks(1);

            Assert.Equal(0.5, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.Equal(0.5, GetHudValue(ui, "hud.time"), precision: 3);

            player.SpawnX = 123f;
            player.Respawn();
            engine.RunHeadlessTicks(2);

            Assert.True(goal.Reached);
            Assert.Equal(1.0, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.time"), precision: 3);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Demo Web-first HUD 每 tick 经公开脚本 UI 服务同步真实生命、武器、任务与危险状态。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesRealHudStateThroughScriptServiceEachTick()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "reloadSeconds": 1.0, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" },
                { "id": "laser", "displayName": "Laser", "kind": "laser", "radius": 1, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 7, "heatPerCell": 120, "beamDps": 1, "muzzleCue": "ui_click", "impactCue": "sizzle_lava_water", "hudColor": "#FFFFFFFF" }
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
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);

            // Assert：验证不变式与预期结果
            Assert.Equal(1.0, GetHudValue(ui, "hud.health"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.weapon"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.ammo"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.cooldown"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.heat"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.reload"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.overheated"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.material_slot"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.brush_radius"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.explosions"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.shots"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_islands"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_scan"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.InRange(GetHudValue(ui, "hud.time"), 0.0, 1.0);
            Assert.InRange(GetHudValue(ui, "hud.hazard"), 0.0, 1.0);
            Assert.True(GetHudValue(ui, "hud.score") > 0.0);
            Assert.Equal(0.5, GetHudValue(ui, "hud.fps"), precision: 3);
            Assert.Equal(0.36, GetHudValue(ui, "hud.frame_p99"), precision: 3);
            Assert.Equal(0.5, GetHudValue(ui, "hud.frame_low1"), precision: 3);
            Assert.Equal(0.075, GetHudValue(ui, "hud.jitter"), precision: 3);
            Assert.Equal(0.064, GetHudValue(ui, "hud.particles"), precision: 3);
            Assert.Equal(0.047, GetHudValue(ui, "hud.lights"), precision: 3);
            Assert.Equal(0.016, GetHudValue(ui, "hud.bodies"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.fx"), precision: 3);

            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            input.Update([Key.R], [], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.True(GetHudValue(ui, "hud.reload") > 0.0);
            Assert.Equal(0.0, GetHudValue(ui, "hud.overheated"), precision: 3);

            health.MaxHealth = 200f;
            input.Update([Key.Digit2], [], mouseX: 0f, mouseY: 0f, wheelY: 0f);
            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(20, 20, 1, 1)));
            engine.RunHeadlessTicks(1);

            Assert.Equal(0.5, GetHudValue(ui, "hud.health"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.weapon"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.reload"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.overheated"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.material_slot"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.brush_radius"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.explosions"), precision: 3);
            Assert.Equal(0.5, GetHudValue(ui, "hud.crystals"), precision: 3);

            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.True(GetHudValue(ui, "hud.heat") > 0.9);
            Assert.Equal(1.0, GetHudValue(ui, "hud.overheated"), precision: 3);
            Assert.True(GetHudValue(ui, "hud.hazard") > 0.0);
            AssertHudPathWritten(ui, "hud.score");

            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(21, 20, 1, 1)));
            engine.RunHeadlessTicks(1);
            mission.MarkExtractionReached();
            engine.RunHeadlessTicks(1);

            Assert.Contains(GameUiDemoController.ResultScreen, ui.PushedScreens);
            Assert.Equal(1.0, GetUiValue(ui, "result.won"), precision: 3);
            Assert.Equal(1.0, GetUiValue(ui, "result.crystals"), precision: 3);
            Assert.InRange(GetUiValue(ui, "result.time"), 0.0, 1.0);
            Assert.True(GetUiValue(ui, "result.score") > 0.0);
            Assert.Equal(1.0, GetUiValue(ui, "result.reason"), precision: 3);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证激光经公开输入打碎 ObjectiveCrystal 后，MineYield 事件会推进 MissionDirector 并同步 Web-first HUD。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesLaserCrystalMineYieldThroughScriptService()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "reloadSeconds": 1.0, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" },
                { "id": "laser", "displayName": "Laser", "kind": "laser", "radius": 1, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 7, "heatPerCell": 80, "beamDps": 4800, "muzzleCue": "ui_click", "impactCue": "sizzle_lava_water", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out ScriptInputApi input);
            Entity playerEntity = scene.CreateEntity();
            _ = playerEntity.AddComponent<Transform>();
            PlayerController player = playerEntity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            _ = playerEntity.AddComponent<PlayerHealth>();
            WeaponController weapons = playerEntity.AddComponent<WeaponController>();
            MissionDirector mission = playerEntity.AddComponent<MissionDirector>();
            mission.RequiredCrystals = 1;
            mission.TimeLimitSeconds = 30f;
            mission.InitialLavaSurfaceY = 100f;
            mission.LavaRiseCellsPerSecond = 0f;
            StartGameplay(playerEntity.AddComponent<GameUiDemoController>(), ui);

            ObjectiveCrystal crystal = scene.CreateEntity().AddComponent<ObjectiveCrystal>();
            crystal.X = 36;
            crystal.Y = 34;
            crystal.Radius = 1;

            // Act：执行被测操作
            engine.RunHeadlessTicks(2);
            CellGrid grid = engine.Context.GetService<CellGrid>();
            ushort crystalBefore = grid.GetMaterial(36, 34);
            // Assert：验证不变式与预期结果
            Assert.NotEqual((ushort)0, crystalBefore);

            input.Update([Key.Digit2], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            input.Update([], [], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(3);

            Assert.Equal(WeaponKind.Laser, weapons.LastDispatchedKind);
            Assert.True(weapons.PrimaryFireCount > 0);
            Assert.NotEqual(crystalBefore, grid.GetMaterial(36, 34));
            Assert.True(crystal.CollectedCells > 0);
            Assert.True(mission.CrystalsCollected > 0);
            Assert.Equal(1.0, GetHudValue(ui, "hud.weapon"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.True(GetHudValue(ui, "hud.heat") > 0.0);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Game UI 控制器拆到 UI/root entity 时，HUD 仍会跨场景实体发现真实 gameplay source。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerDiscoversGameplayHudSourcesAcrossSceneEntities()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "reloadSeconds": 1.0, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" },
                { "id": "laser", "displayName": "Laser", "kind": "laser", "radius": 1, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 7, "heatPerCell": 120, "beamDps": 1, "muzzleCue": "ui_click", "impactCue": "sizzle_lava_water", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out ScriptInputApi input);
            Entity playerEntity = scene.CreateEntity();
            _ = playerEntity.AddComponent<Transform>();
            PlayerController player = playerEntity.AddComponent<PlayerController>();
            player.SpawnX = 12f;
            player.SpawnY = 12f;
            PlayerHealth health = playerEntity.AddComponent<PlayerHealth>();
            _ = playerEntity.AddComponent<WeaponController>();
            MissionDirector mission = playerEntity.AddComponent<MissionDirector>();
            mission.RequiredCrystals = 2;
            mission.TimeLimitSeconds = 30f;
            mission.InitialLavaSurfaceY = 100f;
            mission.LavaRiseCellsPerSecond = 0f;
            RisingHazardDirector hazard = scene.CreateEntity().AddComponent<RisingHazardDirector>();
            hazard.StartSurfaceY = 100f;
            hazard.TargetSurfaceY = 70f;
            hazard.RiseSeconds = 1f;
            hazard.LossSurfaceY = 0f;
            hazard.EmitterCount = 1;
            hazard.FillIntervalSeconds = 10f;
            StartGameplay(scene.CreateEntity().AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            health.MaxHealth = 200f;
            input.Update([Key.Digit2], [], mouseX: 0f, mouseY: 0f, wheelY: 0f);
            // Assert：验证不变式与预期结果
            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(20, 20, 1, 1)));
            engine.RunHeadlessTicks(1);

            Assert.Equal(0.5, GetHudValue(ui, "hud.health"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.weapon"), precision: 3);
            Assert.Equal(1.0, GetHudValue(ui, "hud.ammo"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.material_slot"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.brush_radius"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.explosions"), precision: 3);
            Assert.Equal(0.5, GetHudValue(ui, "hud.crystals"), precision: 3);
            Assert.True(GetHudValue(ui, "hud.hazard") > 0.0);
            AssertHudPathWritten(ui, "hud.score");
            AssertHudPathWritten(ui, "hud.reload");
            AssertHudPathWritten(ui, "hud.overheated");
            AssertHudPathWritten(ui, "hud.fps");
            AssertHudPathWritten(ui, "hud.frame_p99");
            AssertHudPathWritten(ui, "hud.frame_low1");
            AssertHudPathWritten(ui, "hud.jitter");
            AssertHudPathWritten(ui, "hud.particles");
            AssertHudPathWritten(ui, "hud.lights");
            AssertHudPathWritten(ui, "hud.bodies");
            AssertHudPathWritten(ui, "hud.fx");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Web-first HUD 会同步 fallback DemoHud 已展示的材质笔刷与爆破工具状态。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesBrushAndExplosionHudStateThroughScriptService()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out ScriptInputApi input);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            _ = entity.AddComponent<MaterialBrush>();
            ExplosiveTool explosive = entity.AddComponent<ExplosiveTool>();
            explosive.CooldownSeconds = 0f;
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);

            // Assert：验证不变式与预期结果
            Assert.Equal(0.0, GetHudValue(ui, "hud.material_slot"), precision: 3);
            Assert.Equal(0.167, GetHudValue(ui, "hud.brush_radius"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.explosions"), precision: 3);

            input.Update([Key.Digit6], [MouseButton.Middle], mouseX: 36f, mouseY: 34f, wheelY: 1f);
            engine.RunHeadlessTicks(1);

            Assert.Equal(0.556, GetHudValue(ui, "hud.material_slot"), precision: 3);
            Assert.Equal(0.208, GetHudValue(ui, "hud.brush_radius"), precision: 3);
            Assert.Equal(0.1, GetHudValue(ui, "hud.explosions"), precision: 3);
            AssertHudPathWritten(ui, "hud.material_slot");
            AssertHudPathWritten(ui, "hud.brush_radius");
            AssertHudPathWritten(ui, "hud.explosions");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 HUD 会显示唯一输入模式、当前装备和准星下材质的用途说明。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesInputEquipmentAndCursorMaterialText()
    {
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out ScriptInputApi input);
            CellGrid grid = engine.Context.GetService<CellGrid>();
            Assert.True(engine.Context.GetService<MaterialTable>().TryGetId("stone", out ushort stone));
            FillRect(grid, stone, minX: 10, minY: 10, maxX: 11, maxY: 11);

            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            _ = entity.AddComponent<PlayerController>();
            _ = entity.AddComponent<MaterialBrush>();
            _ = entity.AddComponent<WeaponController>();
            PlayerInputModeController inputMode = entity.AddComponent<PlayerInputModeController>();
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            input.Update([], [], mouseX: 10f, mouseY: 10f, wheelY: 0f);
            engine.RunHeadlessTicks(1);

            Assert.Equal(PlayerInputMode.Combat, inputMode.Mode);
            Assert.Equal("战斗 / Combat", GetUiText(ui, "hud.input_mode_text"));
            Assert.Equal("Shot", GetUiText(ui, "hud.equipment_text"));
            Assert.Equal("stone", GetUiText(ui, "hud.material_name"));
            Assert.Equal("石：可破坏固体，破碎后成为砂砾", GetUiText(ui, "hud.material_detail"));

            input.Update([Key.B], [], mouseX: 10f, mouseY: 10f, wheelY: 0f);
            engine.RunHeadlessTicks(1);

            Assert.Equal(PlayerInputMode.MaterialBrush, inputMode.Mode);
            Assert.Equal("材质刷 / Material Brush", GetUiText(ui, "hud.input_mode_text"));
            Assert.Equal("sand", GetUiText(ui, "hud.equipment_text"));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Web-first HUD 会同步 PlayableProjectileTool 的射击与坍塌扫描状态。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesProjectileHudStateThroughScriptService()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out ScriptInputApi input);
            CellGrid grid = engine.Context.GetService<CellGrid>();
            // Assert：验证预期结果
            Assert.True(engine.Context.GetService<MaterialTable>().TryGetId("stone", out ushort stone));
            FillRect(grid, stone, minX: 34, minY: 24, maxX: 35, maxY: 46);
            Entity gameplayEntity = scene.CreateEntity();
            _ = gameplayEntity.AddComponent<Transform>();
            PlayerController player = gameplayEntity.AddComponent<PlayerController>();
            player.SpawnX = 14f;
            player.SpawnY = 30f;
            PlayableProjectileTool projectile = gameplayEntity.AddComponent<PlayableProjectileTool>();
            projectile.CooldownSeconds = 0f;
            projectile.Range = 80f;
            projectile.ImpactRadius = 1;
            projectile.ImpactForce = 2f;
            projectile.UseExplosionDamage = false;
            projectile.CollapseScanRadius = 6;
            StartGameplay(scene.CreateEntity().AddComponent<GameUiDemoController>(), ui);

            engine.RunHeadlessTicks(1);
            Assert.Equal(0.0, GetHudValue(ui, "hud.shots"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_islands"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_scan"), precision: 3);

            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            input.Update([], [], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(3);

            int scanRadius = Math.Clamp(projectile.CollapseScanRadius, 4, 320);
            double scanCapacity = ((scanRadius * 2) + 1) * ((scanRadius * 2) + 1);
            double expectedScan = Math.Clamp(projectile.LastCollapseSolidCandidates / scanCapacity, 0.0, 1.0);
            Assert.Equal(1, projectile.ShotsFired);
            Assert.True(projectile.LastCollapseSolidCandidates > 0);
            Assert.Equal(0.1, GetHudValue(ui, "hud.shots"), precision: 3);
            Assert.Equal(Math.Clamp(projectile.CollapsedFloatingIslands / 10.0, 0.0, 1.0), GetHudValue(ui, "hud.collapse_islands"), precision: 3);
            Assert.Equal(expectedScan, GetHudValue(ui, "hud.collapse_scan"), precision: 3);
            AssertHudPathWritten(ui, "hud.shots");
            AssertHudPathWritten(ui, "hud.collapse_islands");
            AssertHudPathWritten(ui, "hud.collapse_scan");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证弹药耗尽后 WeaponController 不再分派主火，Web-first HUD 稳定发布耗尽弹药状态。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesDepletedAmmoAndSuppressesExtraDispatchThroughScriptService()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "single", "displayName": "Single", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 1, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
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
            WeaponController weapons = entity.AddComponent<WeaponController>();
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            // Assert：验证不变式与预期结果
            Assert.Equal(1, weapons.CurrentAmmo);
            Assert.Equal(1.0, GetHudValue(ui, "hud.ammo"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.shots"), precision: 3);

            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);

            Assert.Equal(WeaponKind.SingleShot, weapons.LastDispatchedKind);
            Assert.Equal(0, weapons.CurrentAmmo);
            Assert.Equal(1, weapons.PrimaryFireCount);
            Assert.Equal(0.0, GetHudValue(ui, "hud.ammo"), precision: 3);
            Assert.Equal(0.1, GetHudValue(ui, "hud.shots"), precision: 3);

            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(2);

            Assert.Equal(0, weapons.CurrentAmmo);
            Assert.Equal(1, weapons.PrimaryFireCount);
            Assert.Equal(0.0, GetHudValue(ui, "hud.ammo"), precision: 3);
            Assert.Equal(0.1, GetHudValue(ui, "hud.shots"), precision: 3);
            AssertHudPathWritten(ui, "hud.ammo");
            AssertHudPathWritten(ui, "hud.shots");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Web-first HUD 的射击计数优先来自 WeaponController 主火路径，非 projectile 武器也会同步。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesWeaponControllerFireCountForNonProjectileWeapons()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "builder", "displayName": "Builder", "kind": "builder", "damage": 0, "radius": 1, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 5, "spawnMaterial": "stone", "muzzleCue": "ui_click", "impactCue": "impact_stone", "hudColor": "#FFFFFFFF" }
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
            WeaponController weapons = entity.AddComponent<WeaponController>();
            StartGameplay(entity.AddComponent<GameUiDemoController>(), ui);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            // Assert：验证不变式与预期结果
            Assert.Equal(0.0, GetHudValue(ui, "hud.shots"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_islands"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_scan"), precision: 3);

            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);

            Assert.Equal(WeaponKind.Builder, weapons.LastDispatchedKind);
            Assert.Equal(1, weapons.PrimaryFireCount);
            Assert.Equal(0.1, GetHudValue(ui, "hud.shots"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_islands"), precision: 3);
            Assert.Equal(0.0, GetHudValue(ui, "hud.collapse_scan"), precision: 3);
            AssertHudPathWritten(ui, "hud.shots");
            AssertHudPathWritten(ui, "hud.collapse_islands");
            AssertHudPathWritten(ui, "hud.collapse_scan");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Web-first HUD 会发布与 PlayableHud 同口径的短命视觉 FX burst 数。
    /// </summary>
    [Fact]
    public void DemoGameUiControllerPublishesTransientFxHudStateThroughScriptService()
    {
        // Arrange：搭建测试场景与依赖
        string contentRoot = CreateTemporaryWeaponContent(
                                 /*lang=json,strict*/
                                 """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out _);
            StartGameplay(scene.CreateEntity().AddComponent<GameUiDemoController>(), ui);
            _ = scene.CreateEntity().AddComponent<TransientFxEmitter>();

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            engine.RunHeadlessTicks(1);

            double fx = GetHudValue(ui, "hud.fx");
            // Assert：验证不变式与预期结果
            Assert.InRange(fx, 0.0001, 1.0);
            AssertHudPathWritten(ui, "hud.fx");
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

        controller.StartForService(NoopGameUiService.Instance);
        controller.HandleUiEvent(new ScriptUiEvent(default, default, GameUiDemoController.Action("open_settings"), default));

        Assert.Equal(default, controller.MainScreen);
        Assert.Equal(default, controller.HudScreenHandle);
        Assert.Equal(default, controller.ModalScreen);
        Assert.Equal(GameUiDemoController.Action("open_settings"), controller.LastAction);
    }

    /// <summary>
    /// 验证 GL smoke 开启时，同一批 Demo UI 屏幕能被 RmlUi 后端载入、绑定模型并合成。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void DemoUiScreensLoadThroughRmlUiBackendWhenGlSmokeIsEnabled()
    {
        // Arrange：准备输入与初始状态；NativeSmokeFact 在 discovery 阶段负责未启用环境的 skipped 状态。
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

        Span<UiScreenStackEntry> stack = stackalloc UiScreenStackEntry[16];
        int index = 0;
        int hudIndex = -1;
        int telemetryIndex = -1;
        int mainIndex = -1;
        foreach (UiManifestScreen screen in manifest.Screens)
        {
            UiDocumentHandle document = backend.LoadDocument(screen.ToDocumentSource());
            stack[index] = new UiScreenStackEntry(new UI.UiScreenHandle(index + 1), screen.ScreenId, document, Modal: screen.Id is "settings" or "inventory" or "dialog" or "pause" or "result");
            if (screen.Id == "main-menu")
            {
                mainIndex = index;
            }

            if (screen.Id == "hud")
            {
                hudIndex = index;
            }

            if (screen.Id == "telemetry")
            {
                telemetryIndex = index;
            }

            index++;
        }

        // Assert：验证预期结果
        Assert.True(mainIndex >= 0);
        Assert.True(hudIndex >= 0);
        Assert.True(telemetryIndex >= 0);
        Assert.Equal(manifest.ScreenCount, index);
        backend.SetScreenStack(stack.Slice(mainIndex, 1));
        backend.Update(1f / 60f);
        // main_panel 位于 (392,24)，设置按钮位于面板内 (28,148)；点击中心验证真实布局命中与事件映射。
        backend.FeedPointerMove(536f, 187f);
        backend.FeedPointerButton(UiPointerButton.Left, isDown: true);
        backend.FeedPointerButton(UiPointerButton.Left, isDown: false);
        backend.Update(1f / 60f);
        Span<RuntimeUiEvent> events = stackalloc RuntimeUiEvent[4];
        int eventCount = backend.DrainEvents(events);
        Assert.Contains(events[..eventCount].ToArray(), e => e.Action == new UI.UiActionId(UiStableId.Hash("open_settings")));

        backend.SetScreenStack(stack[..index]);
        backend.Update(1f / 60f);
        foreach (string path in GameUiDemoController.HudModelPathNames)
        {
            backend.SetModelValue(stack[hudIndex].Document, new UI.UiPathId(UiStableId.Hash(path)), new UI.UiValue(path == "hud.cooldown" ? 1.0 : 0.25));
        }

        foreach (string path in GameUiDemoController.TelemetryModelPathNames)
        {
            backend.SetModelValue(stack[telemetryIndex].Document, new UI.UiPathId(UiStableId.Hash(path)), new UI.UiValue(0.25));
        }

        Assert.True(backend.InvokeAction(stack[0].Document, new UI.UiActionId(UiStableId.Hash("open_settings")), UI.UiValue.FromBoolean(true)));
        backend.Composite(default);
        window.SwapBuffers();
    }

    private static void AssertScreenContract(
        UiManifest manifest,
        string screenId,
        string contract,
        string[] expectedPaths,
        string[] expectedActions)
    {
        XDocument document = XDocument.Load(manifest.GetRequiredScreen(screenId).FullPath);
        XElement root = document.Root ?? throw new InvalidDataException($"UI screen 缺少根节点：{screenId}");

        Assert.Equal(screenId, (string?)root.Attribute("data-screen"));
        Assert.Equal(contract, (string?)root.Attribute("data-contract"));
        Assert.Equal(Sorted(expectedPaths), Sorted(ExtractAttributeValues(document, "path", "data-model")));
        Assert.Equal(Sorted(expectedActions), Sorted(ExtractAttributeValues(document, "data-event-click", "data-event-change")));
    }

    private static void AssertManagedModelPaths(GameUiHost host, UI.UiScreenHandle screen, string[] expectedPaths)
    {
        UI.UiPathId[] paths = new UI.UiPathId[32];
        int count = host.CopyModelPaths(screen, paths);
        int[] actual = [.. paths[..count].Select(path => path.Value).OrderBy(value => value)];
        int[] expected =
        [
            .. expectedPaths
                .Select(path => new UI.UiPathId(UiStableId.Hash(path)).Value)
                .OrderBy(value => value),
        ];
        Assert.Equal(expected, actual);
    }

    private static void AssertManagedDoubleValueRoundTrips(
        GameUiHost host,
        UI.UiScreenHandle screen,
        string path,
        double expected)
    {
        UI.UiPathId pathId = new(UiStableId.Hash(path));
        host.SetModelValue(screen, pathId, new UI.UiValue(expected));

        Assert.True(host.TryGetModelValue(screen, pathId, out UI.UiValue value), $"ManagedFallback 未声明 UI path：{path}");
        Assert.Equal(expected, value.AsDouble(), precision: 3);
    }

    private static void AssertResultActionRoutes(string contentRoot, ScriptUiActionId action, bool expectRestart)
    {
        FakeRuntimeControlApi runtime = new();
        using Engine engine = CreateHudEngine(contentRoot, out ScriptScene scene, out FakeGameUiService ui, out _, runtime);
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 12f;
        _ = entity.AddComponent<PlayerHealth>();
        _ = entity.AddComponent<WeaponController>();
        MissionDirector mission = entity.AddComponent<MissionDirector>();
        mission.RequiredCrystals = 1;
        mission.TimeLimitSeconds = 30f;
        mission.InitialLavaSurfaceY = 100f;
        mission.LavaRiseCellsPerSecond = 0f;
        GameUiDemoController controller = entity.AddComponent<GameUiDemoController>();
        StartGameplay(controller, ui);

        engine.RunHeadlessTicks(1);
        mission.MarkLost("scripted_loss");
        engine.RunHeadlessTicks(2);
        ScriptUiScreenHandle resultModal = controller.ModalScreen;

        Assert.NotEqual(default, resultModal);
        Assert.Equal(1, runtime.PauseCount);
        Assert.Equal([GameUiDemoController.ResultScreen], ui.PushedScreens);
        Assert.Equal(0.0, GetUiValue(ui, "result.won"), precision: 3);

        ui.Raise(action);

        Assert.Contains(resultModal, ui.HiddenScreens);
        Assert.Equal(default, controller.ModalScreen);
        Assert.Equal(expectRestart ? 1 : 0, runtime.RestartCount);
        Assert.Equal(expectRestart ? 0 : 1, runtime.ShutdownCount);
    }

    private static void AssertResultButtonRoutesThroughManagedFallback(
        string contentRoot,
        string buttonText,
        string expectedAction,
        bool expectRestart)
    {
        ScriptUiActionId action = DrainResultButtonActionThroughManagedFallback(buttonText, expectedAction);
        AssertResultActionRoutes(contentRoot, action, expectRestart);
    }

    private static ScriptUiActionId DrainResultButtonActionThroughManagedFallback(string buttonText, string expectedAction)
    {
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 720, 480, 1f), UiBackendKind.ManagedFallback));

        _ = host.PushModal(
            manifest.GetRequiredScreen(GameUiDemoController.ResultScreen).ScreenId,
            manifest.ResolveDocumentSource(GameUiDemoController.ResultScreen));
        _ = gui.Context.ClickedButtons.Add(buttonText);

        host.Composite(default);

        RuntimeUiEvent[] events = new RuntimeUiEvent[4];
        int eventCount = host.DrainEvents(events);
        UI.UiActionId expected = new(UiStableId.Hash(expectedAction));
        RuntimeUiEvent uiEvent = Assert.Single(events[..eventCount], e => e.Action == expected);

        Assert.Contains("结算", gui.Context.Texts);
        Assert.Contains(buttonText, gui.Context.Buttons);
        return new ScriptUiActionId(uiEvent.Action.Value);
    }

    private static GameUiHost CreateDemoHudHost()
    {
        UiManifest manifest = UiManifestLoader.LoadFromDirectory(DemoUiRoot());
        ManagedFallbackBackend backend = new(new FakeGuiHost());
        GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 720, 480, 1f), UiBackendKind.ManagedFallback));
        _ = host.ShowScreen(
            manifest.GetRequiredScreen(GameUiDemoController.HudScreen).ScreenId,
            manifest.ResolveDocumentSource(GameUiDemoController.HudScreen));
        return host;
    }

    private static UiInputCapture RouteScriptInputThroughGameUi(
        UiInputRouter router,
        RoutedUiInputSource uiInput,
        ScriptInputApi input,
        UiPointerState pointer,
        Key[] keys,
        MouseButton[] buttons)
    {
        uiInput.Pointer = pointer;
        UiInputCapture capture = router.Pump();
        InputArbitrationState state = InputArbitrator.ApplyGameUi(InputArbitrationState.Allowed, capture);
        ScriptInputSnapshotBuilder.Update(
            input,
            keys,
            buttons,
            pointer.X,
            pointer.Y,
            pointer.WheelDeltaY,
            state.AllowWorldKeyboard,
            state.AllowWorldMouse);
        return capture;
    }

    private static string[] ExtractAttributeValues(XDocument document, params string[] attributeNames)
    {
        return
        [
            .. document
                .Descendants()
                .SelectMany(element => attributeNames.Select(name => (string?)element.Attribute(name)))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()),
        ];
    }

    private static string[] UiVisibleText(string path)
    {
        XDocument document = XDocument.Load(path);
        IEnumerable<string> elementText = document
            .Descendants()
            .Where(element => !element.HasElements)
            .Select(element => element.Value.Trim());
        IEnumerable<string> attributeText = document
            .Descendants()
            .Select(element => (string?)element.Attribute("text"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());
        return [.. elementText.Concat(attributeText).Where(value => value.Length > 0)];
    }

    private static string[] Sorted(IEnumerable<string> values)
    {
        string[] sorted = [.. values];
        Array.Sort(sorted, StringComparer.Ordinal);
        return sorted;
    }

    private static void AssertScreen(UiManifest manifest, string id)
    {
        Assert.True(manifest.TryGetScreen(id, out UiManifestScreen screen), $"缺少 UI screen: {id}");
        Assert.True(File.Exists(screen.FullPath), screen.FullPath);
        Assert.Equal(new UiScreenId(UiStableId.Hash(id)), screen.ScreenId);
    }

    private static void StartGameplay(GameUiDemoController controller, FakeGameUiService ui)
    {
        controller.StartForService(ui);
        ui.Raise(GameUiDemoController.Action("start_game"));
        Assert.Equal(default, controller.MainScreen);
        Assert.NotEqual(default, controller.HudScreenHandle);
        ui.Raise(GameUiDemoController.Action("toggle_telemetry"));
        Assert.NotEqual(default, controller.TelemetryScreenHandle);
    }

    private static void AssertHudPathWritten(FakeGameUiService ui, string path)
    {
        Assert.Contains(GameUiDemoController.Path(path), ui.WrittenPaths);
        Assert.True(ui.Values.ContainsKey(GameUiDemoController.Path(path)), $"HUD path 未写入值：{path}");
    }

    private static void AssertUiPathWritten(FakeGameUiService ui, string path)
    {
        Assert.Contains(GameUiDemoController.Path(path), ui.WrittenPaths);
        Assert.True(ui.Values.ContainsKey(GameUiDemoController.Path(path)), $"UI path 未写入值：{path}");
    }

    private static double GetHudValue(FakeGameUiService ui, string path)
    {
        return GetUiValue(ui, path);
    }

    private static double GetUiValue(FakeGameUiService ui, string path)
    {
        Assert.True(ui.Values.TryGetValue(GameUiDemoController.Path(path), out ScriptUiValue value), $"UI path 未写入值：{path}");
        Assert.Equal(Scripting.UiValueKind.Double, value.Kind);
        return value.AsDouble();
    }

    private static string GetUiText(FakeGameUiService ui, string path)
    {
        Assert.True(ui.Values.TryGetValue(GameUiDemoController.Path(path), out ScriptUiValue value), $"UI path 未写入值：{path}");
        Assert.Equal(Scripting.UiValueKind.StringHandle, value.Kind);
        return ui.ResolveString(value.AsStringHandle());
    }

    private static string[] HudPaths()
    {
        return [
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
            "hud.distance",
            "hud.longitude",
            "hud.depth",
            "hud.elevation",
            "hud.fps",
            "hud.frame_p99",
            "hud.frame_low1",
            "hud.jitter",
            "hud.particles",
            "hud.lights",
            "hud.bodies",
            "hud.fx",
        ];
    }

    private static WeaponCatalog LoadDefaultWeaponCatalog()
    {
        string path = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content", "weapons.json");
        return WeaponCatalog.Parse(File.ReadAllText(path));
    }

    private static Engine CreateHudEngine(
        string contentRoot,
        out ScriptScene scene,
        out FakeGameUiService ui,
        out ScriptInputApi input,
        IRuntimeControlApi? runtime = null)
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
        engine.Context.Counters.RenderFramesPerSecond = 60.0;
        engine.Context.Counters.RenderFrameP99Milliseconds = 18.0;
        engine.Context.Counters.RenderFrameLow1PercentFps = 60.0;
        engine.Context.Counters.RenderFrameJitterMilliseconds = 1.5;
        engine.Context.Counters.FreeParticles = 64;
        engine.Context.Counters.RigidBodies = 2;
        engine.Context.RegisterService<IDiagnosticsApi>(new EngineScriptDiagnosticsApi(
            engine.Context.Counters,
            engine.Context.Clock,
            new DebugOverlaySettings(),
            () => 3));
        if (runtime is not null)
        {
            engine.Context.RegisterService(runtime);
        }

        _ = engine.AttachScriptingFromServices();
        return engine;
    }

    private static void FillRect(CellGrid grid, ushort material, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                grid.MaterialAt(x, y) = material;
                grid.FlagsAt(x, y) = default;
            }
        }
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

    private static float ReadInlinePixelStyle(XElement element, string property)
    {
        string style = (string?)element.Attribute("style") ?? string.Empty;
        foreach (string declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = declaration.IndexOf(':');
            if (separator <= 0 ||
                !string.Equals(declaration[..separator].Trim(), property, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = declaration[(separator + 1)..].Trim();
            if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^2].Trim();
            }

            return float.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float parsed)
                ? parsed
                : throw new InvalidDataException($"{property} 不是有效 px 数值：{value}");
        }

        throw new InvalidDataException($"元素缺少 inline style：{property}");
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

    private sealed class TransientFxEmitter : Behaviour
    {
        protected override void OnStart()
        {
            TransientParticleBurst.Emit(Context, x: 12f, y: 12f, count: 8, speed: 10f, lifetime: 60);
        }
    }

    private sealed class FakeGameUiService : ScriptGameUiService
    {
        private int _nextHandle = 1;
        private readonly Dictionary<string, ScriptUiStringHandle> _strings = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _screenIds = [];

        public event Action<ScriptUiEvent>? UiEventRaised;

        public List<string> ShownScreens { get; } = [];

        public List<string> PushedScreens { get; } = [];

        public List<ScriptUiScreenHandle> HiddenScreens { get; } = [];

        public List<(ScriptUiCanvasHandle Canvas, string ScreenId)> ShownCanvasScreens { get; } = [];

        public List<ScriptUiPathId> WrittenPaths { get; } = [];

        public Dictionary<ScriptUiPathId, ScriptUiValue> Values { get; } = [];

        public ScriptUiCanvasHandle PrimaryCanvas { get; set; }

        public ScriptUiCanvasHandle[] Canvases { get; set; } = [];

        public int CopyCanvases(Span<ScriptUiCanvasHandle> destination)
        {
            int count = Math.Min(destination.Length, Canvases.Length);
            Canvases.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        public ScriptUiScreenHandle ShowScreen(string screenId)
        {
            ShownScreens.Add(screenId);
            return CreateScreenHandle(screenId);
        }

        public ScriptUiScreenHandle ShowScreen(ScriptUiCanvasHandle canvas, string screenId)
        {
            if (canvas.Value == 0)
            {
                return default;
            }

            ShownCanvasScreens.Add((canvas, screenId));
            return CreateScreenHandle(screenId);
        }

        public void HideScreen(ScriptUiScreenHandle screen)
        {
            HiddenScreens.Add(screen);
        }

        public ScriptUiScreenHandle PushModal(string screenId)
        {
            PushedScreens.Add(screenId);
            return CreateScreenHandle(screenId);
        }

        public void BindModel(ScriptUiScreenHandle screen, ScriptUiModelName modelName, ScriptIUiModel model)
        {
            _ = screen;
            _ = modelName;
            _ = model;
        }

        public ScriptUiStringHandle InternString(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_strings.TryGetValue(value, out ScriptUiStringHandle existing))
            {
                return existing;
            }

            ScriptUiStringHandle handle = new(_strings.Count + 1);
            _strings.Add(value, handle);
            return handle;
        }

        public string ResolveString(ScriptUiStringHandle handle)
        {
            foreach ((string value, ScriptUiStringHandle candidate) in _strings)
            {
                if (candidate == handle)
                {
                    return value;
                }
            }

            throw new KeyNotFoundException($"未知 UI string handle：{handle.Value}。");
        }

        public void SetValue(ScriptUiScreenHandle screen, ScriptUiPathId path, in ScriptUiValue value)
        {
            ValidateDocumentPath(screen, path);
            WrittenPaths.Add(path);
            Values[path] = value;
        }

        private ScriptUiScreenHandle CreateScreenHandle(string screenId)
        {
            ScriptUiScreenHandle handle = new(_nextHandle++);
            _screenIds.Add(handle.Value, screenId);
            return handle;
        }

        private void ValidateDocumentPath(ScriptUiScreenHandle screen, ScriptUiPathId path)
        {
            if (!_screenIds.TryGetValue(screen.Value, out string? screenId))
            {
                return;
            }

            ReadOnlySpan<string> allowed = screenId switch
            {
                GameUiDemoController.MainMenuScreen => GameUiDemoController.MenuModelPathNames,
                GameUiDemoController.HudScreen => GameUiDemoController.HudModelPathNames,
                GameUiDemoController.TelemetryScreen => GameUiDemoController.TelemetryModelPathNames,
                GameUiDemoController.ResultScreen => GameUiDemoController.ResultModelPathNames,
                _ => default,
            };
            if (allowed.IsEmpty)
            {
                return;
            }

            for (int i = 0; i < allowed.Length; i++)
            {
                if (GameUiDemoController.Path(allowed[i]) == path)
                {
                    return;
                }
            }

            throw new InvalidOperationException($"屏幕 {screenId} 未声明 model path {path.Value}。");
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

    private sealed class RoutedUiInputSource : IUiInputSource
    {
        public UiPointerState Pointer { get; set; }

        public bool TryGetPointer(out UiPointerState state)
        {
            state = Pointer;
            return true;
        }

        public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
        {
            _ = destination;
            modifiers = UiKeyModifiers.None;
            return 0;
        }

        public int CaptureText(Span<char> destination)
        {
            _ = destination;
            return 0;
        }
    }

    private sealed class FakeRuntimeControlApi : IRuntimeControlApi
    {
        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public int RestartCount { get; private set; }

        public int ShutdownCount { get; private set; }

        public int AudioToggleCount { get; private set; }

        public int VSyncToggleCount { get; private set; }

        public bool IsPlaying { get; private set; } = true;

        public bool AudioEnabled { get; private set; } = true;

        public bool VSyncEnabled { get; private set; } = true;

        public RuntimeControlSnapshot Capture()
        {
            return new RuntimeControlSnapshot(IsPlaying, ShutdownCount > 0, RequestedSimHz: 60.0, FrameCount: 0);
        }

        public RuntimeSettingsSnapshot CaptureSettings()
        {
            return new RuntimeSettingsSnapshot(VSyncEnabled, CanToggleVSync: true, AudioEnabled, CanToggleAudio: true);
        }

        public void PauseSimulation()
        {
            PauseCount++;
            IsPlaying = false;
        }

        public void ResumeSimulation()
        {
            ResumeCount++;
            IsPlaying = true;
        }

        public RuntimeControlResult RequestShutdown()
        {
            ShutdownCount++;
            return new RuntimeControlResult(true, "shutdown");
        }

        public RuntimeControlResult OpenEditor()
        {
            return new RuntimeControlResult(true, "editor");
        }

        public RuntimeControlResult RequestRestartCurrentScene()
        {
            RestartCount++;
            IsPlaying = true;
            return new RuntimeControlResult(true, "restart");
        }

        public RuntimeControlResult SetVSyncEnabled(bool enabled)
        {
            VSyncToggleCount++;
            VSyncEnabled = enabled;
            return new RuntimeControlResult(true, "vsync");
        }

        public RuntimeControlResult SetAudioEnabled(bool enabled)
        {
            AudioToggleCount++;
            AudioEnabled = enabled;
            return new RuntimeControlResult(true, "audio");
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

        public void FeedPointerMove(float x, float y)
        {
            _ = x;
            _ = y;
        }

        public void FeedPointerButton(UiPointerButton button, bool isDown)
        {
            _ = button;
            _ = isDown;
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
            _ = deltaX;
            _ = deltaY;
        }

        public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
        {
            _ = key;
            _ = isDown;
            _ = modifiers;
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
            _ = text;
        }
    }

    private sealed class FakeGuiDrawContext : IGuiDrawContext
    {
        public List<string> Texts { get; } = [];

        public List<string> Buttons { get; } = [];

        public List<float> VerticalSpacings { get; } = [];

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

        public void AddVerticalSpacing(float height)
        {
            VerticalSpacings.Add(height);
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
