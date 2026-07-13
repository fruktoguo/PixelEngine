using System.Diagnostics;
using PixelEngine.Rendering;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Scene View 的 Web Canvas authoring 预览层。它复用 Editor 窗口的 GL context，
/// 但持有独立 UI host、屏栈与事件队列，绝不把预览 action 派发到游戏脚本。
/// </summary>
internal sealed class SceneWebCanvasAuthoringPreview : IUiPresentLayer, IDisposable
{
    private const int EventDrainCapacity = 64;
    private readonly EditorSceneModel _scene;
    private readonly string _contentRoot;
    private readonly RenderWindow _window;
    private readonly Func<string, string?>? _manifestAssetResolver;
    private readonly IDisposable _registration;
    private SceneWebCanvasPreviewRequest _request;
    private SceneWebCanvasPreviewMaterializationKey? _materializedKey;
    private SceneWebCanvasPreviewMaterializationKey? _failedKey;
    private string _failedDiagnostic = string.Empty;
    private GameUiHost? _host;
    private RmlUiBackend? _backend;
    private ColorRenderTarget? _target;
    private string _screenId = string.Empty;
    private string _materializationDiagnostic = string.Empty;
    private long _assetRevision;
    private long _snapshotRevision;
    private long _suppressedEventCount;
    private long _lastUpdateTimestamp;
    private bool _pointerInside;
    private bool _disposed;

