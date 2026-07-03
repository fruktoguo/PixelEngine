using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩 Demo 的精简 HUD。
/// </summary>
public sealed class PlayableHud : Behaviour
{
    private const int GraphSampleCapacity = 48;

    private readonly float[] _frameGraphSamples = new float[GraphSampleCapacity];
    private readonly char[] _frameGraphText = new char[GraphSampleCapacity];
    private PlayerHealth? _health;
    private PlayableProjectileTool? _projectile;
    private int _frameGraphIndex;
    private int _frameGraphCount;

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
        gui.SetNextWindow(X, Y, 470f, 224f, GuiCondition.FirstUseEver);
        GuiWindowFlags flags = GuiWindowFlags.NoResize |
            GuiWindowFlags.NoMove |
            GuiWindowFlags.NoSavedSettings |
            GuiWindowFlags.NoTitleBar |
            GuiWindowFlags.NoScrollbar;
        if (!gui.BeginWindow("playable-hud", "Playable HUD", flags))
        {
            gui.EndWindow();
            return;
        }

        DrawHealth(gui);
        gui.Text($"射击 {_projectile?.ShotsFired ?? 0}");
        EngineDiagnosticsSnapshot diagnostics = Context.Diagnostics.Capture();
        PushFrameGraphSample(diagnostics);
        gui.Separator();
        gui.Text($"Render FPS {diagnostics.FramesPerSecond:0.0} avg   {diagnostics.FrameMilliseconds:0.0} ms");
        gui.Text($"1% low {diagnostics.FrameLow1PercentFps:0.0}   p99 {diagnostics.FrameP99Milliseconds:0.0} ms   jitter {diagnostics.FrameJitterMilliseconds:0.0}");
        gui.Text($"Frame graph {BuildFrameGraphText()}");
        gui.Text($"Sim {diagnostics.SimHz:0}Hz   Frame {diagnostics.FrameCount}   Bodies {diagnostics.RigidBodies}");
        gui.Text($"Chunks {diagnostics.ActiveChunks}/{diagnostics.ResidentChunks}   Particles {diagnostics.FreeParticles}");
        if (_projectile is not null)
        {
            gui.Text(
                $"Collapse {_projectile.CollapsedFloatingIslands}   {_projectile.CollapseStatus}   " +
                $"scan {_projectile.LastCollapseSolidCandidates}");
        }

        gui.EndWindow();
    }

    private void PushFrameGraphSample(EngineDiagnosticsSnapshot diagnostics)
    {
        float frameMs = diagnostics.FrameLastMilliseconds > 0.001f
            ? diagnostics.FrameLastMilliseconds
            : diagnostics.FrameMilliseconds;
        if (!float.IsFinite(frameMs) || frameMs <= 0)
        {
            return;
        }

        _frameGraphSamples[_frameGraphIndex] = MathF.Min(frameMs, 99f);
        _frameGraphIndex = (_frameGraphIndex + 1) % GraphSampleCapacity;
        if (_frameGraphCount < GraphSampleCapacity)
        {
            _frameGraphCount++;
        }
    }

    private string BuildFrameGraphText()
    {
        if (_frameGraphCount == 0)
        {
            return "";
        }

        for (int i = 0; i < GraphSampleCapacity; i++)
        {
            _frameGraphText[i] = ' ';
        }

        int start = (_frameGraphIndex - _frameGraphCount + GraphSampleCapacity) % GraphSampleCapacity;
        for (int i = 0; i < _frameGraphCount; i++)
        {
            float ms = _frameGraphSamples[(start + i) % GraphSampleCapacity];
            _frameGraphText[i] = ms switch
            {
                < 12f => '.',
                < 17f => '-',
                < 24f => '=',
                < 34f => '+',
                _ => '#',
            };
        }

        return new string(_frameGraphText, 0, _frameGraphCount);
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
