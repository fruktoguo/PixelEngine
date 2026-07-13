using PixelEngine.Rendering;
using Xunit;
using RuntimeUi = PixelEngine.UI;
using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 多 Canvas 固定容量注册表的独立屏栈、全局句柄、输入层级、事件来源与原子切场景测试。
/// </summary>
public sealed class GameUiCanvasRegistryTests
{
    /// <summary>每个 Canvas 保持独立模型状态，脚本屏幕句柄跨 Canvas 全局唯一。</summary>
    [Fact]
    public void CanvasesHaveIndependentStateAndGloballyUniqueScreenHandles()
    {
        using RegistryFixture fixture = new();
        fixture.Configure(TwoCanvasDocument());
        GameUiServiceBridge bridge = new(fixture.Registry);
        ScriptUi.UiCanvasHandle[] canvases = new ScriptUi.UiCanvasHandle[2];

        Assert.Equal(2, bridge.CopyCanvases(canvases));
        Assert.Equal(canvases[1], bridge.PrimaryCanvas);
        Assert.True(bridge.TryGetCanvas(GameUiCanvasIdentity.FromStableId(10), out ScriptUi.UiCanvasHandle lower));
        Assert.True(bridge.TryGetCanvas(GameUiCanvasIdentity.FromStableId(20), out ScriptUi.UiCanvasHandle upper));
        Assert.Equal(canvases[0], lower);
        Assert.Equal(canvases[1], upper);

        ScriptUi.UiScreenHandle lowerScreen = bridge.ShowScreen(lower, "main");
        ScriptUi.UiScreenHandle upperScreen = bridge.ShowScreen(upper, "main");
        Assert.NotEqual(default, lowerScreen);
        Assert.NotEqual(default, upperScreen);
        Assert.NotEqual(lowerScreen, upperScreen);

        ScriptUi.UiPathId path = new(7);
        bridge.SetValue(lowerScreen, path, new ScriptUi.UiValue(11L));
        bridge.SetValue(upperScreen, path, new ScriptUi.UiValue(22L));
        Assert.True(bridge.TryGetValue(lowerScreen, path, out ScriptUi.UiValue lowerValue));
        Assert.True(bridge.TryGetValue(upperScreen, path, out ScriptUi.UiValue upperValue));
        Assert.Equal(11L, lowerValue.AsInt64());
        Assert.Equal(22L, upperValue.AsInt64());
    }

    /// <summary>输入按 sorting order 从顶到底命中，并在拖拽期间保持按钮捕获。</summary>
    [Fact]
    public void InputRoutesToTopCanvasAndKeepsPointerCaptureUntilRelease()
    {
        using RegistryFixture fixture = new();
        fixture.Configure(TwoCanvasDocument());
        RecordingBackend lower = fixture.GetBackend(10);
        RecordingBackend upper = fixture.GetBackend(20);
        lower.HitResult = new RuntimeUi.UiHitResult(true, true, true, false);
        upper.HitResult = new RuntimeUi.UiHitResult(true, true, true, true);

        fixture.Registry.FeedPointerMove(40f, 20f);
        fixture.Registry.FeedPointerButton(RuntimeUi.UiPointerButton.Left, isDown: true);
        upper.HitResult = RuntimeUi.UiHitResult.None;
        fixture.Registry.FeedPointerMove(400f, 200f);
        fixture.Registry.FeedScroll(0f, 2f);
        fixture.Registry.FeedPointerButton(RuntimeUi.UiPointerButton.Left, isDown: false);

        Assert.Equal(2, lower.PointerMoveCount);
        Assert.Equal(2, upper.PointerMoveCount);
        Assert.Equal(0, lower.PointerButtonCount);
        Assert.Equal(2, upper.PointerButtonCount);
        Assert.Equal(0, lower.ScrollCount);
        Assert.Equal(1, upper.ScrollCount);
        Assert.Equal(new RuntimeUi.UiHitResult(true, true, true, false), fixture.Registry.HitTest(400f, 200f));
    }

