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
        GameViewWindowTitle,
        SceneHierarchyWindowTitle,
        AssetBrowserWindowTitle,
        InspectorWindowTitle,
        WorldInspectorWindowTitle,
        MaterialReactionEditorWindowTitle,
        DebugOverlayWindowTitle,
        SimulationControlWindowTitle,
        SaveLoadWindowTitle,
        PhysicsTuningWindowTitle,
        ParticleTuningWindowTitle,
        LightingTuningWindowTitle,
        EditorModeWindowTitle,
        PerformanceHudWindowTitle,
        ConsoleDiagnosticsWindowTitle,
        UiManifestWindowTitle,
        ProjectSettingsWindowTitle,
        PlayerSettingsWindowTitle,
        BuildSettingsWindowTitle,
    ];

    /// <summary>
    /// Scene View 世界视口窗口标题。
    /// </summary>
    public const string ViewportWindowTitle = "Scene";

    /// <summary>
    /// Game View 玩家视角窗口标题。
    /// </summary>
    public const string GameViewWindowTitle = "Game View";

    /// <summary>
    /// 场景层级窗口标题。
    /// </summary>
    public const string SceneHierarchyWindowTitle = "Hierarchy";

    /// <summary>
    /// 资源浏览器窗口标题。
    /// </summary>
    public const string AssetBrowserWindowTitle = "Project";

    /// <summary>
    /// Inspector 窗口标题。
    /// </summary>
    public const string InspectorWindowTitle = "Inspector";

    /// <summary>
    /// 世界检视器窗口标题。
    /// </summary>
    public const string WorldInspectorWindowTitle = "World Inspector";

    /// <summary>
    /// 材质与画刷窗口标题。
    /// </summary>
    public const string MaterialBrushWindowTitle = "Tools";

    /// <summary>
    /// 材质/反应编辑器窗口标题。
    /// </summary>
    public const string MaterialReactionEditorWindowTitle = "Materials";

    /// <summary>
    /// 调试叠层窗口标题。
    /// </summary>
    public const string DebugOverlayWindowTitle = "Overlays";

    /// <summary>
    /// sim 控制条窗口标题。
    /// </summary>
    public const string SimulationControlWindowTitle = "Simulation";

    /// <summary>
    /// 存读档窗口标题。
    /// </summary>
    public const string SaveLoadWindowTitle = "Save/Load";

    /// <summary>
    /// 物理调参窗口标题。
    /// </summary>
    public const string PhysicsTuningWindowTitle = "Physics";

    /// <summary>
    /// 粒子调参窗口标题。
    /// </summary>
    public const string ParticleTuningWindowTitle = "Particles";

    /// <summary>
    /// 光照调参窗口标题。
    /// </summary>
    public const string LightingTuningWindowTitle = "Lighting";

    /// <summary>
    /// 编辑/运行模式窗口标题。
    /// </summary>
    public const string EditorModeWindowTitle = "Play Mode";

    /// <summary>
    /// 性能 HUD 窗口标题。
    /// </summary>
    public const string PerformanceHudWindowTitle = "Profiler";

    /// <summary>
    /// 控制台与诊断窗口标题。
    /// </summary>
    public const string ConsoleDiagnosticsWindowTitle = "Console";

    /// <summary>
    /// UI manifest 管理窗口标题。
    /// </summary>
    public const string UiManifestWindowTitle = "UI Manifest";

    /// <summary>
    /// Project Settings 窗口标题。
    /// </summary>
    public const string ProjectSettingsWindowTitle = "Project Settings";

    /// <summary>
    /// Player Settings 窗口标题。
    /// </summary>
    public const string PlayerSettingsWindowTitle = "Player Settings";

    /// <summary>
    /// 构建与发布窗口标题。
    /// </summary>
    public const string BuildSettingsWindowTitle = "Build Settings";

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

        uint centerNode;
        uint rightRegion;
        uint middleRightNode;
        uint inspectorNode;
        uint hierarchyNode;
        uint projectNode;
        _ = ImGuiP.DockBuilderSplitNode(dockspaceId, ImGuiDir.Right, 0.48f, &rightRegion, &centerNode);
        _ = ImGuiP.DockBuilderSplitNode(rightRegion, ImGuiDir.Right, 0.52f, &inspectorNode, &middleRightNode);
        _ = ImGuiP.DockBuilderSplitNode(middleRightNode, ImGuiDir.Down, 0.48f, &projectNode, &hierarchyNode);

        ImGuiP.DockBuilderDockWindow(ViewportWindowTitle, centerNode);
        ImGuiP.DockBuilderDockWindow(GameViewWindowTitle, centerNode);
        ImGuiP.DockBuilderDockWindow(SceneHierarchyWindowTitle, hierarchyNode);
        ImGuiP.DockBuilderDockWindow(AssetBrowserWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(InspectorWindowTitle, inspectorNode);
        ImGuiP.DockBuilderDockWindow(ConsoleDiagnosticsWindowTitle, inspectorNode);
        ImGuiP.DockBuilderDockWindow(WorldInspectorWindowTitle, inspectorNode);
        ImGuiP.DockBuilderDockWindow(MaterialReactionEditorWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(DebugOverlayWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(SimulationControlWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(SaveLoadWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(PhysicsTuningWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(ParticleTuningWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(LightingTuningWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(EditorModeWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(PerformanceHudWindowTitle, inspectorNode);
        ImGuiP.DockBuilderDockWindow(UiManifestWindowTitle, projectNode);
        ImGuiP.DockBuilderDockWindow(BuildSettingsWindowTitle, projectNode);
        ImGuiP.DockBuilderFinish(dockspaceId);
    }
}
