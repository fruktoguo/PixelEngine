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
        Context.Camera.SetCenter(clampedX, clampedY);
    }

    private static float ClampWithFallback(float value, float min, float max)
    {
        return min <= max ? Math.Clamp(value, min, max) : (min + max) * 0.5f;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * Math.Clamp(t, 0f, 1f));
    }
}