    /// <summary>桥接事件携带来源 Canvas 与全局 Screen，而不是碰撞的后端局部句柄。</summary>
    [Fact]
    public void ScriptEventsCarryCanvasAndGlobalScreenIdentity()
    {
        using RegistryFixture fixture = new();
        fixture.Configure(TwoCanvasDocument());
        GameUiServiceBridge bridge = new(fixture.Registry);
        Assert.True(bridge.TryGetCanvas(GameUiCanvasIdentity.FromStableId(20), out ScriptUi.UiCanvasHandle canvas));
        ScriptUi.UiScreenHandle screen = bridge.ShowScreen(canvas, "main");
        RecordingBackend backend = fixture.GetBackend(20);
        ScriptUi.UiEvent received = default;
        bridge.UiEventRaised += value => received = value;

        bridge.OnGameUiEvents(canvas, [new RuntimeUi.UiEvent(
            backend.LastDocument,
            new RuntimeUi.UiElementId(3),
            new RuntimeUi.UiActionId(5),
            RuntimeUi.UiValue.FromBoolean(true))]);

        Assert.Equal(canvas, received.Canvas);
        Assert.Equal(screen, received.Screen);
        Assert.Equal(new ScriptUi.UiElementId(3), received.Element);
        Assert.Equal(new ScriptUi.UiActionId(5), received.Action);
        Assert.True(received.Payload.AsBoolean());
    }

    /// <summary>重新物化场景会使旧 Canvas/Screen 句柄失效，且新句柄不会复用旧值。</summary>
    [Fact]
    public void ReconfigureInvalidatesOldCanvasAndScreenHandles()
    {
        using RegistryFixture fixture = new();
        fixture.Configure(TwoCanvasDocument());
        GameUiServiceBridge bridge = new(fixture.Registry);
        Assert.True(bridge.TryGetCanvas(GameUiCanvasIdentity.FromStableId(10), out ScriptUi.UiCanvasHandle oldCanvas));
        ScriptUi.UiScreenHandle oldScreen = bridge.ShowScreen(oldCanvas, "main");

        fixture.Configure(TwoCanvasDocument());

        Assert.True(bridge.TryGetCanvas(GameUiCanvasIdentity.FromStableId(10), out ScriptUi.UiCanvasHandle newCanvas));
        Assert.NotEqual(oldCanvas, newCanvas);
        Assert.Equal(default, bridge.ShowScreen(oldCanvas, "main"));
        Assert.False(bridge.TryGetValue(oldScreen, new ScriptUi.UiPathId(1), out _));
        ScriptUi.UiScreenHandle newScreen = bridge.ShowScreen(newCanvas, "main");
        Assert.NotEqual(oldScreen, newScreen);
    }

    /// <summary>新场景任一 Canvas 创建失败时，旧注册表保持可用且 staged host 被释放。</summary>
    [Fact]
    public void ConfigureIsAtomicWhenAHostFactoryFails()
    {
        using RegistryFixture fixture = new();
        fixture.Configure(OneCanvasDocument());
        Assert.True(fixture.Registry.TryGetCanvas(
            GameUiCanvasIdentity.FromStableId(10),
            out ScriptUi.UiCanvasHandle original));
        fixture.FailStableId = 20;

        _ = Assert.Throws<InvalidOperationException>(() => fixture.Configure(TwoCanvasDocument()));

        Assert.Equal(1, fixture.Registry.Count);
        Assert.True(fixture.Registry.TryGetCanvas(GameUiCanvasIdentity.FromStableId(10), out ScriptUi.UiCanvasHandle retained));
        Assert.Equal(original, retained);
        Assert.False(fixture.Registry.TryGetCanvas(GameUiCanvasIdentity.FromStableId(20), out _));
        Assert.Contains(fixture.CreatedBackends, backend => backend.StableId == 10 && backend.Disposed);
    }

    /// <summary>显式 Canvas 全 disabled 时服务是安全 no-op，Primary 为 None。</summary>
    [Fact]
    public void AllDisabledSceneHasNoPrimaryAndSafeNoOpService()
    {
        using RegistryFixture fixture = new();
        fixture.Configure(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "disabled",
            Entities = [CanvasEntity(10, 0, primary: true, enabled: false)],
        });
        GameUiServiceBridge bridge = new(fixture.Registry);

