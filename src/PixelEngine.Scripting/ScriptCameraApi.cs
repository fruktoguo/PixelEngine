namespace PixelEngine.Scripting;

/// <summary>
/// 脚本可变相机服务，维护中心、缩放、视口与屏幕/世界坐标转换。
/// </summary>
public sealed class ScriptCameraApi : ICameraApi
{
    /// <summary>
    /// 创建脚本相机服务。
    /// </summary>
    /// <param name="viewportWidth">视口宽度，单位像素。</param>
    /// <param name="viewportHeight">视口高度，单位像素。</param>
    /// <param name="centerX">初始中心 X 坐标。</param>
    /// <param name="centerY">初始中心 Y 坐标。</param>
    /// <param name="zoom">初始缩放倍率；1 表示 1:1。</param>
    public ScriptCameraApi(int viewportWidth, int viewportHeight, float centerX = 0f, float centerY = 0f, float zoom = 1f)
    {
        SetViewport(viewportWidth, viewportHeight);
        SetCenter(centerX, centerY);
        SetZoom(zoom);
    }

    /// <inheritdoc />
    public float CenterX { get; private set; }

    /// <inheritdoc />
    public float CenterY { get; private set; }

    /// <inheritdoc />
    public float Zoom { get; private set; }

    /// <inheritdoc />
    public RectF Viewport => new(0, 0, ViewportWidth, ViewportHeight);

    /// <summary>
    /// 当前视口宽度，单位像素。
    /// </summary>
    public int ViewportWidth { get; private set; }

    /// <summary>
    /// 当前视口高度，单位像素。
    /// </summary>
    public int ViewportHeight { get; private set; }

    /// <summary>
    /// 更新视口尺寸。
    /// </summary>
    /// <param name="viewportWidth">视口宽度，单位像素。</param>
    /// <param name="viewportHeight">视口高度，单位像素。</param>
    public void SetViewport(int viewportWidth, int viewportHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeight);
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    /// <inheritdoc />
    public void SetCenter(float x, float y)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        CenterX = x;
        CenterY = y;
    }

    /// <inheritdoc />
    public void SetZoom(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "相机缩放必须为有限正数。");
        }

        Zoom = zoom;
    }

    /// <inheritdoc />
    public void Follow(Entity target)
    {
        ArgumentNullException.ThrowIfNull(target);
        throw new NotSupportedException("当前脚本实体尚无统一 Transform，不能通过 Entity 自动跟随；请由脚本读取目标位置后调用 SetCenter。");
    }

    /// <inheritdoc />
    public Point2F ScreenToWorld(float screenX, float screenY)
    {
        ValidateFinite(screenX, nameof(screenX));
        ValidateFinite(screenY, nameof(screenY));
        return new Point2F(OriginWorldX + (screenX / Zoom), OriginWorldY + (screenY / Zoom));
    }

    /// <inheritdoc />
    public Point2F WorldToScreen(float worldX, float worldY)
    {
        ValidateFinite(worldX, nameof(worldX));
        ValidateFinite(worldY, nameof(worldY));
        return new Point2F((worldX - OriginWorldX) * Zoom, (worldY - OriginWorldY) * Zoom);
    }

    /// <summary>
    /// 读取当前相机快照；Hosting 可将该值适配给 Rendering 与 World residency。
    /// </summary>
    /// <returns>当前相机快照。</returns>
    public CameraSnapshot Snapshot()
    {
        return new CameraSnapshot(OriginWorldX, OriginWorldY, 1f / Zoom, ViewportWidth, ViewportHeight);
    }

    private float OriginWorldX => CenterX - (ViewportWidth / (2f * Zoom));

    private float OriginWorldY => CenterY - (ViewportHeight / (2f * Zoom));

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "相机坐标必须为有限值。");
        }
    }
}
