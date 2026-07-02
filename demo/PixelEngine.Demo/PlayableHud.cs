using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩 Demo 的精简 HUD。
/// </summary>
public sealed class PlayableHud : Behaviour
{
    private PlayerHealth? _health;
    private PlayableProjectileTool? _projectile;

    /// <summary>
    /// HUD 左上角 X 坐标，单位像素。
    /// </summary>
    public float X { get; set; } = 14f;

    /// <summary>
    /// HUD 左上角 Y 坐标，单位像素。
    /// </summary>
    public float Y { get; set; } = 14f;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveComponents();
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        ResolveComponents();
        gui.SetNextWindow(X, Y, 330f, 126f, GuiCondition.FirstUseEver);
        GuiWindowFlags flags = GuiWindowFlags.NoResize |
            GuiWindowFlags.NoMove |
            GuiWindowFlags.NoSavedSettings |
            GuiWindowFlags.NoTitleBar;
        if (!gui.BeginWindow("playable-hud", "Playable HUD", flags))
        {
            gui.EndWindow();
            return;
        }

        DrawHealth(gui);
        gui.Text($"射击 {_projectile?.ShotsFired ?? 0}");
        EngineDiagnosticsSnapshot diagnostics = Context.Diagnostics.Capture();
        gui.Separator();
        float frameMs = diagnostics.FramesPerSecond <= 0.01
            ? 0f
            : 1000f / (float)diagnostics.FramesPerSecond;
        gui.Text($"Render FPS {diagnostics.FramesPerSecond:0.0}   {frameMs:0.0} ms");
        gui.Text($"Sim {diagnostics.SimHz:0}Hz   Frame {diagnostics.FrameCount}   Bodies {diagnostics.RigidBodies}");
        gui.Text($"Chunks {diagnostics.ActiveChunks}/{diagnostics.ResidentChunks}   Particles {diagnostics.FreeParticles}");
        gui.EndWindow();
    }

    private void ResolveComponents()
    {
        _health = Entity.TryGetComponent(out PlayerHealth health) ? health : null;
        _projectile = Entity.TryGetComponent(out PlayableProjectileTool projectile) ? projectile : null;
    }

    private void DrawHealth(IGuiContext gui)
    {
        if (_health is null)
        {
            gui.Text("生命 --");
            return;
        }

        float max = MathF.Max(1f, _health.MaxHealth);
        float value = Math.Clamp(_health.Health / max, 0f, 1f);
        gui.Text($"生命 {_health.Health:0}/{max:0}");
        gui.ProgressBar(value, $"{value:P0}");
    }
}
