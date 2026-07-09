using PixelEngine.Rendering;
using PixelEngine.UI;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

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
}

internal readonly record struct GameViewViewportSnapshot(
    bool IsValid,
    int TextureWidth,
    int TextureHeight,
    GameViewRect ImageRect,
    GameViewRect VisibleViewportRect,
    float FitScale)
{
    public static readonly GameViewViewportSnapshot Empty = new(
        IsValid: false,
        TextureWidth: 0,
        TextureHeight: 0,
        ImageRect: default,
        VisibleViewportRect: default,
        FitScale: 0f);

    public static GameViewViewportSnapshot Create(
        int textureWidth,
        int textureHeight,
        Vector2 imageMinPanel,
        Vector2 availablePanelSize)
    {
        if (textureWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureWidth), "纹理宽度必须为正数。");
        }

        if (textureHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureHeight), "纹理高度必须为正数。");
        }

        Vector2 imageSize = ViewportPanel.FitTexture(textureWidth, textureHeight, availablePanelSize);
        float fitScale = MathF.Min(imageSize.X / textureWidth, imageSize.Y / textureHeight);
        return new GameViewViewportSnapshot(
            IsValid: true,
            TextureWidth: textureWidth,
            TextureHeight: textureHeight,
            ImageRect: new GameViewRect(imageMinPanel.X, imageMinPanel.Y, imageSize.X, imageSize.Y),
            VisibleViewportRect: new GameViewRect(0f, 0f, textureWidth, textureHeight),
            FitScale: fitScale);
    }

    public bool ContainsPanelPoint(Vector2 panelPoint)
    {
        return IsValid && ImageRect.Contains(panelPoint);
    }

    public bool TryMapPanelToViewport(Vector2 panelPoint, out Vector2 viewportPoint)
    {
        if (!ContainsPanelPoint(panelPoint))
        {
            viewportPoint = default;
            return false;
        }

        float normalizedX = (panelPoint.X - ImageRect.X) / MathF.Max(1f, ImageRect.Width);
        float normalizedY = (panelPoint.Y - ImageRect.Y) / MathF.Max(1f, ImageRect.Height);
        viewportPoint = new Vector2(
            VisibleViewportRect.X + (normalizedX * VisibleViewportRect.Width),
            VisibleViewportRect.Y + (normalizedY * VisibleViewportRect.Height));
        return true;
    }

    /// <summary>
    /// 将 viewport 纹理坐标映射回 Game View 面板局部坐标。
    /// </summary>
    /// <param name="viewportPoint">viewport 纹理像素坐标。</param>
    /// <param name="panelPoint">面板局部坐标。</param>
    /// <returns>映射成功时返回 true。</returns>
    public bool TryMapViewportToPanel(Vector2 viewportPoint, out Vector2 panelPoint)
    {
        if (!IsValid || !ImageRect.IsValid || VisibleViewportRect.Width <= 0f || VisibleViewportRect.Height <= 0f)
        {
            panelPoint = default;
            return false;
        }

        float normalizedX = (viewportPoint.X - VisibleViewportRect.X) / VisibleViewportRect.Width;
        float normalizedY = (viewportPoint.Y - VisibleViewportRect.Y) / VisibleViewportRect.Height;
        if (!float.IsFinite(normalizedX) || !float.IsFinite(normalizedY))
        {
            panelPoint = default;
            return false;
        }

        panelPoint = new Vector2(
            ImageRect.X + (normalizedX * ImageRect.Width),
            ImageRect.Y + (normalizedY * ImageRect.Height));
        return true;
    }

    /// <summary>
    /// 将 viewport 纹理坐标中的 IME caret/候选锚点映射到窗口 client / framebuffer 坐标，
    /// 与 <see cref="TryCreateUiPresentTarget"/> 使用同一 panel origin + DPI 约定，供 IMM32 定位。
    /// </summary>
    /// <param name="viewportGeometry">viewport 纹理像素空间中的 IME 几何。</param>
    /// <param name="panelOriginFramebuffer">Game View 面板原点在 framebuffer/client 空间的位置（已含 DPI 缩放）。</param>
    /// <param name="framebufferScale">逻辑面板坐标 → framebuffer 的缩放。</param>
    /// <param name="windowGeometry">映射后的窗口 client 坐标几何。</param>
    /// <returns>snapshot 有效且输入几何有效时返回 true。</returns>
    public bool TryMapViewportImeGeometryToWindowClient(
        in UiImeGeometry viewportGeometry,
        Vector2 panelOriginFramebuffer,
        Vector2 framebufferScale,
        out UiImeGeometry windowGeometry)
    {
        windowGeometry = UiImeGeometry.None;
        if (!viewportGeometry.HasAny ||
            !IsValid ||
            !ImageRect.IsValid ||
            VisibleViewportRect.Width <= 0f ||
            VisibleViewportRect.Height <= 0f)
        {
            return false;
        }

        float fitScaleX = ImageRect.Width / VisibleViewportRect.Width;
        float fitScaleY = ImageRect.Height / VisibleViewportRect.Height;
        if (!float.IsFinite(fitScaleX) || !float.IsFinite(fitScaleY) || fitScaleX <= 0f || fitScaleY <= 0f)
        {
            return false;
        }

        float panelOffsetX = ImageRect.X - (VisibleViewportRect.X * fitScaleX);
        float panelOffsetY = ImageRect.Y - (VisibleViewportRect.Y * fitScaleY);
        // 先 viewport → panel-local，再 panel-local → window client（与 present target 同源）。
        UiImeGeometry panelLocal = viewportGeometry.Transform(panelOffsetX, panelOffsetY, fitScaleX, fitScaleY);
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

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}
