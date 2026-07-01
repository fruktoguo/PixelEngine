using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

/// <summary>
/// 编辑/运行模式切换面板。
/// </summary>
/// <param name="service">Editor Play session 服务。</param>
public sealed class EditorModePanel(IEditorPlaySessionService service) : IEditorPanel
{
    private readonly IEditorPlaySessionService _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc />
    public string Title => EditorDockSpace.EditorModeWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次捕获的状态。
    /// </summary>
    public EditorPlaySessionSnapshot LastSnapshot { get; private set; }

    /// <summary>
    /// 最近一次切换结果。
    /// </summary>
    public EditorPlaySessionResult LastResult { get; private set; }

    /// <summary>
    /// 以当前 live 世界进入 Play。
    /// </summary>
    public void EnterPlayCurrent()
    {
        LastResult = _service.EnterPlayCurrent();
        LastSnapshot = LastResult.Snapshot;
    }

    /// <summary>
    /// 保存临时快照后进入 Play。
    /// </summary>
    public void EnterPlayTemporary()
    {
        LastResult = _service.EnterPlayTemporary();
        LastSnapshot = LastResult.Snapshot;
    }

    /// <summary>
    /// 返回 Edit，必要时恢复临时快照。
    /// </summary>
    public void ExitPlay()
    {
        LastResult = _service.ExitPlay();
        LastSnapshot = LastResult.Snapshot;
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
        LastSnapshot = _service.Capture();
        ImGui.TextUnformatted($"mode={LastSnapshot.Mode} source={LastSnapshot.Source}");
        if (LastSnapshot.Mode == EditorMode.Play)
        {
            if (ImGui.Button("Exit Play"))
            {
                ExitPlay();
            }
        }
        else
        {
            if (ImGui.Button("Play Current"))
            {
                EnterPlayCurrent();
            }

            ImGui.SameLine();
            if (ImGui.Button("Play Temp"))
            {
                EnterPlayTemporary();
            }
        }

        if (!string.IsNullOrWhiteSpace(LastResult.Message))
        {
            ImGui.TextUnformatted(LastResult.Message);
        }

        ImGui.End();
    }
}
