using PixelEngine.Core.Events;
using PixelEngine.Gui;
using PixelEngine.Rendering;
using Xunit;
using RuntimeUi = PixelEngine.UI;
using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Game UI 脚本服务桥测试。
/// </summary>
public sealed class GameUiServiceBridgeTests
{
    /// <summary>
    /// 验证脚本服务桥会把 Show/Set/Get 与 UI 事件映射到运行时 GameUiHost。
    /// </summary>
    [Fact]
    public void BridgeMapsScriptServiceCallsAndRaisesEvents()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-bridge-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        _ = Directory.CreateDirectory(uiRoot);
        File.WriteAllText(Path.Combine(uiRoot, "main.xhtml"), "<ui><text>Main</text></ui>");
        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));
            GameUiServiceBridge bridge = new(host, root);
            ScriptUi.UiEvent received = default;
            int eventCount = 0;
            bridge.UiEventRaised += e =>
            {
                received = e;
                eventCount++;
            };

            ScriptUi.UiScreenHandle screen = bridge.ShowScreen("main");
            bridge.SetValue(screen, new ScriptUi.UiPathId(7), new ScriptUi.UiValue(42L));
            bool found = bridge.TryGetValue(screen, new ScriptUi.UiPathId(7), out ScriptUi.UiValue value);
            bridge.Invoke(screen, new ScriptUi.UiActionId(5), new ScriptUi.UiValue(77L));
            bool invokedValueFound = bridge.TryGetValue(screen, new ScriptUi.UiPathId(7), out ScriptUi.UiValue invokedValue);
            bridge.OnGameUiEvents([
                new RuntimeUi.UiEvent(
                    backend.LastDocument,
                    new RuntimeUi.UiElementId(3),
                    new RuntimeUi.UiActionId(5),
                    RuntimeUi.UiValue.FromBoolean(true)),
            ]);

            Assert.True(found);
            Assert.Equal(42L, value.AsInt64());
            Assert.True(invokedValueFound);
            Assert.Equal(77L, invokedValue.AsInt64());
            Assert.Equal(Path.Combine(uiRoot, "main.xhtml"), backend.LastSource.Path);
            Assert.Equal(1, eventCount);
            Assert.Equal(screen, received.Screen);
            Assert.Equal(new ScriptUi.UiElementId(3), received.Element);
            Assert.Equal(new ScriptUi.UiActionId(5), received.Action);
            Assert.True(received.Payload.AsBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证接入脚本事件总线后，UI 事件不会同步调用脚本处理器，而是等待脚本相位 drain。
    /// </summary>
    [Fact]
    public void BridgePublishesUiEventsThroughScriptEventBusWhenAttached()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-script-events-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        _ = Directory.CreateDirectory(uiRoot);
        File.WriteAllText(Path.Combine(uiRoot, "main.xhtml"), "<ui><text>Main</text></ui>");
        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));
            EventBus coreEvents = new(capacityPerChannel: 8);
            using ScriptUi.ScriptEventBus scriptEvents = new(coreEvents);
            GameUiServiceBridge bridge = new(host, root);
            bridge.AttachScriptEventBus(scriptEvents);
            ScriptUi.UiEvent received = default;
            int eventCount = 0;
            bridge.UiEventRaised += e =>
            {
                received = e;
                eventCount++;
            };

            ScriptUi.UiScreenHandle screen = bridge.ShowScreen("main");
            bridge.OnGameUiEvents([
                new RuntimeUi.UiEvent(
                    backend.LastDocument,
                    new RuntimeUi.UiElementId(3),
                    new RuntimeUi.UiActionId(5),
                    RuntimeUi.UiValue.FromBoolean(true)),
            ]);

            Assert.Equal(0, eventCount);

            scriptEvents.DrainEvents();

            Assert.Equal(1, eventCount);
            Assert.Equal(screen, received.Screen);
            Assert.Equal(new ScriptUi.UiElementId(3), received.Element);
            Assert.Equal(new ScriptUi.UiActionId(5), received.Action);
            Assert.True(received.Payload.AsBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证先注册的直接 UI 事件处理器在接入脚本事件总线时会迁移为相位 drain 派发。
    /// </summary>
    [Fact]
    public void BridgeMigratesExistingUiEventHandlersWhenScriptEventBusAttaches()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-event-migrate-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        _ = Directory.CreateDirectory(uiRoot);
        File.WriteAllText(Path.Combine(uiRoot, "main.xhtml"), "<ui><text>Main</text></ui>");
        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));
            EventBus coreEvents = new(capacityPerChannel: 8);
            using ScriptUi.ScriptEventBus scriptEvents = new(coreEvents);
            GameUiServiceBridge bridge = new(host, root);
            int eventCount = 0;
            bridge.UiEventRaised += _ => eventCount++;

            _ = bridge.ShowScreen("main");
            bridge.AttachScriptEventBus(scriptEvents);
            bridge.OnGameUiEvents([
                new RuntimeUi.UiEvent(
                    backend.LastDocument,
                    new RuntimeUi.UiElementId(3),
                    new RuntimeUi.UiActionId(5),
                    default),
            ]);

            Assert.Equal(0, eventCount);

            scriptEvents.DrainEvents();

            Assert.Equal(1, eventCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证存在 ui-manifest.json 时服务桥按清单解析 screen id，而不是继续猜 content/ui/&lt;id&gt;.xhtml。
    /// </summary>
    [Fact]
    public void BridgeResolvesScreensThroughManifestWhenPresent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-manifest-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        _ = Directory.CreateDirectory(Path.Combine(uiRoot, "screens"));
        string expected = Path.Combine(uiRoot, "screens", "main.xhtml");
        File.WriteAllText(expected, "<ui><text>Main</text></ui>");
        File.WriteAllText(Path.Combine(uiRoot, RuntimeUi.UiManifestLoader.ManifestFileName), """
            {
              "screens": [
                { "id": "main", "path": "screens/main.xhtml", "preload": true }
              ]
            }
            """);

        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));
            GameUiServiceBridge bridge = new(host, root);

            _ = bridge.ShowScreen("main");

            Assert.Equal(Path.GetFullPath(expected), backend.LastSource.Path);
            Assert.Equal(RuntimeUi.UiStableId.Hash("main"), backend.LastSource.StableId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证无 manifest 的约定路径仍被限制在 content/ui 根目录内，不能用绝对路径或 .. 逃逸。
    /// </summary>
    [Fact]
    public void BridgeRejectsConventionScreenPathsEscapingUiRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-path-guard-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        _ = Directory.CreateDirectory(uiRoot);
        string safe = Path.Combine(uiRoot, "safe.xhtml");
        string outside = Path.Combine(root, "outside.xhtml");
        string siblingUiDocument = Path.Combine(root, "ui.xhtml");
        File.WriteAllText(safe, "<ui><text>Safe</text></ui>");
        File.WriteAllText(outside, "<ui><text>Outside</text></ui>");
        File.WriteAllText(siblingUiDocument, "<ui><text>Sibling</text></ui>");

        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));
            GameUiServiceBridge bridge = new(host, root);

            _ = bridge.ShowScreen("safe");
            InvalidDataException relative = Assert.Throws<InvalidDataException>(() => bridge.ShowScreen("../outside.xhtml"));
            InvalidDataException rooted = Assert.Throws<InvalidDataException>(() => bridge.ShowScreen(outside));
            InvalidDataException currentDirectory = Assert.Throws<InvalidDataException>(() => bridge.ShowScreen("."));
            InvalidDataException collapsedParent = Assert.Throws<InvalidDataException>(() => bridge.ShowScreen("screens/.."));

            Assert.Equal(Path.GetFullPath(safe), backend.LastSource.Path);
            Assert.Equal(1, backend.LoadDocumentCount);
            Assert.Contains("content/ui", relative.Message);
            Assert.Contains("content/ui", rooted.Message);
            Assert.Contains("content/ui", currentDirectory.Message);
            Assert.Contains("content/ui", collapsedParent.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证无 manifest 的正常相对 UI 路径可通过约定补齐扩展名或显式扩展名载入。
    /// </summary>
    [Fact]
    public void BridgeResolvesConventionRelativeScreenPathsWithOptionalExtension()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-relative-paths-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        string screensRoot = Path.Combine(uiRoot, "screens");
        _ = Directory.CreateDirectory(screensRoot);
        string hud = Path.Combine(screensRoot, "hud.xhtml");
        string inventory = Path.Combine(screensRoot, "inventory.xhtml");
        File.WriteAllText(hud, "<ui><text>HUD</text></ui>");
        File.WriteAllText(inventory, "<ui><text>Inventory</text></ui>");

        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));
            GameUiServiceBridge bridge = new(host, root);

            _ = bridge.ShowScreen("screens/hud");

            Assert.Equal(Path.GetFullPath(hud), backend.LastSource.Path);
            Assert.Equal(1, backend.LoadDocumentCount);

            _ = bridge.ShowScreen("screens/inventory.xhtml");

            Assert.Equal(Path.GetFullPath(inventory), backend.LastSource.Path);
            Assert.Equal(2, backend.LoadDocumentCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 manifest preload 屏幕会在服务桥创建时载入，后续显示同屏不重复载入。
    /// </summary>
    [Fact]
    public void BridgePreloadsManifestScreensWithoutShowingThem()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-preload-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        _ = Directory.CreateDirectory(Path.Combine(uiRoot, "screens"));
        string main = Path.Combine(uiRoot, "screens", "main.xhtml");
        string settings = Path.Combine(uiRoot, "screens", "settings.xhtml");
        File.WriteAllText(main, "<ui><text>Main</text></ui>");
        File.WriteAllText(settings, "<ui><text>Settings</text></ui>");
        File.WriteAllText(Path.Combine(uiRoot, RuntimeUi.UiManifestLoader.ManifestFileName), """
            {
              "screens": [
                { "id": "main", "path": "screens/main.xhtml", "preload": true },
                { "id": "settings", "path": "screens/settings.xhtml", "preload": false }
              ]
            }
            """);

        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));

            GameUiServiceBridge bridge = new(host, root);

            Assert.Equal(1, backend.LoadDocumentCount);
            Assert.Equal(1, host.Documents.DocumentCount);
            Assert.Equal(0, host.Documents.StackCount);
            Assert.Equal(Path.GetFullPath(main), backend.LastSource.Path);

            _ = bridge.ShowScreen("main");

            Assert.Equal(1, backend.LoadDocumentCount);
            Assert.Equal(1, host.Documents.StackCount);

            _ = bridge.ShowScreen("settings");

            Assert.Equal(2, backend.LoadDocumentCount);
            Assert.Equal(Path.GetFullPath(settings), backend.LastSource.Path);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 manifest images preload 会通过 ManagedFallbackBackend 真实消费图片路径。
    /// </summary>
    [Fact]
    public void BridgePreloadsManifestImagesThroughManagedFallbackBackend()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-image-preload-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        string images = Path.Combine(uiRoot, "images");
        _ = Directory.CreateDirectory(images);
        string logo = Path.Combine(images, "logo.png");
        File.WriteAllBytes(logo, [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(uiRoot, RuntimeUi.UiManifestLoader.ManifestFileName), """
            {
              "screens": [
                { "id": "main", "path": "main.xhtml", "preload": false }
              ],
              "images": [
                { "id": "logo", "path": "images/logo.png", "preload": true }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(uiRoot, "main.xhtml"), "<ui><text>Main</text></ui>");

        try
        {
            RecordingGuiHost gui = new();
            using RuntimeUi.ManagedFallbackBackend backend = new(gui);
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));

            _ = new GameUiServiceBridge(host, root);

            Assert.Equal(Path.GetFullPath(logo), Assert.Single(gui.LoadedImages));
            Assert.Equal(0, host.Documents.DocumentCount);
            Assert.Equal(0, host.Documents.StackCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 BindModel 会按 UI 文档声明的 path 从脚本模型读取值，并在 UI Update 前推送到后端。
    /// </summary>
    [Fact]
    public void BridgePushesBoundModelValuesToUiPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-gameui-model-{Guid.NewGuid():N}");
        string uiRoot = Path.Combine(root, "ui");
        _ = Directory.CreateDirectory(uiRoot);
        File.WriteAllText(Path.Combine(uiRoot, "main.xhtml"), "<ui><text>Main</text></ui>");
        try
        {
            RecordingBackend backend = new();
            using RuntimeUi.GameUiHost host = new(backend);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                new RuntimeUi.UiViewport(0, 0, 320, 240, 1f),
                RuntimeUi.UiBackendKind.ManagedFallback));
            GameUiServiceBridge bridge = new(host, root);

            ScriptUi.UiScreenHandle screen = bridge.ShowScreen("main");
            RecordingModel model = new(new ScriptUi.UiPathId(7), new ScriptUi.UiValue(99L));
            bridge.BindModel(screen, new ScriptUi.UiModelName(1), model);
            bridge.PushGameUiModels();

            Assert.Equal(new ScriptUi.UiPathId(7), model.LastPath);
            Assert.True(bridge.TryGetValue(screen, new ScriptUi.UiPathId(7), out ScriptUi.UiValue value));
            Assert.Equal(99L, value.AsInt64());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class RecordingBackend : RuntimeUi.IGameUiBackend
    {
        private RuntimeUi.UiValue _value;

        public RuntimeUi.UiDocumentHandle LastDocument { get; private set; } = new(10);

        public RuntimeUi.UiDocumentSource LastSource { get; private set; }

        public int LoadDocumentCount { get; private set; }

        public RuntimeUi.UiBackendKind Kind => RuntimeUi.UiBackendKind.ManagedFallback;

        public bool IsDirty => false;

        public bool IsAnimating => false;

        public void Initialize(in RuntimeUi.UiBackendInitializeInfo info)
        {
            info.Viewport.Validate();
        }

        public void Resize(in RuntimeUi.UiViewport viewport)
        {
            viewport.Validate();
        }

        public RuntimeUi.UiDocumentHandle LoadDocument(in RuntimeUi.UiDocumentSource source)
        {
            LoadDocumentCount++;
            LastDocument = new RuntimeUi.UiDocumentHandle(10 + LoadDocumentCount);
            LastSource = source;
            return LastDocument;
        }

        public void UnloadDocument(RuntimeUi.UiDocumentHandle document)
        {
            document.Validate();
        }

        public void SetScreenStack(ReadOnlySpan<RuntimeUi.UiScreenStackEntry> stack)
        {
            _ = stack;
        }

        public void Update(float deltaSeconds)
        {
            _ = deltaSeconds;
        }

        public void FeedPointerMove(float x, float y)
        {
            _ = x;
            _ = y;
        }

        public void FeedPointerButton(RuntimeUi.UiPointerButton button, bool isDown)
        {
            _ = button;
            _ = isDown;
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
            _ = deltaX;
            _ = deltaY;
        }

        public void FeedKey(RuntimeUi.UiKey key, bool isDown, RuntimeUi.UiKeyModifiers modifiers)
        {
            _ = key;
            _ = isDown;
            _ = modifiers;
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
            _ = text;
        }

        public RuntimeUi.UiHitResult HitTest(float x, float y)
        {
            _ = x;
            _ = y;
            return RuntimeUi.UiHitResult.None;
        }

        public void SetModelValue(RuntimeUi.UiDocumentHandle document, RuntimeUi.UiPathId path, in RuntimeUi.UiValue value)
        {
            Assert.Equal(LastDocument, document);
            Assert.Equal(new RuntimeUi.UiPathId(7), path);
            _value = value;
        }

        public bool TryGetModelValue(RuntimeUi.UiDocumentHandle document, RuntimeUi.UiPathId path, out RuntimeUi.UiValue value)
        {
            Assert.Equal(LastDocument, document);
            Assert.Equal(new RuntimeUi.UiPathId(7), path);
            value = _value;
            return true;
        }

        public int CopyModelPaths(RuntimeUi.UiDocumentHandle document, Span<RuntimeUi.UiPathId> destination)
        {
            Assert.Equal(LastDocument, document);
            if (destination.IsEmpty)
            {
                return 0;
            }

            destination[0] = new RuntimeUi.UiPathId(7);
            return 1;
        }

        public bool InvokeAction(RuntimeUi.UiDocumentHandle document, RuntimeUi.UiActionId action, in RuntimeUi.UiValue payload)
        {
            Assert.Equal(LastDocument, document);
            Assert.Equal(new RuntimeUi.UiActionId(5), action);
            _value = payload;
            return true;
        }

        public int DrainEvents(Span<RuntimeUi.UiEvent> destination)
        {
            _ = destination;
            return 0;
        }

        public void Composite(in UiPresentContext context)
        {
            _ = context;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingGuiHost : RuntimeUi.IManagedFallbackGuiHost
    {
        public List<string> LoadedImages { get; } = [];

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
            drawGui(new RecordingGuiDrawContext());
        }

        public RuntimeUi.ManagedFallbackImage LoadImage(string path)
        {
            string fullPath = Path.GetFullPath(path);
            Assert.True(File.Exists(fullPath), fullPath);
            LoadedImages.Add(fullPath);
            return new RuntimeUi.ManagedFallbackImage(1, 1, 1);
        }
    }

    private sealed class RecordingGuiDrawContext : IGuiDrawContext
    {
        public int Width => 320;

        public int Height => 240;

        public float DeltaTime => 1f / 60f;

        public bool WantsMouse => false;

        public bool WantsKeyboard => false;

        public void SetNextWindow(float x, float y, float width, float height, GuiDrawCondition condition = GuiDrawCondition.Always)
        {
        }

        public bool BeginWindow(string id, string title, GuiDrawWindowFlags flags = GuiDrawWindowFlags.None)
        {
            return true;
        }

        public void EndWindow()
        {
        }

        public void Text(string text)
        {
        }

        public void TextColored(string text, uint colorBgra)
        {
        }

        public void SameLine()
        {
        }

        public void Separator()
        {
        }

        public bool Button(string label)
        {
            return false;
        }

        public bool Checkbox(string label, ref bool value)
        {
            return false;
        }

        public void ProgressBar(float value01, string? label = null)
        {
        }

        public void ColorSwatch(string id, uint colorBgra, float size = 16)
        {
        }

        public void Image(string id, uint textureHandle, int textureWidth, int textureHeight, float width, float height, uint tintBgra = 4294967295)
        {
        }
    }

    private sealed class RecordingModel(ScriptUi.UiPathId expectedPath, ScriptUi.UiValue value) : ScriptUi.IUiModel
    {
        public ScriptUi.UiPathId LastPath { get; private set; }

        public bool TryGetValue(ScriptUi.UiPathId path, out ScriptUi.UiValue result)
        {
            LastPath = path;
            if (path == expectedPath)
            {
                result = value;
                return true;
            }

            result = default;
            return false;
        }
    }
}
