namespace PixelEngine.Editor;

/// <summary>
/// Editor 停靠面板统一接口。
/// </summary>
public interface IEditorPanel
{
    /// <summary>
    /// 面板标题。
    /// </summary>
    string Title { get; }

    /// <summary>
    /// 面板是否可见。
    /// </summary>
    bool Visible { get; set; }

    /// <summary>
    /// 绘制面板内容。
    /// </summary>
    /// <param name="context">当前 Editor 上下文。</param>
    void Draw(in EditorContext context);
}

/// <summary>
/// DockSpace 创建前绘制的编辑器 chrome 面板，例如主菜单栏、全局工具栏与状态栏。
/// </summary>
public interface IEditorChromePanel : IEditorPanel;

/// <summary>
/// 可在同一 ImGui/OS 窗口内进入独占呈现模式的面板。
/// </summary>
/// <remarks>
/// 独占模式只抑制其他 dock 内容的绘制，不销毁 DockSpace 或其 ini 布局；退出后原停靠关系必须原样恢复。
/// </remarks>
public interface IEditorMaximizedPanel : IEditorPanel
{
    /// <summary>当前面板是否独占 Editor 内容区域。</summary>
    bool IsMaximized { get; }
}
