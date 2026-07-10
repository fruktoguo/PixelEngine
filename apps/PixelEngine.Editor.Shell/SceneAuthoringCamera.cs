using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Scene View 独立 authoring 相机；不读取或修改 runtime pipeline camera。
/// </summary>
internal sealed class SceneAuthoringCamera
{
    private const float MinCellsPerPixel = 0.05f;
    private const float MaxCellsPerPixel = 64f;
    private float _viewportWidth = 1f;
    private float _viewportHeight = 1f;

    public float CenterX { get; private set; }

    public float CenterY { get; private set; }

    public float CellsPerPixel { get; private set; } = 1f;

    public SceneAuthoringCameraSnapshot Snapshot => new(
        CenterX,
        CenterY,
        CellsPerPixel,
        _viewportWidth,
        _viewportHeight);

    public void SetViewport(Vector2 size)
    {
        _viewportWidth = NormalizeViewport(size.X);
        _viewportHeight = NormalizeViewport(size.Y);
    }

    public void PanPixels(Vector2 delta)
    {
        CenterX -= delta.X * CellsPerPixel;
        CenterY -= delta.Y * CellsPerPixel;
    }

    public void ZoomAt(Vector2 canvasPoint, float wheelDelta)
    {
        if (!float.IsFinite(wheelDelta) || wheelDelta == 0f)
        {
            return;
        }

        Vector2 before = CanvasToWorld(canvasPoint);
        CellsPerPixel = Math.Clamp(
            CellsPerPixel / MathF.Pow(1.12f, wheelDelta),
            MinCellsPerPixel,
            MaxCellsPerPixel);
        Vector2 after = CanvasToWorld(canvasPoint);
        CenterX += before.X - after.X;
        CenterY += before.Y - after.Y;
    }

    public void FrameBounds(SceneAuthoringBounds bounds, float paddingPixels = 36f)
    {
        float usableWidth = MathF.Max(1f, _viewportWidth - (MathF.Max(0f, paddingPixels) * 2f));
        float usableHeight = MathF.Max(1f, _viewportHeight - (MathF.Max(0f, paddingPixels) * 2f));
        CenterX = bounds.X + (bounds.Width * 0.5f);
        CenterY = bounds.Y + (bounds.Height * 0.5f);
        CellsPerPixel = Math.Clamp(
            MathF.Max(bounds.Width / usableWidth, bounds.Height / usableHeight),
            MinCellsPerPixel,
            MaxCellsPerPixel);
    }

    public void FramePoint(Vector2 worldPoint, float radiusCells = 48f)
    {
        float radius = MathF.Max(8f, radiusCells);
        FrameBounds(new SceneAuthoringBounds(
            worldPoint.X - radius,
            worldPoint.Y - radius,
            radius * 2f,
            radius * 2f));
    }

    public Vector2 WorldToCanvas(Vector2 worldPoint)
    {
        SceneAuthoringCameraSnapshot snapshot = Snapshot;
        return new Vector2(
            (worldPoint.X - snapshot.OriginX) / snapshot.CellsPerPixel,
            (worldPoint.Y - snapshot.OriginY) / snapshot.CellsPerPixel);
    }

    public Vector2 CanvasToWorld(Vector2 canvasPoint)
    {
        SceneAuthoringCameraSnapshot snapshot = Snapshot;
        return new Vector2(
            snapshot.OriginX + (canvasPoint.X * snapshot.CellsPerPixel),
            snapshot.OriginY + (canvasPoint.Y * snapshot.CellsPerPixel));
    }

    private static float NormalizeViewport(float value)
    {
        return float.IsFinite(value) && value > 0f ? value : 1f;
    }
}

/// <summary>
/// Scene View authoring 相机的不可变快照。
/// </summary>
internal readonly record struct SceneAuthoringCameraSnapshot(
    float CenterX,
    float CenterY,
    float CellsPerPixel,
    float ViewportWidth,
    float ViewportHeight)
{
    public float OriginX => CenterX - (ViewportWidth * CellsPerPixel * 0.5f);

    public float OriginY => CenterY - (ViewportHeight * CellsPerPixel * 0.5f);
}
