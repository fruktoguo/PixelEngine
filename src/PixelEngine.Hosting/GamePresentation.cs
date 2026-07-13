using System.Numerics;
using PixelEngine.Rendering;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// Game presentation 尺寸的来源；只描述最终游戏画布，不改变固定内部 world/camera surface。
/// </summary>
public enum GamePresentationSource
{
    /// <summary>Player Settings 的默认 presentation 尺寸。</summary>
    PlayerDefault,

    /// <summary>Editor Game View 的 Free Aspect。</summary>
    EditorFreeAspect,

    /// <summary>Editor Game View 的固定宽高比。</summary>
    EditorAspectRatio,

    /// <summary>Editor Game View 的固定或自定义像素分辨率。</summary>
    EditorFixedResolution,
}

/// <summary>
/// 外部宿主提交的 pending presentation 请求。Hosting 只在下一次 render 前帧边界读取并提交。
/// </summary>
/// <param name="Width">请求的完整 presentation 宽度。</param>
/// <param name="Height">请求的完整 presentation 高度。</param>
/// <param name="Source">请求来源。</param>
/// <param name="RequestRevision">宿主侧单调请求版本。</param>
public readonly record struct GamePresentationOverride(
    int Width,
    int Height,
    GamePresentationSource Source,
    long RequestRevision)
{
    /// <summary>请求是否包含有限可执行的正尺寸与非负 revision。</summary>
    public bool IsValid => Width > 0 && Height > 0 && RequestRevision >= 0 && Enum.IsDefined(Source);
}

/// <summary>
/// Editor 等外部宿主的 presentation override。实现只发布 pending 值，不直接 resize Rendering 资源。
/// </summary>
public interface IGamePresentationOverride
{
    /// <summary>
    /// 读取最近一份 pending presentation 请求；没有覆盖时返回 false，Hosting 使用 Player Default。
    /// </summary>
    /// <param name="request">当前 pending 请求。</param>
    /// <returns>存在显式覆盖时为 true。</returns>
    bool TryGetPendingPresentation(out GamePresentationOverride request);
}

/// <summary>
/// 运行时 Web Canvas 合成策略。Editor 用它显式区分 Edit 与 Play/Paused，不能用 target provider 缺失代替。
/// </summary>
public interface IGameUiCompositionPolicy
{
    /// <summary>当前帧是否允许把 runtime Canvas 合成到 game presentation。</summary>
    bool AllowsGameUiComposition { get; }
}

/// <summary>
/// Hosting 在 render 前帧边界提交的三层分辨率描述。
/// </summary>
/// <param name="InternalWorldWidth">固定内部 world/camera 宽度。</param>
/// <param name="InternalWorldHeight">固定内部 world/camera 高度。</param>
/// <param name="PresentationWidth">完整 game presentation 宽度。</param>
/// <param name="PresentationHeight">完整 game presentation 高度。</param>
/// <param name="WorldContentRect">内部 world 在 presentation 中的居中 Fit 区域。</param>
/// <param name="UiDisplayMetrics">全部 CanvasScaler 共用的 presentation/display metrics。</param>
/// <param name="Source">当前 presentation 来源。</param>
/// <param name="RequestRevision">产生本描述的宿主请求版本。</param>
/// <param name="PresentationRevision">descriptor、texture、input 与 IME 必须共同匹配的单调版本。</param>
public readonly record struct GamePresentationDescriptor(
    int InternalWorldWidth,
    int InternalWorldHeight,
    int PresentationWidth,
    int PresentationHeight,
    PresentationViewport WorldContentRect,
    UiDisplayMetrics UiDisplayMetrics,
    GamePresentationSource Source,
    long RequestRevision,
    long PresentationRevision)
{
    /// <summary>描述是否完整有效。</summary>
    public bool IsValid =>
        InternalWorldWidth > 0 &&
        InternalWorldHeight > 0 &&
        PresentationWidth > 0 &&
        PresentationHeight > 0 &&
        PresentationRevision >= 0 &&
        WorldContentRect.SourceWidth == InternalWorldWidth &&
        WorldContentRect.SourceHeight == InternalWorldHeight &&
        WorldContentRect.TargetWidth == PresentationWidth &&
        WorldContentRect.TargetHeight == PresentationHeight;

    /// <summary>Rendering 可直接提交的无 UI 依赖描述。</summary>
    public RenderPresentationDescriptor ToRenderDescriptor()
    {
        return !IsValid
            ? throw new InvalidOperationException("Game presentation descriptor 尚未有效提交。")
            : new RenderPresentationDescriptor(
                PresentationWidth,
                PresentationHeight,
                WorldContentRect,
                UiDisplayMetrics.MetricsRevision,
                PresentationRevision);
    }
}

