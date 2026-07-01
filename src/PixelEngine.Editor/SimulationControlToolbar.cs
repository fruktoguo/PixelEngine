using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

/// <summary>
/// Editor 视角的模拟控制快照。
/// </summary>
/// <param name="IsPlaying">当前是否处于 Play 模式。</param>
/// <param name="SimHz">当前请求的 sim 频率。</param>
/// <param name="FrameIndex">当前渲染帧序号。</param>
/// <param name="SimTickIndex">当前 sim tick 序号。</param>
/// <param name="RunSimThisFrame">最近一帧是否执行 sim。</param>
public readonly record struct SimulationControlSnapshot(
    bool IsPlaying,
    double SimHz,
    long FrameIndex,
    long SimTickIndex,
    bool RunSimThisFrame);

/// <summary>
/// SimulationControlToolbar 操作的运行时控制接口。
/// </summary>
public interface ISimulationControlService
{
    /// <summary>
    /// 捕获当前控制状态。
    /// </summary>
    /// <returns>控制状态快照。</returns>
    SimulationControlSnapshot Capture();

    /// <summary>
    /// 进入 Play 模式。
    /// </summary>
    void EnterPlayMode();

    /// <summary>
    /// 进入 Edit/Pause 模式。
    /// </summary>
    void EnterEditMode();

    /// <summary>
    /// 从 Edit/Pause 模式执行恰好一个 sim tick。
    /// </summary>
    void StepOnce();

    /// <summary>
    /// 设置请求的 sim 频率。
    /// </summary>
    /// <param name="simHz">sim 频率。</param>
    void SetSimHz(double simHz);
}

/// <summary>
/// Play/Pause/单步/60-30Hz 控制条；所有 sim 决策委托给 Hosting/FrameClock。
/// </summary>
/// <param name="control">运行时控制服务。</param>
public sealed class SimulationControlToolbar(ISimulationControlService control) : IEditorPanel
{
    private readonly ISimulationControlService _control = control ?? throw new ArgumentNullException(nameof(control));

    /// <inheritdoc />
    public string Title => EditorDockSpace.SimulationControlWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次控制快照。
    /// </summary>
    public SimulationControlSnapshot LastSnapshot { get; private set; }

    /// <summary>
    /// 进入 Play 模式。
    /// </summary>
    public void Play()
    {
        _control.EnterPlayMode();
        LastSnapshot = _control.Capture();
    }

    /// <summary>
    /// 进入 Pause/Edit 模式。
    /// </summary>
    public void Pause()
    {
        _control.EnterEditMode();
        LastSnapshot = _control.Capture();
    }

    /// <summary>
    /// 在暂停态执行恰好一次 sim tick。
    /// </summary>
    public void StepOnce()
    {
        _control.StepOnce();
        LastSnapshot = _control.Capture();
    }

    /// <summary>
    /// 请求 60Hz sim。
    /// </summary>
    public void Use60Hz()
    {
        _control.SetSimHz(60.0);
        LastSnapshot = _control.Capture();
    }

    /// <summary>
    /// 请求 30Hz sim。
    /// </summary>
    public void Use30Hz()
    {
        _control.SetSimHz(30.0);
        LastSnapshot = _control.Capture();
    }

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        LastSnapshot = _control.Capture();
        DrawButtons();
        ImGui.TextUnformatted($"Frame {LastSnapshot.FrameIndex}  SimTick {LastSnapshot.SimTickIndex}  Sim {LastSnapshot.SimHz:F0}Hz  {(LastSnapshot.RunSimThisFrame ? "sim" : "render-only")}");
        ImGui.End();
    }

    private void DrawButtons()
    {
        if (LastSnapshot.IsPlaying)
        {
            if (ImGui.Button("Pause"))
            {
                Pause();
            }
        }
        else if (ImGui.Button("Play"))
        {
            Play();
        }

        if (!LastSnapshot.IsPlaying)
        {
            ImGui.SameLine();
            if (ImGui.Button("Step"))
            {
                StepOnce();
            }
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("60Hz", Math.Abs(LastSnapshot.SimHz - 60.0) < 0.001))
        {
            Use60Hz();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("30Hz", Math.Abs(LastSnapshot.SimHz - 30.0) < 0.001))
        {
            Use30Hz();
        }
    }
}