    public SceneWebCanvasAuthoringPreview(
        EditorSceneModel scene,
        string contentRoot,
        RenderWindow window,
        RenderPipeline pipeline,
        Func<string, string?>? manifestAssetResolver = null)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _contentRoot = NormalizeDirectory(contentRoot);
        _window = window ?? throw new ArgumentNullException(nameof(window));
        ArgumentNullException.ThrowIfNull(pipeline);
        _manifestAssetResolver = manifestAssetResolver;
        _registration = pipeline.RegisterUiLayer(
            UiPresentSurface.WindowFramebuffer,
            UiPresentLayerOrders.Editor - 1,
            this);
    }

    /// <summary>最近一次完成或失败的 authoring 预览快照。</summary>
    public SceneWebCanvasPreviewSnapshot Snapshot { get; private set; }

    /// <summary>
    /// 提交 Scene View 本帧的选择与 hover 坐标。由于 Editor ImGui 层晚于本层绘制，
    /// 请求会在下一 present 帧生效，纹理仍在同一 GL context 内同步可见。
    /// </summary>
    public void Request(
        int? stableId,
        bool visible,
        bool pointerInside,
        float presentationX,
        float presentationY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool validPointer = pointerInside &&
            float.IsFinite(presentationX) &&
            float.IsFinite(presentationY);
        _request = new SceneWebCanvasPreviewRequest(
            stableId,
            visible && stableId.HasValue,
            validPointer,
            validPointer ? presentationX : 0f,
            validPointer ? presentationY : 0f);
    }

    /// <summary>使 manifest、XHTML、CSS、字体或图片变更在下一帧重新物化。</summary>
    public void InvalidateAssets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _assetRevision = checked(_assetRevision + 1);
    }

    /// <inheritdoc />
    public void Present(in UiPresentContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SceneWebCanvasPreviewRequest request = _request;
        if (!request.Visible || !request.StableId.HasValue)
        {
            ReleaseMaterialization();
            SetSnapshot(default);
            return;
        }

        SceneWebCanvasPreviewDescriptor descriptor;
        try
        {
            descriptor = SceneWebCanvasPreviewDescriptorResolver.Resolve(
                _scene,
                request.StableId.Value,
                _contentRoot,
                _manifestAssetResolver,
                _assetRevision);
        }
        catch (Exception exception) when (IsAuthoringPreviewFailure(exception))
        {
            ReleaseMaterialization();
            SetSnapshot(new SceneWebCanvasPreviewSnapshot(
                Visible: true,
                Ready: false,
                TextureHandle: 0,
                StableId: request.StableId.Value,
                CanvasMetrics: default,
                ScreenId: string.Empty,
                Diagnostic: $"Web Canvas 预览配置无效：{exception.Message}",
                Revision: 0,
                SuppressedEventCount: _suppressedEventCount));
            return;
        }

        if (!descriptor.CanRender)
        {
            ReleaseMaterialization();
            SetSnapshot(new SceneWebCanvasPreviewSnapshot(
                Visible: true,
                Ready: false,
                TextureHandle: 0,
                StableId: descriptor.StableId,
                CanvasMetrics: descriptor.CanvasMetrics,
                ScreenId: string.Empty,
                Diagnostic: descriptor.Diagnostic,
                Revision: 0,
                SuppressedEventCount: _suppressedEventCount));
            return;
        }

        if (_failedKey == descriptor.Key)
        {
            SetSnapshot(new SceneWebCanvasPreviewSnapshot(
                Visible: true,
                Ready: false,
                TextureHandle: 0,
                StableId: descriptor.StableId,
                CanvasMetrics: descriptor.CanvasMetrics,
                ScreenId: string.Empty,
                Diagnostic: _failedDiagnostic,
                Revision: 0,
                SuppressedEventCount: _suppressedEventCount));
            return;
        }

        try
        {
            if (_materializedKey != descriptor.Key || _host is null || _target is null)
            {
                Materialize(in descriptor);
            }

            UiCanvasMetrics canvasMetrics = descriptor.CanvasMetrics;
            FeedHover(in request, in canvasMetrics);
            _host!.Update(ResolveDeltaSeconds());
            DrainSuppressedEvents();
            _target!.Clear();
            int targetY = Math.Max(0, context.FramebufferHeight - _target.Height);
            UiPresentContext previewContext = context.WithTarget(
                new UiPresentTarget(0, targetY, _target.Width, _target.Height, 1f));
            _host.Composite(in previewContext);
            SetSnapshot(new SceneWebCanvasPreviewSnapshot(
                Visible: true,
                Ready: true,
                TextureHandle: _target.Handle,
                StableId: descriptor.StableId,
                CanvasMetrics: descriptor.CanvasMetrics,
                ScreenId: _screenId,
                Diagnostic: _materializationDiagnostic,
                Revision: checked(_snapshotRevision + 1),
                SuppressedEventCount: _suppressedEventCount));
        }
        catch (Exception exception) when (IsAuthoringPreviewFailure(exception))
        {
            string diagnostic =
                $"真实 XHTML authoring preview 初始化或渲染失败：{exception.GetType().Name}: {exception.Message}";
            ReleaseMaterialization();
            _failedKey = descriptor.Key;
            _failedDiagnostic = diagnostic;
            SetSnapshot(new SceneWebCanvasPreviewSnapshot(
                Visible: true,
                Ready: false,
                TextureHandle: 0,
                StableId: descriptor.StableId,
                CanvasMetrics: descriptor.CanvasMetrics,
                ScreenId: string.Empty,
                Diagnostic: diagnostic,
                Revision: 0,
                SuppressedEventCount: _suppressedEventCount));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _registration.Dispose();
        ReleaseMaterialization();
        _disposed = true;
    }

    private void Materialize(in SceneWebCanvasPreviewDescriptor descriptor)
    {
        ReleaseMaterialization();
        if (!RmlUiNativeProfileGate.CanUseNativeRenderer(
                _window.Backend,
                _window.Capabilities,
                out string? profileDiagnostic))
        {
            throw new NotSupportedException(profileDiagnostic ?? "当前 GL profile 不能运行 RmlUi authoring preview。");
        }

        if (!RmlUiNativeInfo.TryQuery(out RmlUiNativeProbe probe))
        {
            throw new DllNotFoundException($"RmlUi native 不可用：{probe.Error ?? "unknown"}。");
        }

        UiManifest manifest = UiManifestLoader.Load(descriptor.ManifestPath);
        UiManifestScreen screen = ResolveScreen(
            manifest,
            descriptor.InitialScreenId,
            out string diagnostic);
        RmlUiBackend backend = new(_window);
        GameUiHost host = new(backend);
        ColorRenderTarget? target = null;
        try
        {
            // 与 Engine.InitializeGameUiHost 完全相同：字体从 content/ui/fonts 解析，
            // manifest 即使位于子目录也不能让 Scene 预览偷偷换一套字体来源。
            UiFontSelection font = new FontEngine(
                new FontEngineOptions(Path.Combine(_contentRoot, "ui"))).Resolve();
            host.Initialize(new UiBackendInitializeInfo(
                descriptor.DisplayMetrics,
                descriptor.ScalerSettings,
                UiBackendKind.RmlUi,
                font));
            PreloadManifest(host, manifest);
            UiDocumentSource source = screen.ToDocumentSource();
            _ = host.ShowScreen(screen.ScreenId, in source);
            target = new ColorRenderTarget(
                _window.Gl,
                descriptor.CanvasMetrics.PresentationWidth,
                descriptor.CanvasMetrics.PresentationHeight);
        }
        catch
        {
            target?.Dispose();
            host.Dispose();
            throw;
        }

        _backend = backend;
        _host = host;
        _target = target;
        _screenId = screen.Id;
        _materializationDiagnostic = diagnostic;
        _materializedKey = descriptor.Key;
        _failedKey = null;
        _failedDiagnostic = string.Empty;
        _lastUpdateTimestamp = 0;
        _pointerInside = false;
    }

    private static UiManifestScreen ResolveScreen(
        UiManifest manifest,
        string? initialScreenId,
        out string diagnostic)
    {
        if (!string.IsNullOrWhiteSpace(initialScreenId))
        {
            diagnostic = string.Empty;
            return manifest.GetRequiredScreen(initialScreenId.Trim());
        }

        ReadOnlySpan<UiManifestScreen> screens = manifest.Screens;
        if (screens.IsEmpty)
        {
            throw new InvalidDataException("UI manifest 没有可供 Scene View 显示的 screen。");
        }

        diagnostic =
            $"Authoring preview 临时显示 manifest 第一屏 '{screens[0].Id}'；Initial Screen 为空，运行时不会自动显示该屏。";
        return screens[0];
    }

    private static void PreloadManifest(GameUiHost host, UiManifest manifest)
    {
        ReadOnlySpan<UiManifestScreen> screens = manifest.Screens;
        for (int i = 0; i < screens.Length; i++)
        {
            if (!screens[i].Preload || host.Documents.TryGetDocument(screens[i].ScreenId, out _))
            {
                continue;
            }

            UiDocumentSource source = screens[i].ToDocumentSource();
            _ = host.LoadDocument(screens[i].ScreenId, in source);
        }

        // Scene authoring 必须与真实 RmlUi 图片路径一致；这里预载清单内全部图片，
        // 避免仅因 runtime preload 标志为 false 而让编辑态出现假缺图。
        ReadOnlySpan<UiManifestImage> images = manifest.Images;
        for (int i = 0; i < images.Length; i++)
        {
            _ = host.PreloadImage(images[i].FullPath);
        }
    }

    private void FeedHover(in SceneWebCanvasPreviewRequest request, in UiCanvasMetrics metrics)
    {
        if (request.PointerInside &&
            request.PresentationX >= 0f &&
            request.PresentationY >= 0f &&
            request.PresentationX < metrics.PresentationWidth &&
            request.PresentationY < metrics.PresentationHeight)
        {
            _host!.FeedPointerMove(request.PresentationX, request.PresentationY);
            _pointerInside = true;
            return;
        }

        if (_pointerInside)
        {
            // GameUiHost 会拒绝 surface 外坐标；authoring preview 直接给隔离 backend
            // 一个离开 Canvas 的 hover 点，以清理 CSS :hover，不发送任何 button/key/action。
            _backend!.FeedPointerMove(-1f, -1f);
            _pointerInside = false;
        }
    }

    private void DrainSuppressedEvents()
    {
        Span<UiEvent> events = stackalloc UiEvent[EventDrainCapacity];
        int count;
        do
        {
            count = _host!.DrainEvents(events);
            _suppressedEventCount = checked(_suppressedEventCount + count);
        }
        while (count == events.Length);
    }

    private float ResolveDeltaSeconds()
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastUpdateTimestamp == 0)
        {
            _lastUpdateTimestamp = now;
            return 1f / 60f;
        }

        double seconds = (now - _lastUpdateTimestamp) / (double)Stopwatch.Frequency;
        _lastUpdateTimestamp = now;
        return (float)Math.Clamp(seconds, 0.0, 0.1);
    }

    private void SetSnapshot(in SceneWebCanvasPreviewSnapshot snapshot)
    {
        _snapshotRevision = Math.Max(_snapshotRevision, snapshot.Revision);
        Snapshot = snapshot;
    }

    private void ReleaseMaterialization()
    {
        _target?.Dispose();
        _target = null;
        _host?.Dispose();
        _host = null;
        _backend = null;
        _materializedKey = null;
        _screenId = string.Empty;
        _materializationDiagnostic = string.Empty;
        _lastUpdateTimestamp = 0;
        _pointerInside = false;
    }

    private static bool IsAuthoringPreviewFailure(Exception exception)
    {
        return exception is ArgumentException or
            InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            DllNotFoundException or
            EntryPointNotFoundException or
            BadImageFormatException;
    }

    private static string NormalizeDirectory(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}

