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
