using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 短寿命爆炸闪光状态，只通过 overlay 与瞬时点光源表现，不写入权威 cell 网格。
/// </summary>
public struct ExplosionFlashEffect
{
    private float _elapsed;
    private float _x;
    private float _y;
    private float _radius;
    private float _duration;
    private uint _colorBgra;

    /// <summary>
    /// 是否仍处于活跃生命周期。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 最近一帧提交的 overlay 命令数量。
    /// </summary>
    public int LastOverlayCommandsSubmitted { get; private set; }

    /// <summary>
    /// 启动一个有限生命周期爆炸闪光。
    /// </summary>
    public void Start(float x, float y, float radius, uint colorBgra = 0xFF_30_80_FF, float durationSeconds = 0.22f)
    {
        _x = x;
        _y = y;
        _radius = MathF.Max(1f, radius);
        _duration = Math.Clamp(durationSeconds, 0.03f, 2f);
        _colorBgra = colorBgra;
        _elapsed = 0f;
        IsActive = true;
        LastOverlayCommandsSubmitted = 0;
    }

    /// <summary>
    /// 在启动当帧提交一次完整强度闪光，但不推进生命周期。
    /// </summary>
    public void SubmitInitial(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsActive)
        {
            LastOverlayCommandsSubmitted = 0;
            return;
        }

        Submit(context, t: 0f, alpha: 1f);
    }

    /// <summary>
    /// 推进并提交当前帧视觉反馈；返回更新后是否仍活跃。
    /// </summary>
    public bool Update(IScriptContext context, float dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsActive)
        {
            LastOverlayCommandsSubmitted = 0;
            return false;
        }

        _elapsed += ResolveVisualDeltaSeconds(context, dt);
        if (_elapsed >= _duration)
        {
            IsActive = false;
            LastOverlayCommandsSubmitted = 0;
            return false;
        }

        float t = _elapsed / _duration;
        float alpha = 1f - t;
        Submit(context, t, alpha);
        return true;
    }

    private void Submit(IScriptContext context, float t, float alpha)
    {
        float outer = _radius * (1.1f + (0.9f * t));
        float inner = MathF.Max(2f, _radius * (0.35f + (0.45f * t)));
        Point2F center = context.Camera.WorldToScreen(_x, _y);
        float scale = MathF.Max(1f, context.Camera.Zoom);
        DrawFlash(context, center.X, center.Y, inner * scale, outer * scale, FadeAlpha(_colorBgra, alpha));
        context.Lighting.AddPointLight(_x, _y, _radius * (2.6f - (0.8f * t)), _colorBgra, 1.35f * alpha);
    }

    private void DrawFlash(IScriptContext context, float x, float y, float inner, float outer, uint color)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(inner) || !float.IsFinite(outer))
        {
            LastOverlayCommandsSubmitted = 0;
            return;
        }

        LastOverlayCommandsSubmitted = 0;
        context.Overlay.Line(x - outer, y, x - inner, y, 3f, color);
        LastOverlayCommandsSubmitted++;
        context.Overlay.Line(x + inner, y, x + outer, y, 3f, color);
        LastOverlayCommandsSubmitted++;
        context.Overlay.Line(x, y - outer, x, y - inner, 3f, color);
        LastOverlayCommandsSubmitted++;
        context.Overlay.Line(x, y + inner, x, y + outer, 3f, color);
        LastOverlayCommandsSubmitted++;
        context.Overlay.OutlineRectangle(x - inner, y - inner, inner * 2f, inner * 2f, 2f, color);
        LastOverlayCommandsSubmitted++;
    }

    private static uint FadeAlpha(uint bgra, float alpha)
    {
        byte original = (byte)(bgra >> 24);
        byte faded = (byte)Math.Clamp((int)MathF.Round(original * Math.Clamp(alpha, 0f, 1f)), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)faded << 24);
    }

    private static float ResolveVisualDeltaSeconds(IScriptContext context, float fallbackDt)
    {
        float realDt = context.Time.RealDeltaTime;
        return float.IsFinite(realDt) && realDt > 0f
            ? realDt
            : MathF.Max(0f, fallbackDt);
    }
}