        Assert.Equal(0, fixture.Registry.Count);
        Assert.Equal(default, bridge.PrimaryCanvas);
        Assert.Equal(default, bridge.ShowScreen("main"));
        Assert.Equal(RuntimeUi.UiHitResult.None, fixture.Registry.HitTest(1f, 1f));
    }

    /// <summary>稳定 asset id 暂时无法解析时使用入盘 logical path，二者都缺失才明确失败。</summary>
    [Fact]
    public void ManifestAssetIdUsesLogicalPathFallback()
    {
        using RegistryFixture fixture = new();
        EngineSceneEntityDocument canvas = CanvasEntity(10, 0, primary: true, enabled: true);
        canvas = new EngineSceneEntityDocument
        {
            StableId = canvas.StableId,
            Name = canvas.Name,
            Enabled = canvas.Enabled,
            CanvasScaler = canvas.CanvasScaler,
            WebCanvas = new EngineSceneWebCanvasDocument
            {
                ManifestAssetId = "asset_manifest_missing_from_database",
                ManifestPath = "ui/ui-manifest.json",
                Enabled = true,
                Primary = true,
            },
        };
        fixture.Configure(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "manifest-fallback",
            Entities = [canvas],
        });
        GameUiServiceBridge service = new(fixture.Registry);

        Assert.NotEqual(default, service.ShowScreen("main"));
    }

    /// <summary>稳态 update/hit/input 路由不在托管堆分配。</summary>
    [Fact]
    public void SteadyStateRoutingDoesNotAllocateManagedMemory()
    {
        using RegistryFixture fixture = new();
        fixture.Configure(TwoCanvasDocument());
        fixture.GetBackend(10).HitResult = new RuntimeUi.UiHitResult(true, false, true, false);
        fixture.GetBackend(20).HitResult = new RuntimeUi.UiHitResult(true, true, true, true);
        fixture.Registry.FeedPointerMove(1f, 1f);
        _ = fixture.Registry.HitTest(1f, 1f);
        fixture.Registry.Update(1f / 60f);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            fixture.Registry.FeedPointerMove(i & 63, i & 31);
            _ = fixture.Registry.HitTest(i & 63, i & 31);
            fixture.Registry.Update(1f / 60f);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private static EngineSceneDocument OneCanvasDocument()
    {
        return new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "one",
            Entities = [CanvasEntity(10, 0, primary: true)],
        };
    }

    private static EngineSceneDocument TwoCanvasDocument()
    {
        return new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "two",
            Entities =
            [
                CanvasEntity(20, 100, primary: true),
                CanvasEntity(10, -10),
            ],
        };
    }

    private static EngineSceneEntityDocument CanvasEntity(
        int stableId,
        int sortingOrder,
        bool primary = false,
        bool enabled = true)
    {
        return new EngineSceneEntityDocument
        {
            StableId = stableId,
            Name = $"Canvas {stableId}",
            Enabled = true,
            WebCanvas = new EngineSceneWebCanvasDocument
            {
                Enabled = enabled,
                SortingOrder = sortingOrder,
                Primary = primary,
            },
            CanvasScaler = new EngineSceneCanvasScalerDocument
            {
                ScaleMode = RuntimeUi.UiScaleMode.ScaleWithScreenSize,
                ReferenceWidth = 1280,
                ReferenceHeight = 720,
                ScreenMatchMode = RuntimeUi.UiScreenMatchMode.MatchWidthOrHeight,
                MatchWidthOrHeight = 0.5f,
            },
        };
    }

    private sealed class RegistryFixture : IDisposable
    {
        private readonly string _root;
        private readonly Dictionary<int, RecordingBackend> _activeBackends = [];

        internal RegistryFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), $"pixelengine-canvas-registry-{Guid.NewGuid():N}");
            string uiRoot = Path.Combine(_root, "ui");
            _ = Directory.CreateDirectory(uiRoot);
            File.WriteAllText(Path.Combine(uiRoot, "main.xhtml"), "<ui><text>Main</text></ui>");
            File.WriteAllText(
                Path.Combine(uiRoot, RuntimeUi.UiManifestLoader.ManifestFileName),
                "{\"screens\":[{\"id\":\"main\",\"path\":\"main.xhtml\",\"preload\":false}]}");
            Registry = new GameUiCanvasRegistry(_root, CreateHost, maxCanvases: 4, maxScreenInstances: 16);
        }

        internal GameUiCanvasRegistry Registry { get; }

        internal int? FailStableId { get; set; }

        internal List<RecordingBackend> CreatedBackends { get; } = [];

        internal void Configure(EngineSceneDocument document)
        {
            _activeBackends.Clear();
            Registry.Configure(EngineSceneCanvasResolver.Resolve(document));
        }

        internal RecordingBackend GetBackend(int stableId)
        {
            return _activeBackends[stableId];
        }

        public void Dispose()
        {
            Registry.Dispose();
            Directory.Delete(_root, recursive: true);
        }

        private RuntimeUi.GameUiHost CreateHost(EngineSceneCanvasDefinition definition)
        {
            if (definition.StableId == FailStableId)
            {
                throw new InvalidOperationException($"factory failure: {definition.StableId}");
            }

            RecordingBackend backend = new(definition.StableId);
            CreatedBackends.Add(backend);
            _activeBackends[definition.StableId] = backend;
            RuntimeUi.GameUiHost host = new(backend, new RuntimeUi.GameUiHostOptions(true, 16, 8));
            RuntimeUi.UiDisplayMetrics display = new(1280, 720, 1f, 1f, 96f, 0, 0);
            host.Initialize(new RuntimeUi.UiBackendInitializeInfo(
                display,
                definition.ScalerSettings,
                RuntimeUi.UiBackendKind.RmlUi));
            return host;
        }
    }

    private sealed class RecordingBackend(int stableId) : RuntimeUi.IGameUiBackend
    {
        private readonly Dictionary<(int Document, int Path), RuntimeUi.UiValue> _values = [];
        private int _nextDocument = 10;

        internal int StableId { get; } = stableId;

        internal RuntimeUi.UiHitResult HitResult { get; set; }

        internal int PointerMoveCount { get; private set; }

        internal int PointerButtonCount { get; private set; }

        internal int ScrollCount { get; private set; }

        internal bool Disposed { get; private set; }

        internal RuntimeUi.UiDocumentHandle LastDocument { get; private set; }

        public RuntimeUi.UiBackendKind Kind => RuntimeUi.UiBackendKind.RmlUi;

        public bool IsDirty => false;

        public bool IsAnimating => false;

        public void Initialize(in RuntimeUi.UiBackendInitializeInfo info)
        {
            info.DisplayMetrics.Validate();
        }

        public void Resize(in RuntimeUi.UiViewport viewport)
        {
            viewport.Validate();
        }

        public RuntimeUi.UiDocumentHandle LoadDocument(in RuntimeUi.UiDocumentSource source)
        {
            _ = source;
            LastDocument = new RuntimeUi.UiDocumentHandle(_nextDocument++);
            return LastDocument;
        }

        public void UnloadDocument(RuntimeUi.UiDocumentHandle document)
        {
            _ = document;
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
            PointerMoveCount++;
        }

        public void FeedPointerButton(RuntimeUi.UiPointerButton button, bool isDown)
        {
            _ = button;
            _ = isDown;
            PointerButtonCount++;
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
            _ = deltaX;
            _ = deltaY;
            ScrollCount++;
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
            return HitResult;
        }

        public void SetModelValue(
            RuntimeUi.UiDocumentHandle document,
            RuntimeUi.UiPathId path,
            in RuntimeUi.UiValue value)
        {
            _values[(document.Value, path.Value)] = value;
        }

        public bool TryGetModelValue(
            RuntimeUi.UiDocumentHandle document,
            RuntimeUi.UiPathId path,
            out RuntimeUi.UiValue value)
        {
            return _values.TryGetValue((document.Value, path.Value), out value);
        }

        public int CopyModelPaths(RuntimeUi.UiDocumentHandle document, Span<RuntimeUi.UiPathId> destination)
        {
            _ = document;
            _ = destination;
            return 0;
        }

        public bool InvokeAction(
            RuntimeUi.UiDocumentHandle document,
            RuntimeUi.UiActionId action,
            in RuntimeUi.UiValue payload)
        {
            _ = document;
            _ = action;
            _ = payload;
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
            Disposed = true;
        }
    }
}
