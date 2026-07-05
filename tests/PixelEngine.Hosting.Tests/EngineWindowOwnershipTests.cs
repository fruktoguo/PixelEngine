using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 窗口与渲染管线所有权契约测试。
/// </summary>
public sealed class EngineWindowOwnershipTests
{
    /// <summary>
    /// 验证外部窗口重载不把窗口加入 Engine 拥有资源。
    /// </summary>
    [Fact]
    public void AttachWindowRuntimeExternalOverloadDoesNotTakeWindowOwnershipBySourceContract()
    {
        string source = ReadRepositoryFile("src", "PixelEngine.Hosting", "Engine.cs");

        Assert.Contains("public RenderWindow AttachWindowRuntime(RenderWindow window)", source, StringComparison.Ordinal);
        Assert.Contains("return AttachWindowRuntime(window, takeOwnership: false);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Engine 自建窗口路径仍由 Engine 接管窗口生命周期。
    /// </summary>
    [Fact]
    public void AttachWindowRuntimeCreatedWindowPathTakesWindowOwnershipBySourceContract()
    {
        string source = ReadRepositoryFile("src", "PixelEngine.Hosting", "Engine.cs");

        Assert.Contains("RenderWindow.Create(new RenderWindowOptions", source, StringComparison.Ordinal);
        Assert.Contains("return AttachWindowRuntime(window, takeOwnership: true);", source, StringComparison.Ordinal);
        Assert.Contains("if (takeOwnership)", source, StringComparison.Ordinal);
        Assert.Contains("_ownedRuntimeResources.Add(window);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Rendering 管线仍归 Engine 管理，但不隐式接管外部窗口。
    /// </summary>
    [Fact]
    public void AttachRenderingOwnsRenderPipelineButNotRenderWindowBySourceContract()
    {
        string source = ReadRepositoryFile("src", "PixelEngine.Hosting", "Engine.cs");

        Assert.Contains("public RenderPhaseDriver AttachRendering(RenderWindow window)", source, StringComparison.Ordinal);
        Assert.Contains("_ownedRuntimeResources.Add(pipeline);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ownedRuntimeResources.Add(window);", ExtractAttachRenderingBody(source), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 RmlUi 游戏 UI 走独立 present 层，ManagedFallback 仍复用中性 Gui bridge。
    /// </summary>
    [Fact]
    public void GameUiRuntimeRoutesDirectBackendsThroughUiLayerCompositorBySourceContract()
    {
        string source = ReadRepositoryFile("src", "PixelEngine.Hosting", "Engine.cs");
        string body = ExtractAttachGuiRuntimeBody(source);

        Assert.Contains("gameUi.BackendKind != UiBackendKind.ManagedFallback", body, StringComparison.Ordinal);
        Assert.Contains("UiLayerCompositor.Attach(pipeline, gameUi!)", body, StringComparison.Ordinal);
        Assert.Contains("gameUi.BackendKind == UiBackendKind.ManagedFallback", body, StringComparison.Ordinal);
        Assert.Contains("GuiRenderBridge.AttachIfEnabled", body, StringComparison.Ordinal);
        Assert.Contains("Action<IGuiDrawContext>? managedGui = gameUiNeedsGuiBridge ? gameUi!.DrawGui : null;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑态 bootstrap 只依赖中性 Gui/Rendering，不引用 Editor 程序集。
    /// </summary>
    [Fact]
    public void EditorHostBootstrapCreatesNeutralGuiHostWithoutEditorReferenceBySourceContract()
    {
        string source = ReadRepositoryFile("src", "PixelEngine.Hosting", "EditorHostBootstrap.cs");

        Assert.Contains("RenderWindow.Create(windowOptions, diagnostics)", source, StringComparison.Ordinal);
        Assert.Contains("GuiApp gui = new(new HexaImGuiBackend(), guiOptions);", source, StringComparison.Ordinal);
        Assert.Contains("GuiWindowInputConnector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PixelEngine.Editor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorApp", source, StringComparison.Ordinal);
    }

    private static string ExtractAttachRenderingBody(string source)
    {
        const string marker = "public RenderPhaseDriver AttachRendering(RenderWindow window)";
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "未找到 AttachRendering 方法。");
        int end = source.IndexOf("private void AttachGuiRuntime", start, StringComparison.Ordinal);
        Assert.True(end > start, "未找到 AttachRendering 方法结束边界。");
        return source[start..end];
    }

    private static string ExtractAttachGuiRuntimeBody(string source)
    {
        const string marker = "private void AttachGuiRuntime(RenderWindow window, RenderPipeline pipeline)";
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "未找到 AttachGuiRuntime 方法。");
        int end = source.IndexOf("private void AttachEditorHostExtensions", start, StringComparison.Ordinal);
        Assert.True(end > start, "未找到 AttachGuiRuntime 方法结束边界。");
        return source[start..end];
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        return File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
