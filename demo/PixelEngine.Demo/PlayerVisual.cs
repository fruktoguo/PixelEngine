using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩 Demo 的玩家可见层，使用脚本 overlay 绘制角色、准星与短暂弹道，不污染权威 cell 网格。
/// </summary>
public sealed class PlayerVisual : Behaviour
{
    private PlayerController? _player;
    private PlayableProjectileTool? _projectile;
    private float _facing = 1f;

    /// <summary>
    /// 玩家主体颜色。
    /// </summary>
    public uint BodyColorBgra { get; set; } = 0xFF_F2_D0_5E;

    /// <summary>
    /// 玩家轮廓颜色。
    /// </summary>
    public uint OutlineColorBgra { get; set; } = 0xFF_18_20_2A;

    /// <summary>
    /// 准星颜色。
    /// </summary>
    public uint CrosshairColorBgra { get; set; } = 0xFF_60_D8_FF;

    /// <summary>
    /// 最近一帧提交的 overlay 命令数量。
    /// </summary>
    public int LastOverlayCommandsSubmitted { get; private set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveComponents();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        ResolveComponents();
        if (_player is null)
        {
            return;
        }

        CharacterState state = _player.State;
        if (state.Width <= 0f || state.Height <= 0f)
        {
            LastOverlayCommandsSubmitted = 0;
            return;
        }

        if (MathF.Abs(state.VelocityX) > 0.01f)
        {
            _facing = MathF.Sign(state.VelocityX);
        }

        LastOverlayCommandsSubmitted = 0;
        DrawPlayer(state);
        DrawCrosshair();
        DrawTracer();
        Context.Lighting.RevealAround(_player.CenterX, _player.CenterY, 86f);
        Context.Lighting.AddPointLight(_player.CenterX, _player.CenterY, 96f, 0xFF_F8_DA_8C, 0.42f);
    }

    private void ResolveComponents()
    {
        if (_player is null && Entity.TryGetComponent(out PlayerController player))
        {
            _player = player;
        }

        if (_projectile is null && Entity.TryGetComponent(out PlayableProjectileTool projectile))
        {
            _projectile = projectile;
        }
    }

    private void DrawPlayer(CharacterState state)
    {
        Point2F topLeft = Context.Camera.WorldToScreen(state.X, state.Y);
        float scale = MathF.Max(1f, Context.Camera.Zoom);
        float width = MathF.Max(10f, state.Width * scale);
        float height = MathF.Max(18f, state.Height * scale);
        float x = topLeft.X;
        float y = topLeft.Y;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            return;
        }

        Context.Overlay.SolidRectangle(x + 1f, y + height - 3f, width + 4f, 3f, 0x80_00_00_00);
        LastOverlayCommandsSubmitted++;
        Context.Overlay.SolidRectangle(x, y, width, height, BodyColorBgra);
        LastOverlayCommandsSubmitted++;
        Context.Overlay.OutlineRectangle(x - 1f, y - 1f, width + 2f, height + 2f, 2f, OutlineColorBgra);
        LastOverlayCommandsSubmitted++;

        float eyeX = _facing >= 0f ? x + (width * 0.68f) : x + (width * 0.22f);
        float eyeY = y + (height * 0.26f);
        Context.Overlay.SolidRectangle(eyeX, eyeY, MathF.Max(2f, scale * 1.2f), MathF.Max(2f, scale * 1.2f), 0xFF_10_18_22);
        LastOverlayCommandsSubmitted++;

        if (!state.OnGround)
        {
            Context.Overlay.Line(x + (width * 0.5f), y + height, x + (width * 0.5f), y + height + 8f, 2f, 0xAA_FF_FF_FF);
            LastOverlayCommandsSubmitted++;
        }
    }

    private void DrawCrosshair()
    {
        (float mouseX, float mouseY) = Context.Input.MousePixel;
        RectF viewport = Context.Camera.Viewport;
        if (mouseX < -8f || mouseY < -8f || mouseX > viewport.Width + 8f || mouseY > viewport.Height + 8f)
        {
            return;
        }

        Context.Overlay.Line(mouseX - 7f, mouseY, mouseX - 2f, mouseY, 2f, CrosshairColorBgra);
        LastOverlayCommandsSubmitted++;
        Context.Overlay.Line(mouseX + 2f, mouseY, mouseX + 7f, mouseY, 2f, CrosshairColorBgra);
        LastOverlayCommandsSubmitted++;
        Context.Overlay.Line(mouseX, mouseY - 7f, mouseX, mouseY - 2f, 2f, CrosshairColorBgra);
        LastOverlayCommandsSubmitted++;
        Context.Overlay.Line(mouseX, mouseY + 2f, mouseX, mouseY + 7f, 2f, CrosshairColorBgra);
        LastOverlayCommandsSubmitted++;
    }

    private void DrawTracer()
    {
        if (_projectile is null || _projectile.TracerRemainingSeconds <= 0f)
        {
            return;
        }

        Point2F start = Context.Camera.WorldToScreen(_projectile.LastShotStartX, _projectile.LastShotStartY);
        Point2F end = Context.Camera.WorldToScreen(_projectile.LastHitX, _projectile.LastHitY);
        RectF viewport = Context.Camera.Viewport;
        if (!IntersectsViewport(
            MathF.Min(start.X, end.X) - 4f,
            MathF.Min(start.Y, end.Y) - 4f,
            MathF.Abs(end.X - start.X) + 8f,
            MathF.Abs(end.Y - start.Y) + 8f,
            viewport))
        {
            return;
        }

        Context.Overlay.Line(start.X, start.Y, end.X, end.Y, 3f, 0xFF_60_D8_FF);
        LastOverlayCommandsSubmitted++;
        Context.Overlay.Line(start.X, start.Y, end.X, end.Y, 1f, 0xFF_FF_FF_FF);
        LastOverlayCommandsSubmitted++;
    }

    private static bool IntersectsViewport(float x, float y, float width, float height, RectF viewport)
    {
        return x + width >= viewport.X &&
            y + height >= viewport.Y &&
            x <= viewport.X + viewport.Width &&
            y <= viewport.Y + viewport.Height;
    }
}