/// <summary>将选中 GameObject 的 Canvas/Scaler 解析为可缓存的 authoring preview 描述。</summary>
internal static class SceneWebCanvasPreviewDescriptorResolver
{
    private const int DefaultPresentationWidth = 1280;
    private const int DefaultPresentationHeight = 720;
    private const int MaximumPresentationAxis = 2048;

    public static SceneWebCanvasPreviewDescriptor Resolve(
        EditorSceneModel scene,
        int stableId,
        string contentRoot,
        Func<string, string?>? manifestAssetResolver,
        long assetRevision)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentOutOfRangeException.ThrowIfNegative(assetRevision);
        EditorGameObject gameObject = scene.Get(stableId);
        EditorWebCanvasComponent canvas = gameObject.WebCanvas ??
            throw new InvalidOperationException("选中对象没有 Canvas (Web) 组件。");
        UiCanvasScalerSettings settings = gameObject.CanvasScaler?.Settings ?? UiCanvasScalerSettings.Default;
        (int width, int height) = ResolvePresentationSize(in settings);
        UiDisplayMetrics display = new(width, height, 1f, 1f, null, 0, 0);
        UiCanvasMetrics metrics = UiCanvasScaleResolver.Resolve(in settings, in display);
        bool effectiveEnabled = canvas.Enabled && ResolveEffectiveEnabled(scene, gameObject);
        if (!effectiveEnabled)
        {
            return new SceneWebCanvasPreviewDescriptor(
                stableId,
                CanRender: false,
                ManifestPath: string.Empty,
                NormalizeOptional(canvas.InitialScreenId),
                settings,
                display,
                metrics,
                default,
                "Canvas (Web) 自身或父级 disabled；Scene View 保持 runtime 一致，不渲染该预览。");
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));
        string manifestPath = ResolveManifestPath(
            root,
            NormalizeOptional(canvas.ManifestAssetId),
            NormalizeOptional(canvas.ManifestPath),
            manifestAssetResolver);
        FileInfo manifestFile = new(manifestPath);
        if (!manifestFile.Exists)
        {
            return new SceneWebCanvasPreviewDescriptor(
                stableId,
                CanRender: false,
                manifestPath,
                NormalizeOptional(canvas.InitialScreenId),
                settings,
                display,
                metrics,
                default,
                $"找不到 Web Canvas manifest：{manifestPath}");
        }

        SceneWebCanvasPreviewMaterializationKey key = new(
            scene.SceneGeneration,
            stableId,
            manifestPath,
            NormalizeOptional(canvas.InitialScreenId),
            settings,
            width,
            height,
            assetRevision,
            manifestFile.LastWriteTimeUtc.Ticks,
            manifestFile.Length);
        return new SceneWebCanvasPreviewDescriptor(
            stableId,
            CanRender: true,
            manifestPath,
            NormalizeOptional(canvas.InitialScreenId),
            settings,
            display,
            metrics,
            key,
            string.Empty);
    }

    internal static (int Width, int Height) ResolvePresentationSize(in UiCanvasScalerSettings settings)
    {
        if (settings.ScaleMode != UiScaleMode.ScaleWithScreenSize)
        {
            return (DefaultPresentationWidth, DefaultPresentationHeight);
        }

        double referenceWidth = settings.ReferenceWidth;
        double referenceHeight = settings.ReferenceHeight;
        double maximum = Math.Max(referenceWidth, referenceHeight);
        double factor = maximum > MaximumPresentationAxis
            ? MaximumPresentationAxis / maximum
            : 1.0;
        int width = Math.Max(1, (int)Math.Round(referenceWidth * factor, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(referenceHeight * factor, MidpointRounding.AwayFromZero));
        return (width, height);
    }

    internal static string ResolveManifestPath(
        string contentRoot,
        string? manifestAssetId,
        string? manifestPath,
        Func<string, string?>? manifestAssetResolver)
    {
        string? resolved = null;
        if (manifestAssetId is not null)
        {
            resolved = manifestAssetResolver?.Invoke(manifestAssetId);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                resolved = manifestPath;
            }

            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new FileNotFoundException(
                    $"找不到 Web Canvas manifest asset id：{manifestAssetId}，且场景没有 manifestPath 回退。");
            }
        }
        else
        {
            resolved = manifestPath ?? Path.Combine("ui", UiManifestLoader.ManifestFileName);
        }

        string fullPath = Path.IsPathRooted(resolved)
            ? Path.GetFullPath(resolved)
            : Path.GetFullPath(Path.Combine(contentRoot, resolved));
        EnsureUnderRoot(contentRoot, fullPath);
        return fullPath;
    }

    private static bool ResolveEffectiveEnabled(EditorSceneModel scene, EditorGameObject gameObject)
    {
        EditorGameObject current = gameObject;
        int remaining = scene.Count + 1;
        while (remaining-- > 0)
        {
            if (!current.Enabled)
            {
                return false;
            }

            if (!current.ParentId.HasValue)
            {
                return true;
            }

            current = scene.Get(current.ParentId.Value);
        }

        throw new InvalidOperationException("Scene 层级包含循环，无法解析 Canvas effective enabled。");
    }

    private static void EnsureUnderRoot(string contentRoot, string fullPath)
    {
        string rootWithSeparator =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot)) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidDataException($"Web Canvas manifest 逃逸 content 根目录：{fullPath}");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal readonly record struct SceneWebCanvasPreviewDescriptor(
    int StableId,
    bool CanRender,
    string ManifestPath,
    string? InitialScreenId,
    UiCanvasScalerSettings ScalerSettings,
    UiDisplayMetrics DisplayMetrics,
    UiCanvasMetrics CanvasMetrics,
    SceneWebCanvasPreviewMaterializationKey Key,
    string Diagnostic);

internal readonly record struct SceneWebCanvasPreviewMaterializationKey(
    long SceneGeneration,
    int StableId,
    string ManifestPath,
    string? InitialScreenId,
    UiCanvasScalerSettings ScalerSettings,
    int PresentationWidth,
    int PresentationHeight,
    long AssetRevision,
    long ManifestWriteTimeUtcTicks,
    long ManifestLength);

internal readonly record struct SceneWebCanvasPreviewRequest(
    int? StableId,
    bool Visible,
    bool PointerInside,
    float PresentationX,
    float PresentationY);

internal readonly record struct SceneWebCanvasPreviewSnapshot(
    bool Visible,
    bool Ready,
    uint TextureHandle,
    int StableId,
    UiCanvasMetrics CanvasMetrics,
    string ScreenId,
    string Diagnostic,
    long Revision,
    long SuppressedEventCount);
