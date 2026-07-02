using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 暂停菜单，经脚本公开 GUI / Runtime / Diagnostics API 控制运行与调试叠层。
/// </summary>
public sealed class PauseMenu : Behaviour
{
    private string _status = string.Empty;
    private long _lastEscapeFrame = -1;

    /// <summary>
    /// 菜单窗口宽度，单位像素。
    /// </summary>
    public float Width { get; set; } = 340f;

    /// <summary>
    /// 菜单窗口高度，单位像素。
    /// </summary>
    public float Height { get; set; } = 318f;

    /// <summary>
    /// 最近一次阻塞原因；为空表示菜单可提供已落地的控制项。
    /// </summary>
    public string BlockedReason { get; private set; } = "重开关卡快照尚未捕获。";

    /// <summary>
    /// 暂停菜单当前是否打开。
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        ToggleFromEscape();
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        ToggleFromEscape();
        if (!IsOpen)
        {
            return;
        }

        float x = MathF.Max(12f, (gui.Width - Width) * 0.5f);
        float y = MathF.Max(12f, (gui.Height - Height) * 0.5f);
        gui.SetNextWindow(x, y, Width, Height, GuiCondition.FirstUseEver);
        if (!gui.BeginWindow("demo-pause-menu", "暂停", GuiWindowFlags.NoResize | GuiWindowFlags.NoSavedSettings))
        {
            gui.EndWindow();
            return;
        }

        DrawControls(gui);
        gui.Separator();
        DrawOverlays(gui);
        gui.Separator();
        gui.TextColored(BlockedReason, 0xFF_40_80_FF);
        if (!string.IsNullOrEmpty(_status))
        {
            gui.Text(_status);
        }

        gui.EndWindow();
    }

    private void DrawControls(IGuiContext gui)
    {
        RuntimeControlSnapshot snapshot = Context.Runtime.Capture();
        gui.Text(snapshot.IsPlaying ? "状态: 运行" : "状态: 暂停");
        if (gui.Button("继续"))
        {
            CloseAndResume();
        }

        gui.SameLine();
        if (gui.Button("打开 Editor"))
        {
            RuntimeControlResult result = Context.Runtime.OpenEditor();
            _status = result.Message;
        }

        gui.SameLine();
        if (gui.Button("重开"))
        {
            RuntimeControlResult result = Context.Runtime.RequestRestartCurrentScene();
            _status = result.Message;
            if (!result.Success)
            {
                BlockedReason = result.Message;
            }
        }

        gui.SameLine();
        if (gui.Button("退出"))
        {
            RuntimeControlResult result = Context.Runtime.RequestShutdown();
            _status = result.Message;
        }
    }

    private void DrawOverlays(IGuiContext gui)
    {
        gui.Text("调试叠层");
        DrawOverlay(gui, DebugOverlayKind.DirtyRects, "dirty rect");
        DrawOverlay(gui, DebugOverlayKind.ChunkGridParity, "chunk parity");
        DrawOverlay(gui, DebugOverlayKind.KeepAliveHotspots, "KeepAlive");
        DrawOverlay(gui, DebugOverlayKind.CellParity, "cell parity");
        DrawOverlay(gui, DebugOverlayKind.TemperatureHeatmap, "temperature");
        DrawOverlay(gui, DebugOverlayKind.OwnedByBody, "owned body");
        DrawOverlay(gui, DebugOverlayKind.ParticleTrails, "particle trails");
        DrawOverlay(gui, DebugOverlayKind.ConnectedComponents, "CCL");
    }

    private void DrawOverlay(IGuiContext gui, DebugOverlayKind kind, string label)
    {
        bool enabled = Context.Diagnostics.IsOverlayEnabled(kind);
        if (gui.Checkbox(label, ref enabled))
        {
            Context.Diagnostics.SetOverlay(kind, enabled);
        }
    }

    private void ToggleFromEscape()
    {
        if (!Context.Input.WasPressed(Key.Escape))
        {
            return;
        }

        long frame = Context.Time.FrameCount;
        if (frame == _lastEscapeFrame)
        {
            return;
        }

        _lastEscapeFrame = frame;
        if (IsOpen)
        {
            CloseAndResume();
            return;
        }

        IsOpen = true;
        Context.Runtime.PauseSimulation();
        _status = "已暂停。";
    }

    private void CloseAndResume()
    {
        Context.Runtime.ResumeSimulation();
        IsOpen = false;
        _status = string.Empty;
    }
}
