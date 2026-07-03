using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 相机跟随脚本，按玩家运动方向 lookahead，并用脚本相机公开 API 设置中心与缩放。
/// </summary>
public sealed class CameraFollow : Behaviour
{
    private PlayerController? _target;
    private float _smoothedX;
    private float _smoothedY;
    private bool _initialized;

    /// <summary>
    /// 相机跟随阻尼；值越大越快贴近目标。
    /// </summary>
    public float Damping { get; set; } = 12f;

    /// <summary>
    /// 水平方向前瞻距离。
    /// </summary>
    public float LookaheadX { get; set; } = 28f;

    /// <summary>
    /// 垂直方向前瞻距离。
    /// </summary>
    public float LookaheadY { get; set; } = 10f;

    /// <summary>
    /// 默认缩放倍率。
    /// </summary>
    public float Zoom { get; set; } = 1f;

    /// <summary>
    /// 滚轮缩放允许的最小倍率。
    /// </summary>
    public float MinZoom { get; set; } = 1f;

    /// <summary>
    /// 滚轮缩放允许的最大倍率。
    /// </summary>
    public float MaxZoom { get; set; } = 4f;

    /// <summary>
    /// 每格滚轮改变的缩放倍率。
    /// </summary>
    public float ZoomStep { get; set; } = 1f;

    /// <summary>
    /// 是否允许鼠标滚轮调整视野缩放。
    /// </summary>
    public bool MouseWheelZoomEnabled { get; set; } = true;

    /// <summary>
    /// 是否在整数缩放下把视口左上角对齐到整数 cell，避免像素风格相机落回逐像素重采样慢路径。
    /// </summary>
    public bool SnapOriginToCellGrid { get; set; } = true;

    /// <summary>
    /// 关卡左边界。
    /// </summary>
    public float MinX { get; set; } = 0f;

    /// <summary>
    /// 关卡上边界。
    /// </summary>
    public float MinY { get; set; } = 0f;

    /// <summary>
    /// 关卡右边界。
    /// </summary>
    public float MaxX { get; set; } = 1_024f;

    /// <summary>
    /// 关卡下边界。
    /// </summary>
    public float MaxY { get; set; } = 512f;

    /// <inheritdoc />
    protected override void OnStart()
    {
        if (Entity.TryGetComponent<PlayerController>(out PlayerController target))
        {
            _target = target;
        }

        Context.Camera.SetZoom(Zoom);
        if (_target is not null)
        {
            _smoothedX = _target.CenterX;
            _smoothedY = _target.CenterY;
            _initialized = true;
            ApplyCamera(_smoothedX, _smoothedY);
        }
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (_target is null)
        {
            if (Entity.TryGetComponent<PlayerController>(out PlayerController target))
            {
                _target = target;
            }

            if (_target is null)
            {
                return;
            }
        }

        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        ApplyMouseWheelZoom();
        float targetX = _target.CenterX + (MathF.Sign(_target.State.VelocityX) * LookaheadX);
        float targetY = _target.CenterY + (MathF.Sign(_target.State.VelocityY) * LookaheadY);
        if (!_initialized)
        {
            _smoothedX = targetX;
            _smoothedY = targetY;
            _initialized = true;
        }

        float t = 1f - MathF.Exp(-Damping * dt);
        _smoothedX = Lerp(_smoothedX, targetX, t);
        _smoothedY = Lerp(_smoothedY, targetY, t);
        ApplyCamera(_smoothedX, _smoothedY);
    }

    private void ApplyCamera(float x, float y)
    {
        Context.Camera.SetZoom(Zoom);
        RectF viewport = Context.Camera.Viewport;
        float halfWidth = viewport.Width / (2f * MathF.Max(Zoom, 0.001f));
        float halfHeight = viewport.Height / (2f * MathF.Max(Zoom, 0.001f));
        float clampedX = ClampWithFallback(x, MinX + halfWidth, MaxX - halfWidth);
        float clampedY = ClampWithFallback(y, MinY + halfHeight, MaxY - halfHeight);
        if (SnapOriginToCellGrid && IsIntegerZoom(Zoom))
        {
            clampedX = SnapCenterToIntegerOrigin(clampedX, halfWidth, MinX, MaxX);
            clampedY = SnapCenterToIntegerOrigin(clampedY, halfHeight, MinY, MaxY);
        }

        Context.Camera.SetCenter(clampedX, clampedY);
    }

    private void ApplyMouseWheelZoom()
    {
        if (!MouseWheelZoomEnabled)
        {
            return;
        }

        float wheel = Context.Input.MouseWheelY;
        if (MathF.Abs(wheel) <= 0.001f)
        {
            return;
        }

        float min = MathF.Max(0.1f, MathF.Min(MinZoom, MaxZoom));
        float max = MathF.Max(min, MathF.Max(MinZoom, MaxZoom));
        float step = MathF.Max(0.1f, ZoomStep);
        Zoom = Math.Clamp(MathF.Round((Zoom + (MathF.Sign(wheel) * step)) / step) * step, min, max);
    }

    private static float ClampWithFallback(float value, float min, float max)
    {
        return min <= max ? Math.Clamp(value, min, max) : (min + max) * 0.5f;
    }

    private static bool IsIntegerZoom(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0f)
        {
            return false;
        }

        float rounded = MathF.Round(zoom);
        return rounded >= 1f && MathF.Abs(zoom - rounded) <= 0.0001f;
    }

    private static float SnapCenterToIntegerOrigin(float center, float halfExtent, float minWorld, float maxWorld)
    {
        float visibleExtent = halfExtent * 2f;
        int minOrigin = (int)MathF.Ceiling(minWorld);
        int maxOrigin = (int)MathF.Floor(maxWorld - visibleExtent);
        if (minOrigin > maxOrigin)
        {
            return center;
        }

        float snappedOrigin = Math.Clamp(MathF.Round(center - halfExtent), minOrigin, maxOrigin);
        return snappedOrigin + halfExtent;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * Math.Clamp(t, 0f, 1f));
    }
}
