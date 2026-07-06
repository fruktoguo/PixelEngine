using BenchmarkDotNet.Attributes;
using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.UI;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 游戏大 UI 相位稳态零分配基准。
/// </summary>
[MemoryDiagnoser]
public class GameUiAllocationBenchmarks
{
    private readonly StaticGameUiBackend _phaseBackend = new();
    private readonly StaticGameUiBackend _cleanBackend = new();
    private readonly NoPointerInputSource _noPointerInput = new();
    private readonly NoopGuiDrawContext _gui = new();
    private readonly GameUiHost _phaseHost;
    private readonly GameUiHost _cleanHost;
    private readonly GameUiPhaseDriver _driver;
    private readonly Engine _engine;
    private readonly UiInputRouter _router;

    /// <summary>
    /// 创建 UI allocation benchmark fixture。
    /// </summary>
    public GameUiAllocationBenchmarks()
    {
        _phaseHost = new GameUiHost(_phaseBackend);
        _phaseHost.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 180, 1f), UiBackendKind.ManagedFallback));
        _driver = new GameUiPhaseDriver(_phaseHost, eventCapacity: 4);
        _engine = new EngineBuilder()
            .WithWorkerCount(1)
            .UseHeadless()
            .AddPhaseDriver(_driver)
            .Build();

        _cleanHost = new GameUiHost(_cleanBackend);
        _cleanHost.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 180, 1f), UiBackendKind.ManagedFallback));
        _router = new UiInputRouter(_cleanHost, _noPointerInput, keyCapacity: 4, textCapacity: 4);
    }

    /// <summary>
    /// 静态 UI 逻辑相位：无 model push、无事件、无动画，仅推进后端 update 与固定 drain 缓冲。
    /// </summary>
    [Benchmark]
    public long RunStaticUiPhaseFrame()
    {
        _ = _engine.RunOneTick(realDeltaSeconds: 1.0 / 60.0);
        return _driver.TotalDrainedEventCount + _phaseBackend.UpdateCount;
    }

    /// <summary>
    /// 静态无脏 UI present 层：应跳过后端绘制/光栅化并保持 ui.paint=0。
    /// </summary>
    [Benchmark]
    public double CompositeCleanFrameSkip()
    {
        _cleanHost.Composite(default);
        return _cleanHost.LastPaintMilliseconds;
    }

    /// <summary>
    /// ManagedFallback 静态无脏 GUI 路径：应跳过托管控件绘制。
    /// </summary>
    [Benchmark]
    public double DrawGuiCleanFrameSkip()
    {
        _cleanHost.DrawGui(_gui);
        return _cleanHost.LastPaintMilliseconds;
    }

    /// <summary>
    /// 空闲输入泵：无指针、无文本、无按键时不产生临时分配。
    /// </summary>
    [Benchmark]
    public UiInputCapture PumpIdleInput()
    {
        return _router.Pump();
    }

    /// <summary>
    /// 释放基准持有的引擎与 UI 后端。
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _engine.Dispose();
        _phaseHost.Dispose();
        _cleanHost.Dispose();
    }

    private sealed class StaticGameUiBackend : IGameUiBackend
    {
        public long UpdateCount { get; private set; }

        public UiBackendKind Kind => UiBackendKind.ManagedFallback;

        public bool IsDirty => false;

        public bool IsAnimating => false;

        public void Initialize(in UiBackendInitializeInfo info)
        {
        }

        public void Resize(in UiViewport viewport)
        {
        }

        public UiDocumentHandle LoadDocument(in UiDocumentSource source)
        {
            return new UiDocumentHandle(1);
        }

        public void UnloadDocument(UiDocumentHandle document)
        {
        }

        public void SetScreenStack(ReadOnlySpan<UiScreenStackEntry> stack)
        {
        }

        public void Update(float deltaSeconds)
        {
            UpdateCount++;
        }

        public void FeedPointerMove(float x, float y)
        {
        }

        public void FeedPointerButton(UiPointerButton button, bool isDown)
        {
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
        }

        public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
        {
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
        }

        public UiHitResult HitTest(float x, float y)
        {
            return UiHitResult.None;
        }

        public void SetModelValue(UiDocumentHandle document, UiPathId path, in UiValue value)
        {
        }

        public bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value)
        {
            value = default;
            return false;
        }

        public int CopyModelPaths(UiDocumentHandle document, Span<UiPathId> destination)
        {
            return 0;
        }

        public bool InvokeAction(UiDocumentHandle document, UiActionId action, in UiValue payload)
        {
            return false;
        }

        public int DrainEvents(Span<UiEvent> destination)
        {
            return 0;
        }

        public void Composite(in UiPresentContext context)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoPointerInputSource : IUiInputSource
    {
        public bool TryGetPointer(out UiPointerState state)
        {
            state = default;
            return false;
        }

        public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
        {
            modifiers = UiKeyModifiers.None;
            return 0;
        }

        public int CaptureText(Span<char> destination)
        {
            return 0;
        }
    }

    private sealed class NoopGuiDrawContext : IGuiDrawContext
    {
        public int Width => 320;

        public int Height => 180;

        public float DeltaTime => 1f / 60f;

        public bool WantsMouse => false;

        public bool WantsKeyboard => false;

        public void SetNextWindow(float x, float y, float width, float height, GuiDrawCondition condition = GuiDrawCondition.Always)
        {
        }

        public bool BeginWindow(string id, string title, GuiDrawWindowFlags flags = GuiDrawWindowFlags.None)
        {
            return false;
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

        public void ColorSwatch(string id, uint colorBgra, float size = 16f)
        {
        }

        public void Image(string id, uint textureHandle, int textureWidth, int textureHeight, float width, float height, uint tintBgra = 0xFF_FF_FF_FF)
        {
        }
    }
}