/// <summary>
/// Presentation 两阶段输入映射：UI 可命中完整 presentation；gameplay 只命中 world content rect。
/// </summary>
/// <param name="IsInsidePresentation">输入是否位于完整 presentation。</param>
/// <param name="PresentationPoint">presentation 左上角像素坐标。</param>
/// <param name="IsInsideWorldContent">输入是否位于 world content rect。</param>
/// <param name="WorldPoint">固定内部 world 左上角像素坐标。</param>
/// <param name="PresentationRevision">映射使用的 presentation revision。</param>
public readonly record struct GamePresentationInputMapping(
    bool IsInsidePresentation,
    Vector2 PresentationPoint,
    bool IsInsideWorldContent,
    Vector2 WorldPoint,
    long PresentationRevision)
{
    /// <summary>
    /// 将 presentation 坐标解析成 UI 与 gameplay 两个阶段；letterbox 永远不会产生 world point。
    /// </summary>
    /// <param name="descriptor">当前已提交描述。</param>
    /// <param name="presentationPoint">presentation 左上角坐标。</param>
    /// <returns>两阶段命中与坐标。</returns>
    public static GamePresentationInputMapping Resolve(
        in GamePresentationDescriptor descriptor,
        Vector2 presentationPoint)
    {
        if (!descriptor.IsValid ||
            !float.IsFinite(presentationPoint.X) ||
            !float.IsFinite(presentationPoint.Y))
        {
            return new GamePresentationInputMapping(
                false,
                default,
                false,
                default,
                descriptor.PresentationRevision);
        }

        bool insidePresentation =
            presentationPoint.X >= 0f &&
            presentationPoint.Y >= 0f &&
            presentationPoint.X < descriptor.PresentationWidth &&
            presentationPoint.Y < descriptor.PresentationHeight;
        if (!insidePresentation)
        {
            return new GamePresentationInputMapping(
                false,
                presentationPoint,
                false,
                default,
                descriptor.PresentationRevision);
        }

        PresentationViewport viewport = descriptor.WorldContentRect;
        float top = viewport.TargetHeight - viewport.Y - viewport.Height;
        bool insideWorld =
            presentationPoint.X >= viewport.X &&
            presentationPoint.Y >= top &&
            presentationPoint.X < viewport.X + viewport.Width &&
            presentationPoint.Y < top + viewport.Height;
        if (!insideWorld)
        {
            return new GamePresentationInputMapping(
                true,
                presentationPoint,
                false,
                default,
                descriptor.PresentationRevision);
        }

        (float worldX, float worldY) = viewport.MapFramebufferToSource(
            presentationPoint.X,
            presentationPoint.Y);
        return new GamePresentationInputMapping(
            true,
            presentationPoint,
            true,
            new Vector2(worldX, worldY),
            descriptor.PresentationRevision);
    }
}

/// <summary>
/// 收敛 Player Default、Editor pending override 与 display metrics，并只在 render 前提交单调 presentation revision。
/// </summary>
public sealed class GamePresentationCoordinator
{
    private readonly int _internalWorldWidth;
    private readonly int _internalWorldHeight;
    private readonly int _playerDefaultWidth;
    private readonly int _playerDefaultHeight;
    private readonly int _maximumTextureSize;
    private readonly IDisplayMetricsSource _displayMetricsSource;
    private readonly IGamePresentationOverride? _override;
    private long _nextRevision;

    /// <summary>
    /// 创建 Hosting presentation 协调器。
    /// </summary>
    public GamePresentationCoordinator(
        int internalWorldWidth,
        int internalWorldHeight,
        int playerDefaultWidth,
        int playerDefaultHeight,
        int maximumTextureSize,
        IDisplayMetricsSource displayMetricsSource,
        IGamePresentationOverride? presentationOverride = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(internalWorldWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(internalWorldHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerDefaultWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerDefaultHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTextureSize);
        _internalWorldWidth = internalWorldWidth;
        _internalWorldHeight = internalWorldHeight;
        _playerDefaultWidth = playerDefaultWidth;
        _playerDefaultHeight = playerDefaultHeight;
        _maximumTextureSize = maximumTextureSize;
        _displayMetricsSource = displayMetricsSource ?? throw new ArgumentNullException(nameof(displayMetricsSource));
        _override = presentationOverride;
    }

    /// <summary>最近一次已提交的描述。</summary>
    public GamePresentationDescriptor Current { get; private set; }

    /// <summary>最近一次被拒绝的 pending override 诊断；下一次有效提交会清空。</summary>
    public string LastDiagnostic { get; private set; } = string.Empty;

    /// <summary>
    /// 在 render 前读取 pending override、提交 display metrics，并生成与下一张纹理一致的描述。
    /// </summary>
    /// <returns>本帧已提交描述。</returns>
    public GamePresentationDescriptor CommitFrameBoundary()
    {
        DisplayMetricsSnapshot display = _displayMetricsSource.CommitFrameBoundary();
        GamePresentationOverride request = new(
            _playerDefaultWidth,
            _playerDefaultHeight,
            GamePresentationSource.PlayerDefault,
            RequestRevision: 0);
        if (_override is not null && _override.TryGetPendingPresentation(out GamePresentationOverride pending))
        {
            if (pending.IsValid && pending.Width <= _maximumTextureSize && pending.Height <= _maximumTextureSize)
            {
                request = pending;
                LastDiagnostic = string.Empty;
            }
            else
            {
                LastDiagnostic = pending.IsValid
                    ? $"Presentation {pending.Width}×{pending.Height} 超过 renderer 上限 {_maximumTextureSize}，已保留上一提交。"
                    : "Presentation override 非法，已保留上一提交。";
                if (Current.IsValid)
                {
                    request = new GamePresentationOverride(
                        Current.PresentationWidth,
                        Current.PresentationHeight,
                        Current.Source,
                        Current.RequestRevision);
                }
            }
        }

        bool changed = !Current.IsValid ||
            Current.PresentationWidth != request.Width ||
            Current.PresentationHeight != request.Height ||
            Current.Source != request.Source ||
            Current.RequestRevision != request.RequestRevision ||
            Current.UiDisplayMetrics.MetricsRevision != display.Revision;
        long revision = changed ? ++_nextRevision : Current.PresentationRevision;
        PresentationViewport worldRect = PresentationViewport.Fit(
            _internalWorldWidth,
            _internalWorldHeight,
            request.Width,
            request.Height);
        UiDisplayMetrics uiMetrics = UiDisplayMetrics.FromRendering(
            request.Width,
            request.Height,
            in display);
        Current = new GamePresentationDescriptor(
            _internalWorldWidth,
            _internalWorldHeight,
            request.Width,
            request.Height,
            worldRect,
            uiMetrics,
            request.Source,
            request.RequestRevision,
            revision);
        return Current;
    }
}
