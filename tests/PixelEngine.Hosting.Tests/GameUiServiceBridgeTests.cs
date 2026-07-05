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
            bridge.OnGameUiEvents([
                new RuntimeUi.UiEvent(
                    backend.LastDocument,
                    new RuntimeUi.UiElementId(3),
                    new RuntimeUi.UiActionId(5),
                    RuntimeUi.UiValue.FromBoolean(true)),
            ]);

            Assert.True(found);
            Assert.Equal(42L, value.AsInt64());
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
