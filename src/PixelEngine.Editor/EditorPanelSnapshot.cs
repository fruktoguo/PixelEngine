namespace PixelEngine.Editor;

/// <summary>
/// 已注册 Editor panel 的稳定、只读语义快照。
/// </summary>
/// <param name="Id">不随本地化标题改变的稳定 panel ID。</param>
/// <param name="Title">panel canonical title。</param>
/// <param name="Visible">当前是否可见。</param>
/// <param name="Chrome">是否属于 dockspace 外的 chrome。</param>
/// <param name="Maximized">是否独占 Editor 内容区。</param>
/// <param name="FocusPending">是否已经请求下一次绘制聚焦。</param>
/// <param name="DockStateKnown">是否存在可读取的 ImGui window 运行态。</param>
/// <param name="Docked">窗口是否位于 dock node。</param>
/// <param name="DockGroupId">由同组稳定 panel IDs 派生的外部 group ID。</param>
/// <param name="X">window/dock node X。</param>
/// <param name="Y">window/dock node Y。</param>
/// <param name="Width">window/dock node 宽度。</param>
/// <param name="Height">window/dock node 高度。</param>
public readonly record struct EditorPanelSnapshot(
    string Id,
    string Title,
    bool Visible,
    bool Chrome,
    bool Maximized,
    bool FocusPending,
    bool DockStateKnown,
    bool Docked,
    string? DockGroupId,
    float X,
    float Y,
    float Width,
    float Height);
