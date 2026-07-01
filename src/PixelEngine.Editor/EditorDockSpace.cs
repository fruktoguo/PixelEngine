using Hexa.NET.ImGui;
using System.Numerics;

namespace PixelEngine.Editor;

/// <summary>
/// Dear ImGui docking 主机配置。
/// </summary>
public sealed class EditorDockSpace
{
    private static readonly string[] DefaultWindowTitles =
    [
        ViewportWindowTitle,
        SceneHierarchyWindowTitle,
        AssetBrowserWindowTitle,
        InspectorWindowTitle,
        WorldInspectorWindowTitle,
        MaterialBrushWindowTitle,
        PerformanceHudWindowTitle,
        ConsoleDiagnosticsWindowTitle,
    ];

    /// <summary>
    /// 中央世界视口窗口标题。
    /// </summary>
    public const string ViewportWindowTitle = "世界视口";

    /// <summary>
    /// 场景层级窗口标题。
    /// </summary>
    public const string SceneHierarchyWindowTitle = "场景层级";

    /// <summary>
    /// 资源浏览器窗口标题。
    /// </summary>
    public const string AssetBrowserWindowTitle = "资源浏览器";

    /// <summary>
    /// Inspector 窗口标题。
    /// </summary>
    public const string InspectorWindowTitle = "Inspector";

    /// <summary>
    /// 世界检视器窗口标题。
    /// </summary>
    public const string WorldInspectorWindowTitle = "世界检视器";

    /// <summary>
    /// 材质与画刷窗口标题。
    /// </summary>
    public const string MaterialBrushWindowTitle = "材质/画刷";

    /// <summary>
    /// 性能 HUD 窗口标题。
    /// </summary>
    public const string PerformanceHudWindowTitle = "性能 HUD";

    /// <summary>
    /// 控制台与诊断窗口标题。
    /// </summary>
    public const string ConsoleDiagnosticsWindowTitle = "控制台/诊断";

    private bool _layoutBuilt;

    /// <summary>
    /// 生成 Editor 使用的 ImGui config flags。
    /// </summary>
    /// <param name="enableMultiViewport">是否启用多视口。</param>
    /// <returns>ImGui config flags。</returns>
    public static ImGuiConfigFlags BuildConfigFlags(bool enableMultiViewport)
    {
        ImGuiConfigFlags flags = ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;
        if (enableMultiViewport)
        {
            flags |= ImGuiConfigFlags.ViewportsEnable;
        }

        return flags;
    }

    /// <summary>
    /// 返回默认布局中预注册的窗口标题。
    /// </summary>
    /// <returns>窗口标题集合。</returns>
    public static ReadOnlySpan<string> GetDefaultWindowTitles()
    {
        return DefaultWindowTitles.AsSpan();
    }

    /// <summary>
    /// 重置默认布局初始化状态。
    /// </summary>
    /// <param name="buildDefaultLayout">是否在下一帧构建默认布局。</param>
    public void ResetLayoutState(bool buildDefaultLayout)
    {
        _layoutBuilt = !buildDefaultLayout;
    }

    /// <summary>
    /// 绘制全窗口 dockspace。
    /// </summary>
    public void Draw()
    {
        uint dockspaceId = ImGui.DockSpaceOverViewport();
        if (!_layoutBuilt)
        {
            BuildDefaultLayout(dockspaceId, ImGui.GetMainViewport().Size);
            _layoutBuilt = true;
        }
    }

    private static unsafe void BuildDefaultLayout(uint dockspaceId, Vector2 viewportSize)
    {
        if (dockspaceId == 0)
        {
            return;
        }

        ImGuiP.DockBuilderRemoveNode(dockspaceId);
        _ = ImGuiP.DockBuilderAddNode(dockspaceId, ImGuiDockNodeFlags.None);
        ImGuiP.DockBuilderSetNodeSize(dockspaceId, viewportSize);

        uint leftNode;
        uint centerNode;
        uint rightNode;
        uint bottomNode;
        uint rightTopNode;
        uint rightBottomNode;
        _ = ImGuiP.DockBuilderSplitNode(dockspaceId, ImGuiDir.Left, 0.20f, &leftNode, &centerNode);
        _ = ImGuiP.DockBuilderSplitNode(centerNode, ImGuiDir.Right, 0.26f, &rightNode, &centerNode);
        _ = ImGuiP.DockBuilderSplitNode(centerNode, ImGuiDir.Down, 0.24f, &bottomNode, &centerNode);
        _ = ImGuiP.DockBuilderSplitNode(rightNode, ImGuiDir.Down, 0.34f, &rightBottomNode, &rightTopNode);

        ImGuiP.DockBuilderDockWindow(SceneHierarchyWindowTitle, leftNode);
        ImGuiP.DockBuilderDockWindow(AssetBrowserWindowTitle, leftNode);
        ImGuiP.DockBuilderDockWindow(ViewportWindowTitle, centerNode);
        ImGuiP.DockBuilderDockWindow(InspectorWindowTitle, rightTopNode);
        ImGuiP.DockBuilderDockWindow(WorldInspectorWindowTitle, rightTopNode);
        ImGuiP.DockBuilderDockWindow(MaterialBrushWindowTitle, rightBottomNode);
        ImGuiP.DockBuilderDockWindow(PerformanceHudWindowTitle, bottomNode);
        ImGuiP.DockBuilderDockWindow(ConsoleDiagnosticsWindowTitle, bottomNode);
        ImGuiP.DockBuilderFinish(dockspaceId);
    }
}
