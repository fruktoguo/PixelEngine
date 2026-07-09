using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 数据驱动手榴弹投射物；在脚本相位按 dt 积分，碰撞后反弹，引信到期后经公开世界效果爆炸。
/// </summary>
public sealed class GrenadeProjectile : Behaviour
{
    private float _vx;
    private float _vy;
    private float _fuseRemaining;
    private float _gravity;
    private float _bounce;
    private float _impulse;
    private string _impactCue = "explosion.wav";
    private ExplosionFlashEffect _flash;

    /// <summary>
    /// 是否已经爆炸。
    /// </summary>
    public bool Exploded { get; private set; }

    /// <summary>
    /// 当前 X 坐标。
    /// </summary>
    public float X { get; private set; }

    /// <summary>
    /// 当前 Y 坐标。
    /// </summary>
    public float Y { get; private set; }

    /// <summary>
    /// 当前爆炸半径，已包含武器控制器注入的地形效果倍率。
    /// </summary>
    public int Radius { get; private set; }

    /// <summary>
    /// 当前爆炸强度，已包含武器控制器注入的地形效果倍率。
    /// </summary>
    public float BlastForce { get; private set; }

    /// <summary>
    /// 初始化投射物状态。
    /// </summary>
    public void Initialize(
        float x,
        float y,
        float vx,
        float vy,
        float fuseSeconds,
        int radius,
        float damage,
        float impulse,
        float gravity,
        float bounce,
        string impactCue)
    {
        X = x;
        Y = y;
        _vx = vx;
        _vy = vy;
        _fuseRemaining = Math.Max(0.01f, fuseSeconds);
        Radius = Math.Max(1, radius);
        _impulse = Math.Max(1f, impulse);
        BlastForce = Math.Max(_impulse, Math.Max(1f, damage));
        _gravity = Math.Max(0f, gravity);
        _bounce = Math.Clamp(bounce, 0f, 0.9f);
        _impactCue = string.IsNullOrWhiteSpace(impactCue) ? "explosion.wav" : impactCue;
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (Exploded)
        {
            if (!_flash.Update(Context, dt))
            {
                Entity.Destroy();
            }

            return;
        }

        float safeDt = MathF.Max(0f, dt);
        _fuseRemaining -= safeDt;
        _vy += _gravity * safeDt;
        float moveX = _vx * safeDt;
        float moveY = _vy * safeDt;
        float length = MathF.Sqrt((moveX * moveX) + (moveY * moveY));
        RaycastHit hit = default;
        if (length > 0.001f && !TryRaycast(moveX / length, moveY / length, length, out hit))
        {
            Detonate();
            return;
        }

        if (length > 0.001f && hit.Hit)
        {
            X = hit.X;
            Y = hit.Y;
            _vx *= _bounce;
            _vy = -_vy * _bounce;
        }
        else
        {
            X += moveX;
            Y += moveY;
        }

        if (Entity.TryGetComponent(out Transform transform))
        {
            transform.SetPosition(X, Y);
        }

        Point2F screen = Context.Camera.WorldToScreen(X, Y);
        Context.Overlay.SolidRectangle(screen.X - 2f, screen.Y - 2f, 4f, 4f, 0xFF_D7_F2_7A);

        if (_fuseRemaining <= 0f)
        {
            Detonate();
        }
    }

    private bool TryRaycast(float dx, float dy, float length, out RaycastHit hit)
    {
        try
        {
            _ = Context.Solids.Raycast(X, Y, dx, dy, length, out hit);
            return true;
        }
        catch (InvalidOperationException exception) when (IsUnresidentChunk(exception))
        {
            hit = default;
            return false;
        }
    }

    private static bool IsUnresidentChunk(InvalidOperationException exception)
    {
        return exception.Message.Contains("目标 chunk 未驻留", StringComparison.Ordinal);
    }

    private void Detonate()
    {
        Exploded = true;
        Context.World.Explode(X, Y, Radius, BlastForce);
        _flash.Start(X, Y, Radius, 0xFF_30_80_FF);
        _flash.SubmitInitial(Context);
        Context.Audio.PlayAt(_impactCue, X, Y, 0.8f);
        TransientParticleBurst.Emit(
            Context,
            X,
            Y,
            Math.Clamp(Radius / 2, 12, 96),
            Math.Max(10f, _impulse * 1.5f),
            lifetime: 54);
    }
}
