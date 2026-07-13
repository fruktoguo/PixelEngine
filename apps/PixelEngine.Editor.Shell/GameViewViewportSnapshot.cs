using PixelEngine.Rendering;
using PixelEngine.UI;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>Game View 面板内的轴对齐矩形，用于命中测试与坐标映射。</summary>
internal readonly record struct GameViewRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public Vector2 Position => new(X, Y);

    public Vector2 Size => new(Width, Height);

    public bool IsValid => Width > 0f && Height > 0f;

    public bool Contains(Vector2 point)
    {
        return IsValid &&
            point.X >= X &&
            point.Y >= Y &&
            point.X < Right &&
            point.Y < Bottom;
    }

    public static GameViewRect Intersect(in GameViewRect left, in GameViewRect right)
    {
        float x = MathF.Max(left.X, right.X);
        float y = MathF.Max(left.Y, right.Y);
        float farX = MathF.Min(left.Right, right.Right);
        float farY = MathF.Min(left.Bottom, right.Bottom);
        return new GameViewRect(x, y, MathF.Max(0f, farX - x), MathF.Max(0f, farY - y));
    }
}

/// <summary>
/// Game View 原子快照：presentation texture/revision、完整 image rect、可见 crop、pan、display scale 与 world content rect。
/// </summary>
internal readonly record struct GameViewViewportSnapshot(
    bool IsValid,
    int TextureWidth,
    int TextureHeight,
    long PresentationRevision,
    GameViewRect DisplayAreaRect,
    GameViewRect ImageRect,
    GameViewRect VisiblePanelRect,
    GameViewRect VisibleViewportRect,
    Vector2 DisplayScale,
    Vector2 Pan,
    PresentationViewport WorldContentRect)
{
    public static readonly GameViewViewportSnapshot Empty = new(
        IsValid: false,
        TextureWidth: 0,
        TextureHeight: 0,
        PresentationRevision: 0,
        DisplayAreaRect: default,
        ImageRect: default,
        VisiblePanelRect: default,
        VisibleViewportRect: default,
        DisplayScale: default,
        Pan: default,
        WorldContentRect: default);

    /// <summary>旧 FitScale 兼容别名；非等 DPI 时取两轴较小值。</summary>
    public float FitScale => MathF.Min(DisplayScale.X, DisplayScale.Y);

    /// <summary>
    /// 创建旧式 Fit 快照；world content 等于整张 texture，revision 为 0。
    /// </summary>
    public static GameViewViewportSnapshot Create(
        int textureWidth,
        int textureHeight,
        Vector2 imageMinPanel,
        Vector2 availablePanelSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureHeight);
        float availableWidth = MathF.Max(1f, availablePanelSize.X);
        float availableHeight = MathF.Max(1f, availablePanelSize.Y);
        float fitScale = MathF.Max(
            float.Epsilon,
            MathF.Min(availableWidth / textureWidth, availableHeight / textureHeight));
        GameViewRect displayArea = new(imageMinPanel.X, imageMinPanel.Y, availableWidth, availableHeight);
        GameViewRect imageRect = new(
            imageMinPanel.X,
            imageMinPanel.Y,
            textureWidth * fitScale,
            textureHeight * fitScale);
        GameViewRect visiblePanel = GameViewRect.Intersect(in displayArea, in imageRect);
        GameViewRect visibleViewport = new(
            0f,
            0f,
            MathF.Min(textureWidth, visiblePanel.Width / fitScale),
            MathF.Min(textureHeight, visiblePanel.Height / fitScale));
        return new GameViewViewportSnapshot(
            visiblePanel.IsValid,
            textureWidth,
            textureHeight,
            0,
            displayArea,
            imageRect,
            visiblePanel,
            visibleViewport,
            new Vector2(fitScale),
            Vector2.Zero,
            PresentationViewport.Fit(textureWidth, textureHeight, textureWidth, textureHeight));
    }

    /// <summary>
    /// 按 Fit 或物理像素百分比创建 snapshot。scalePercent=0 表示 Fit；100 表示一个 presentation pixel
    /// 对应一个 framebuffer pixel。图像超出面板时 pan 以 presentation pixel clamp。
    /// </summary>
    public static GameViewViewportSnapshot Create(
        int textureWidth,
        int textureHeight,
        long presentationRevision,
        in PresentationViewport worldContentRect,
        Vector2 imageMinPanel,
        Vector2 availablePanelSize,
        Vector2 framebufferScale,
        float scalePercent,
        Vector2 requestedPan)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureHeight);
        if (presentationRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(presentationRevision));
        }

        float availableWidth = MathF.Max(1f, availablePanelSize.X);
        float availableHeight = MathF.Max(1f, availablePanelSize.Y);
        float framebufferScaleX = NormalizeScale(framebufferScale.X);
        float framebufferScaleY = NormalizeScale(framebufferScale.Y);
        float scaleX;
        float scaleY;
        if (float.IsFinite(scalePercent) && scalePercent > 0f)
        {
            float physicalScale = scalePercent / 100f;
            scaleX = physicalScale / framebufferScaleX;
            scaleY = physicalScale / framebufferScaleY;
        }
        else
        {
            float fitScale = MathF.Min(availableWidth / textureWidth, availableHeight / textureHeight);
            scaleX = fitScale;
            scaleY = fitScale;
        }

        scaleX = MathF.Max(float.Epsilon, scaleX);
        scaleY = MathF.Max(float.Epsilon, scaleY);
        float imageWidth = textureWidth * scaleX;
        float imageHeight = textureHeight * scaleY;
        float visiblePresentationWidth = MathF.Min(textureWidth, availableWidth / scaleX);
        float visiblePresentationHeight = MathF.Min(textureHeight, availableHeight / scaleY);
        float maximumPanX = MathF.Max(0f, (textureWidth - visiblePresentationWidth) * 0.5f);
        float maximumPanY = MathF.Max(0f, (textureHeight - visiblePresentationHeight) * 0.5f);
        Vector2 pan = new(
            Math.Clamp(NormalizeFinite(requestedPan.X), -maximumPanX, maximumPanX),
            Math.Clamp(NormalizeFinite(requestedPan.Y), -maximumPanY, maximumPanY));
        if (imageWidth <= availableWidth)
        {
            pan.X = 0f;
        }

        if (imageHeight <= availableHeight)
        {
            pan.Y = 0f;
        }

        GameViewRect displayArea = new(imageMinPanel.X, imageMinPanel.Y, availableWidth, availableHeight);
        GameViewRect imageRect = new(
            imageMinPanel.X + ((availableWidth - imageWidth) * 0.5f) - (pan.X * scaleX),
            imageMinPanel.Y + ((availableHeight - imageHeight) * 0.5f) - (pan.Y * scaleY),
            imageWidth,
            imageHeight);
        GameViewRect visiblePanel = GameViewRect.Intersect(in displayArea, in imageRect);
        GameViewRect visibleViewport = new(
            MathF.Max(0f, (visiblePanel.X - imageRect.X) / scaleX),
            MathF.Max(0f, (visiblePanel.Y - imageRect.Y) / scaleY),
            MathF.Min(textureWidth, visiblePanel.Width / scaleX),
            MathF.Min(textureHeight, visiblePanel.Height / scaleY));
        return new GameViewViewportSnapshot(
            IsValid: visiblePanel.IsValid,
            textureWidth,
            textureHeight,
            presentationRevision,
            displayArea,
            imageRect,
            visiblePanel,
            visibleViewport,
            new Vector2(scaleX, scaleY),
            pan,
            worldContentRect);
    }

    public bool ContainsPanelPoint(Vector2 panelPoint)
    {
        return IsValid && VisiblePanelRect.Contains(panelPoint);
    }

    /// <summary>将 panel-local 可见图像点映射到完整 presentation texture。</summary>
    public bool TryMapPanelToViewport(Vector2 panelPoint, out Vector2 viewportPoint)
    {
        if (!ContainsPanelPoint(panelPoint))
        {
            viewportPoint = default;
            return false;
        }

        viewportPoint = new Vector2(
            (panelPoint.X - ImageRect.X) / ImageRect.Width * TextureWidth,
            (panelPoint.Y - ImageRect.Y) / ImageRect.Height * TextureHeight);
        return float.IsFinite(viewportPoint.X) && float.IsFinite(viewportPoint.Y);
    }

    /// <summary>将 panel-local 点映射到固定内部 world；presentation letterbox 返回 false。</summary>
    public bool TryMapPanelToWorld(Vector2 panelPoint, out Vector2 worldPoint)
    {
        if (!TryMapPanelToViewport(panelPoint, out Vector2 presentationPoint))
        {
            worldPoint = default;
            return false;
        }

        return TryMapPresentationToWorld(presentationPoint, out worldPoint);
    }

    /// <summary>
    /// 将窗口 framebuffer 指针按 panel origin、DPI 与 Game View display/crop 映射到 presentation。
    /// </summary>
    public bool TryMapFramebufferToViewport(
        Vector2 framebufferPoint,
        Vector2 panelOriginFramebuffer,
        Vector2 framebufferScale,
        out Vector2 viewportPoint)
    {
        if (!TryMapFramebufferToPanel(
                framebufferPoint,
                panelOriginFramebuffer,
                framebufferScale,
                out Vector2 panelPoint))
        {
            viewportPoint = default;
            return false;
        }

        return TryMapPanelToViewport(panelPoint, out viewportPoint);
    }

    /// <summary>将窗口 framebuffer 指针映射到固定内部 world；letterbox 与裁剪外区域返回 false。</summary>
    public bool TryMapFramebufferToWorld(
        Vector2 framebufferPoint,
        Vector2 panelOriginFramebuffer,
        Vector2 framebufferScale,
        out Vector2 worldPoint)
    {
        if (!TryMapFramebufferToPanel(
                framebufferPoint,
                panelOriginFramebuffer,
                framebufferScale,
                out Vector2 panelPoint))
        {
            worldPoint = default;
            return false;
        }

        return TryMapPanelToWorld(panelPoint, out worldPoint);
    }

    /// <summary>将 presentation texture 坐标映射回 panel-local；被 crop 的点返回 false。</summary>
    public bool TryMapViewportToPanel(Vector2 viewportPoint, out Vector2 panelPoint)
    {
        if (!IsValid || !VisibleViewportRect.Contains(viewportPoint))
        {
            panelPoint = default;
            return false;
        }

        panelPoint = new Vector2(
            ImageRect.X + (viewportPoint.X / TextureWidth * ImageRect.Width),
            ImageRect.Y + (viewportPoint.Y / TextureHeight * ImageRect.Height));
        return true;
    }

    /// <summary>
    /// 将 presentation 中的 IME caret/候选锚点映射到窗口 client；被 crop 的几何会被明确清除。
    /// </summary>
    public bool TryMapViewportImeGeometryToWindowClient(
        in UiImeGeometry viewportGeometry,
        Vector2 panelOriginFramebuffer,
        Vector2 framebufferScale,
        out UiImeGeometry windowGeometry)
    {
        windowGeometry = UiImeGeometry.None;
        if (!viewportGeometry.HasAny || !IsValid || !ImeGeometryIsVisible(in viewportGeometry))
        {
            return false;
        }

        float scaleX = ImageRect.Width / TextureWidth;
        float scaleY = ImageRect.Height / TextureHeight;
        UiImeGeometry panelLocal = viewportGeometry.Transform(ImageRect.X, ImageRect.Y, scaleX, scaleY);
        float dpiX = NormalizeScale(framebufferScale.X);
        float dpiY = NormalizeScale(framebufferScale.Y);
        float originX = float.IsFinite(panelOriginFramebuffer.X) ? panelOriginFramebuffer.X : 0f;
        float originY = float.IsFinite(panelOriginFramebuffer.Y) ? panelOriginFramebuffer.Y : 0f;
        windowGeometry = panelLocal.Transform(originX, originY, dpiX, dpiY);
        return windowGeometry.HasAny;
    }

    public bool TryCreateUiPresentTarget(
        Vector2 panelOriginFramebuffer,
        Vector2 framebufferScale,
        out UiPresentTarget target)
    {
        if (!IsValid || !ImageRect.IsValid)
        {
            target = default;
            return false;
        }

        float scaleX = NormalizeScale(framebufferScale.X);
        float scaleY = NormalizeScale(framebufferScale.Y);
        float left = panelOriginFramebuffer.X + (ImageRect.X * scaleX);
        float top = panelOriginFramebuffer.Y + (ImageRect.Y * scaleY);
        float right = left + (ImageRect.Width * scaleX);
        float bottom = top + (ImageRect.Height * scaleY);
        int x = (int)MathF.Floor(left);
        int y = (int)MathF.Floor(top);
        int width = Math.Max(1, (int)MathF.Ceiling(right) - x);
        int height = Math.Max(1, (int)MathF.Ceiling(bottom) - y);
        target = new UiPresentTarget(x, y, width, height, MathF.Max(scaleX, scaleY));
        return true;
    }

    /// <summary>创建 presentation texture 内的全尺寸 runtime UI 目标。</summary>
    public bool TryCreateRuntimeUiPresentTarget(out UiPresentTarget target)
    {
        if (!IsValid || TextureWidth <= 0 || TextureHeight <= 0)
        {
            target = default;
            return false;
        }

        target = new UiPresentTarget(0, 0, TextureWidth, TextureHeight, 1f);
        return true;
    }

    private bool TryMapPresentationToWorld(Vector2 point, out Vector2 worldPoint)
    {
        worldPoint = default;
        if (WorldContentRect.SourceWidth <= 0 || WorldContentRect.SourceHeight <= 0)
        {
            return false;
        }

        float top = WorldContentRect.TargetHeight - WorldContentRect.Y - WorldContentRect.Height;
        if (point.X < WorldContentRect.X ||
            point.Y < top ||
            point.X >= WorldContentRect.X + WorldContentRect.Width ||
            point.Y >= top + WorldContentRect.Height)
        {
            return false;
        }

        (float x, float y) = WorldContentRect.MapFramebufferToSource(point.X, point.Y);
        worldPoint = new Vector2(x, y);
        return true;
    }

    private bool ImeGeometryIsVisible(in UiImeGeometry geometry)
    {
        return (!geometry.HasCaretRect ||
                VisibleViewportRect.Contains(new Vector2(geometry.CaretX, geometry.CaretY))) &&
            (!geometry.HasCandidateAnchor ||
                VisibleViewportRect.Contains(new Vector2(geometry.CandidateAnchorX, geometry.CandidateAnchorY)));
    }

    private static bool TryMapFramebufferToPanel(
        Vector2 framebufferPoint,
        Vector2 panelOriginFramebuffer,
        Vector2 framebufferScale,
        out Vector2 panelPoint)
    {
        panelPoint = default;
        if (!float.IsFinite(framebufferPoint.X) ||
            !float.IsFinite(framebufferPoint.Y) ||
            !float.IsFinite(panelOriginFramebuffer.X) ||
            !float.IsFinite(panelOriginFramebuffer.Y))
        {
            return false;
        }

        panelPoint = new Vector2(
            (framebufferPoint.X - panelOriginFramebuffer.X) / NormalizeScale(framebufferScale.X),
            (framebufferPoint.Y - panelOriginFramebuffer.Y) / NormalizeScale(framebufferScale.Y));
        return true;
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    private static float NormalizeFinite(float value)
    {
        return float.IsFinite(value) ? value : 0f;
    }
}
