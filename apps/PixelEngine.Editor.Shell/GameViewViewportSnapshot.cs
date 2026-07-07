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
            point.X <= Right &&
            point.Y <= Bottom;
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
}
