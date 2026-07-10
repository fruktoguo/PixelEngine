using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 层工程纪律测试：solution 完整性、公开入口引用、正式输出与编辑器壳装配不变式。
/// 不变式：用户入口仅经 Hosting/Scripting/Editor 公开项目、WinExe 无控制台、产物可审计校验。
/// </summary>
public sealed class HostingProjectDisciplineTests
{
    /// <summary>
    /// 验证仓库内所有项目都登记在 solution 中，避免工具、测试或应用项目绕过常规 build/test 入口。
    /// </summary>
    [Fact]

    // —— 解决方案与项目引用纪律 ——
    public void SolutionTracksEveryRepositoryProject()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string solution = File.ReadAllText(Path.Combine(root, "PixelEngine.sln"));
        string[] projectFiles =
        [
            .. Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Where(static path => IsRepositoryProjectPath(path))
                .Select(path => Path.GetRelativePath(root, path).Replace('/', '\\'))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        string[] solutionProjects =
        [
            .. Regex.Matches(solution, "\"([^\"]+\\.csproj)\"")
                .Select(static match => match.Groups[1].Value)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        // Assert：验证预期结果
        Assert.Equal(projectFiles, solutionProjects, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证 Demo 只经 Hosting 与 Scripting 公开入口引用引擎。
    /// </summary>
    [Fact]
    public void DemoProjectReferencesOnlyPublicEntryProjects()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "demo", "PixelEngine.Demo", "PixelEngine.Demo.csproj"));

        Assert.Equal(
            ["PixelEngine.Hosting", "PixelEngine.Scripting"],
            [
                .. ReadIncludes(project, "ProjectReference")
                    .Select(include => Path.GetFileNameWithoutExtension(include)!),
            ]);
        Assert.Empty(ReadIncludes(project, "PackageReference"));
    }

    /// <summary>
    /// 验证独立编辑器壳位于 apps 层，只引用 Hosting、Editor 与 Gui 三个公开装配入口。
    /// </summary>
    [Fact]
    public void EditorShellProjectReferencesOnlyShellEntryProjects()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "PixelEngine.Editor.Shell.csproj"));

        Assert.Equal(
            ["PixelEngine.Hosting", "PixelEngine.Editor", "PixelEngine.Gui"],
            [
                .. ReadIncludes(project, "ProjectReference")
                    .Select(include => Path.GetFileNameWithoutExtension(include)!),
            ]);
    }

    /// <summary>
    /// 验证面向用户直接启动的编辑器与 Demo 都使用 Windows GUI 子系统，避免发行包启动时弹出控制台窗口。
    /// </summary>
    [Fact]
    public void UserFacingEntryPointsUseWindowsGuiSubsystem()
    {
        string root = FindRepositoryRoot();
        XDocument editor = XDocument.Load(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "PixelEngine.Editor.Shell.csproj"));
        XDocument demo = XDocument.Load(Path.Combine(root, "demo", "PixelEngine.Demo", "PixelEngine.Demo.csproj"));

        Assert.Equal("WinExe", editor.Descendants("OutputType").Single().Value);
        Assert.Equal("WinExe", demo.Descendants("OutputType").Single().Value);
    }

    /// <summary>
    /// 验证用户入口显式固定本机目标场景定档的 Workstation + Concurrent GC 组合。
    /// </summary>
    [Fact]
    public void UserFacingEntryPointsPinWorkstationConcurrentGc()
    {
        string root = FindRepositoryRoot();
        XDocument editor = XDocument.Load(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "PixelEngine.Editor.Shell.csproj"));
        XDocument demo = XDocument.Load(Path.Combine(root, "demo", "PixelEngine.Demo", "PixelEngine.Demo.csproj"));

        Assert.Equal("false", editor.Descendants("ServerGarbageCollection").Single().Value);
        Assert.Equal("true", editor.Descendants("ConcurrentGarbageCollection").Single().Value);
        Assert.Equal("false", demo.Descendants("ServerGarbageCollection").Single().Value);
        Assert.Equal("true", demo.Descendants("ConcurrentGarbageCollection").Single().Value);
    }

    /// <summary>
    /// 验证正式输出验证链路与 Editor Build And Run 都隐藏子进程窗口，避免用户启动正式应用时看到控制台窗口。
    /// </summary>
    [Fact]
    public void UserFacingLaunchersCreateNoConsoleWindows()
    {
        string root = FindRepositoryRoot();
        string buildSettingsPanel = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "Build", "BuildSettingsPanel.cs"));
        string finalOutputScript = File.ReadAllText(Path.Combine(root, "tools", "update-final-output.ps1"));

        Assert.Contains("UseShellExecute = false", buildSettingsPanel, StringComparison.Ordinal);
        Assert.Contains("CreateNoWindow = true", buildSettingsPanel, StringComparison.Ordinal);
        Assert.Contains("$psi.UseShellExecute = $false", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("$psi.CreateNoWindow = $true", finalOutputScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证正式输出 Demo 默认请求 Web-first UI 产品主路径 RmlUi，并把请求后端写入验证记录。
    /// </summary>
    [Fact]

    // —— 正式输出与玩家包构建 ——
    public void FinalOutputDemoRequestsRmlUiProductUiBackend()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string finalOutputScript = File.ReadAllText(Path.Combine(root, "tools", "update-final-output.ps1"));
        string buildPlayerPs1 = File.ReadAllText(Path.Combine(root, "tools", "build-player.ps1"));
        string buildPlayerSh = File.ReadAllText(Path.Combine(root, "tools", "build-player.sh"));

        // Assert：验证预期结果
        Assert.Contains("[string]$DemoRuntimeUiBackend = 'RmlUi'", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("'-RuntimeUiBackend', $DemoRuntimeUiBackend", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("demoRuntimeUiBackendRequested = $DemoRuntimeUiBackend", finalOutputScript, StringComparison.Ordinal);
        Assert.DoesNotContain("'-RuntimeUiBackend', 'ManagedFallback'", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("runtimeUiBackend = $RuntimeUiBackend", buildPlayerPs1, StringComparison.Ordinal);
        Assert.Contains("\"runtimeUiBackend\"", buildPlayerSh, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证本机正式输出会写出根级 SHA256SUMS，并在 manifest / README 中登记，便于人工验收绑定到已验证产物。
    /// </summary>
    [Fact]
    public void FinalOutputWritesAuditableChecksumManifest()
    {
        string root = FindRepositoryRoot();
        string finalOutputScript = File.ReadAllText(Path.Combine(root, "tools", "update-final-output.ps1"));

        Assert.Contains("function Write-FinalOutputChecksums", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $fileFull -Algorithm SHA256", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $outputFull -Value $lines -Encoding UTF8", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("checksumFile = 'SHA256SUMS'", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("Write-FinalOutputChecksums $nextRoot (Join-Path $nextRoot 'SHA256SUMS')", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("完整性校验：SHA256SUMS", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("Write-Host \"完整性校验：$(Join-Path $outputRootFull 'SHA256SUMS')\"", finalOutputScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证正式输出更新在替换目录前会调用独立 verifier 审计待发布目录，避免更新脚本和审计脚本口径漂移。
    /// </summary>
    [Fact]
    public void FinalOutputUpdateRunsVerifierBeforeReplace()
    {
        string root = FindRepositoryRoot();
        string finalOutputScript = File.ReadAllText(Path.Combine(root, "tools", "update-final-output.ps1"));

        int replaceIndex = finalOutputScript.IndexOf("Replace-FinalOutput $nextRoot $outputRootFull", StringComparison.Ordinal);
        int verifyIndex = finalOutputScript.IndexOf("-Name 'verify-final-output'", StringComparison.Ordinal);

        Assert.True(replaceIndex >= 0, "update-final-output.ps1 应包含原子替换正式输出目录步骤。");
        Assert.True(verifyIndex >= 0 && verifyIndex < replaceIndex, "update-final-output.ps1 应在替换前调用独立 verifier，失败时保留旧正式输出。");
        Assert.Contains("tools/verify-final-output.ps1", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("'-OutputRoot', $nextRoot", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("verify-final-output.stdout.log", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("独立审计：$($verifyFinalOutputResult.StdoutPath)", finalOutputScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证本机正式输出提供独立审计入口，可在不重新打包的情况下校验 manifest、入口和 SHA256SUMS。
    /// </summary>
    [Fact]
    public void FinalOutputVerifierAuditsExistingOutputWithoutRepackaging()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string verifier = File.ReadAllText(Path.Combine(root, "tools", "verify-final-output.ps1"));
        string finalOutputDoc = File.ReadAllText(Path.Combine(root, "docs", "final-output.md"));
        string plan15 = File.ReadAllText(Path.Combine(root, "plan", "15-build-packaging-distribution.md"));

        // Assert：验证预期结果
        Assert.Contains("pixelengine.final-output-verify/v1", verifier, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $filePath -Algorithm SHA256", verifier, StringComparison.Ordinal);
        Assert.Contains("git -C $repoRoot rev-parse HEAD", verifier, StringComparison.Ordinal);
        Assert.Contains("sourceWorktreePolicy -ne 'tracked-clean-required'", verifier, StringComparison.Ordinal);
        Assert.Contains("sourceTrackedWorktreeClean -ne $true", verifier, StringComparison.Ordinal);
        Assert.Contains("editorExecutable", verifier, StringComparison.Ordinal);
        Assert.Contains("demoExecutable", verifier, StringComparison.Ordinal);
        Assert.Contains("demo-build-result.json 不是 ok=true", verifier, StringComparison.Ordinal);
        Assert.Contains("editor_default_workbench_probe ", verifier, StringComparison.Ordinal);
        Assert.Contains("window_frame_probe", verifier, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Demo", verifier, StringComparison.Ordinal);
        Assert.Contains("Assert-ChecksumContains $relativePaths $manifestRelative 'manifest'", verifier, StringComparison.Ordinal);
        Assert.Contains("正式输出包含未登记文件", verifier, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS 登记了不存在的文件", verifier, StringComparison.Ordinal);
        Assert.Contains(".Extension.Equals('.pdb'", verifier, StringComparison.Ordinal);
        Assert.Contains(".Extension.Equals('.xml'", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("build-player.ps1", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Replace-FinalOutput", verifier, StringComparison.Ordinal);

        Assert.Contains("pwsh -NoProfile -File tools/verify-final-output.ps1", finalOutputDoc, StringComparison.Ordinal);
        Assert.Contains("不重新打包", finalOutputDoc, StringComparison.Ordinal);
        Assert.Contains("未登记的额外文件", finalOutputDoc, StringComparison.Ordinal);
        Assert.Contains("本机正式输出审计命令", plan15, StringComparison.Ordinal);
        Assert.Contains("未登记额外文件", plan15, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证正式输出审计脚本会真实拒绝目录文件集与 SHA256SUMS 清单不一致的产物。
    /// </summary>
    [Fact]
    public void FinalOutputVerifierRejectsDirectoryAndChecksumSetDrift()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string outputRoot = Path.Combine(Path.GetTempPath(), "pixelengine-final-output-" + Guid.NewGuid().ToString("N"));
        string verifier = Path.Combine(root, "tools", "verify-final-output.ps1");
        try
        {
            WriteMinimalFinalOutput(outputRoot, ReadCurrentGitHead(root));
            ProcessResult clean = RunPowerShellScriptRaw(root, verifier, "-OutputRoot", outputRoot);
            // Assert：验证预期结果
            Assert.Equal(0, clean.ExitCode);
            Assert.Contains("final_output_verify schema=pixelengine.final-output-verify/v1, ok=True", clean.Stdout, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(outputRoot, "未登记.txt"), "extra");
            ProcessResult extra = RunPowerShellScriptRaw(root, verifier, "-OutputRoot", outputRoot);
            Assert.NotEqual(0, extra.ExitCode);
            Assert.Contains("正式输出包含未登记文件", extra.CombinedOutput, StringComparison.Ordinal);

            File.Delete(Path.Combine(outputRoot, "未登记.txt"));
            File.AppendAllText(Path.Combine(outputRoot, "SHA256SUMS"), Environment.NewLine + new string('0', 64) + "  不存在.txt" + Environment.NewLine);
            ProcessResult missing = RunPowerShellScriptRaw(root, verifier, "-OutputRoot", outputRoot);
            Assert.NotEqual(0, missing.ExitCode);
            Assert.Contains("SHA256SUMS 登记了不存在的文件", missing.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证正式输出审计脚本不会只相信 manifest 布尔值；验证日志必须包含真实 probe 成功 marker。
    /// </summary>
    [Fact]
    public void FinalOutputVerifierRejectsProbeLogsMissingSuccessMarkers()
    {
        string root = FindRepositoryRoot();
        string outputRoot = Path.Combine(Path.GetTempPath(), "pixelengine-final-output-" + Guid.NewGuid().ToString("N"));
        string verifier = Path.Combine(root, "tools", "verify-final-output.ps1");
        try
        {
            WriteMinimalFinalOutput(outputRoot, ReadCurrentGitHead(root));
            WriteTextFile(outputRoot, "_验证记录/logs/editor-default-workbench.stdout.log", "editor stdout without structured success marker");
            WriteFinalOutputChecksums(outputRoot);

            ProcessResult editorMissing = RunPowerShellScriptRaw(root, verifier, "-OutputRoot", outputRoot);

            Assert.NotEqual(0, editorMissing.ExitCode);
            Assert.Contains("编辑器默认工作台 probe stdout 缺少成功摘要", editorMissing.CombinedOutput, StringComparison.Ordinal);

            WriteMinimalFinalOutput(outputRoot, ReadCurrentGitHead(root));
            WriteTextFile(outputRoot, "_验证记录/logs/demo-window.stdout.log", "demo stdout without frame marker");
            WriteFinalOutputChecksums(outputRoot);

            ProcessResult demoMissing = RunPowerShellScriptRaw(root, verifier, "-OutputRoot", outputRoot);

            Assert.NotEqual(0, demoMissing.ExitCode);
            Assert.Contains("Demo 窗口 probe stdout 缺少验证标记", demoMissing.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证本机正式输出只能从已提交的已跟踪源码生成，避免未提交改动被误发布成正式版。
    /// </summary>
    [Fact]
    public void FinalOutputRequiresCleanTrackedWorktree()
    {
        string root = FindRepositoryRoot();
        string finalOutputScript = File.ReadAllText(Path.Combine(root, "tools", "update-final-output.ps1"));

        Assert.Contains("function Assert-CleanTrackedWorktree", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("git -C $repoRoot status --porcelain --untracked-files=no", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("正式输出需要干净的已跟踪工作树", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("Assert-CleanTrackedWorktree", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("sourceWorktreePolicy = 'tracked-clean-required'", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("sourceTrackedWorktreeClean = $true", finalOutputScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证本机正式输出默认清理编辑器开发元数据，只在显式诊断开关下保留符号。
    /// </summary>
    [Fact]
    public void FinalOutputPrunesEditorDeveloperMetadataByDefault()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string finalOutputScript = File.ReadAllText(Path.Combine(root, "tools", "update-final-output.ps1"));

        // Assert：验证预期结果
        Assert.Contains("[switch]$IncludeEditorSymbols", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("function Remove-EditorDeveloperMetadata", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains(".Extension.Equals('.pdb'", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains(".Extension.Equals('.xml'", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("if (-not $IncludeEditorSymbols.IsPresent)", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("Remove-EditorDeveloperMetadata $finalEditorDir", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("editorSymbolsIncluded = $IncludeEditorSymbols.IsPresent", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("editorDeveloperMetadataPolicy = if ($IncludeEditorSymbols.IsPresent) { 'included-for-diagnostics' } else { 'pdb-and-xml-pruned' }", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("-Name 'native-build'", finalOutputScript, StringComparison.Ordinal);
        Assert.Contains("tools/build-native.ps1", finalOutputScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-Tail 80 -Raw", finalOutputScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳只通过中性 bootstrap 创建唯一窗口，不直接散落创建 RenderWindow。
    /// </summary>
    [Fact]

    // —— 编辑器壳与 UI 后端 ——
    public void EditorShellCreatesWindowOnlyThroughNeutralBootstrap()
    {
        string root = FindRepositoryRoot();
        string shellSource = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "apps", "PixelEngine.Editor.Shell"), "*.cs").Select(File.ReadAllText));

        Assert.Contains("EditorShellWindow.Create(Preferences.Current.UiScale)", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorHostBootstrap.Create", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderWindow.Create", shellSource.Replace("EditorHostBootstrap.Create", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证项目选择器交出窗口给 Editor Engine 前会解绑中性 GUI 输入连接器，避免旧 ImGui context 继续抢输入。
    /// </summary>
    [Fact]
    public void EditorShellDisposesProjectPickerInputConnectorBeforeEngineAttach()
    {
        string root = FindRepositoryRoot();
        string shellWindow = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellWindow.cs"));
        string bootstrap = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "EditorHostBootstrap.cs"));

        Assert.Contains("public void DisposeInputConnector()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("_inputConnectorDisposed", bootstrap, StringComparison.Ordinal);
        Assert.Contains("_inputConnector.Dispose();", bootstrap, StringComparison.Ordinal);
        Assert.Contains("_bootstrap.DisposeInputConnector();", shellWindow, StringComparison.Ordinal);
        Assert.Contains("_projectPickerGuiShutdown", shellWindow, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证中性 Gui 与 Editor 输入连接器在点击前按当前 framebuffer scale 刷新鼠标坐标，避免 DPI/窗口切换后按钮命中使用旧位置。
    /// </summary>
    [Fact]
    public void GuiInputConnectorsRefreshPointerPositionBeforeMouseButtons()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string guiConnector = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Gui", "GuiWindowInputConnector.cs"));
        string editorConnector = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorWindowInputConnector.cs"));

        foreach (string source in new[] { guiConnector, editorConnector })
        {
            string compact = source.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            // Assert：验证预期结果
            Assert.Contains("privatevoidForwardMousePosition(Vector2position)", compact, StringComparison.Ordinal);
            Assert.Contains("_input.MouseMoveFramebuffer(position.X*_window.FramebufferScaleX,position.Y*_window.FramebufferScaleY);", compact, StringComparison.Ordinal);
            Assert.Contains("privatevoidOnMouseDown(IMousemouse,MouseButtonbutton){ForwardMousePosition(mouse.Position);_input.MouseButton(button,down:true);}", compact, StringComparison.Ordinal);
            Assert.Contains("privatevoidOnMouseUp(IMousemouse,MouseButtonbutton){ForwardMousePosition(mouse.Position);_input.MouseButton(button,down:false);}", compact, StringComparison.Ordinal);
            Assert.DoesNotContain("_input.MouseMove(position.X,position.Y);", compact, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 验证中性 Gui 与 Editor ImGui 后端在输入、frame 与 render 前都 pin 到自己的 native context。
    /// </summary>
    [Fact]
    public void HexaImGuiBackendsPinCurrentContextBeforeFrameRenderAndInput()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string guiBackend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Gui", "HexaImGuiBackend.cs"));
        string editorBackend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "HexaImGuiBackend.cs"));

        foreach (string source in new[] { guiBackend, editorBackend })
        {
            string compact = source.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            // Assert：验证预期结果
            Assert.Contains("private void SetCurrentContext()", source, StringComparison.Ordinal);
            Assert.Contains("ImGui.SetCurrentContext(_context);", source, StringComparison.Ordinal);
            Assert.Contains("ImGuiImplOpenGL3.SetCurrentContext(_context);", source, StringComparison.Ordinal);
            Assert.Contains("SetCurrentContext();ImGui.AddMouseButtonEvent", compact, StringComparison.Ordinal);
            Assert.Contains("SetCurrentContext();ImGui.AddMouseWheelEvent", compact, StringComparison.Ordinal);
            Assert.Contains("SetCurrentContext();ImGui.AddKeyEvent", compact, StringComparison.Ordinal);
            Assert.Contains("SetCurrentContext();ImGui.Render();", compact, StringComparison.Ordinal);
        }

        Assert.Contains("ImGuizmo.SetImGuiContext(_context);", editorBackend, StringComparison.Ordinal);
        Assert.Contains("ImPlot.SetCurrentContext(_plotContext);", editorBackend, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证两个 Hexa ImGui 后端都把 Silk.NET 逻辑鼠标坐标映射到 framebuffer 坐标后再交给 ImGui。
    /// </summary>
    [Fact]
    public void HexaImGuiBackendsMapLogicalMouseCoordinatesToFramebuffer()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string guiBackend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Gui", "HexaImGuiBackend.cs"));
        string editorBackend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "HexaImGuiBackend.cs"));
        string guiBridge = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Gui", "GuiInputBridge.cs"));
        string editorBridge = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "ImGuiInputBridge.cs"));
        string guiInterface = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Gui", "IGuiImGuiBackend.cs"));
        string editorInterface = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "IEditorImGuiBackend.cs"));

        foreach (string source in new[] { guiBackend, editorBackend })
        {
            string compact = source.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            // Assert：验证预期结果
            Assert.Contains("ImGuiFrameMetrics.Create(width,height,framebufferScaleX,framebufferScaleY)", compact, StringComparison.Ordinal);
            Assert.Contains("Vector2mapped=_frameMetrics.MapMousePosition(x,y);", compact, StringComparison.Ordinal);
            Assert.Contains("ImGui.AddMousePosEvent(ImGui.GetIO(),mapped.X,mapped.Y);", compact, StringComparison.Ordinal);
            Assert.Contains("io.DisplaySize=metrics.DisplaySize;", compact, StringComparison.Ordinal);
            Assert.Contains("io.DisplayFramebufferScale=metrics.DisplayFramebufferScale;", compact, StringComparison.Ordinal);
            Assert.Contains("publicvoidAddFramebufferMousePosition(floatx,floaty)", compact, StringComparison.Ordinal);
            Assert.Contains("ImGui.AddMousePosEvent(ImGui.GetIO(),x,y);", compact, StringComparison.Ordinal);
        }

        foreach (string source in new[] { guiBridge, editorBridge })
        {
            string compact = source.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            Assert.Contains("publicvoidMouseMoveFramebuffer(floatx,floaty)", compact, StringComparison.Ordinal);
            Assert.Contains("_backend.AddFramebufferMousePosition(x,y);", compact, StringComparison.Ordinal);
        }

        Assert.Contains("void AddFramebufferMousePosition(float x, float y);", guiInterface, StringComparison.Ordinal);
        Assert.Contains("void AddFramebufferMousePosition(float x, float y);", editorInterface, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证独立编辑器壳已经接入工程模型、最近工程、项目选择器、主菜单和布局宿主。
    /// </summary>
    [Fact]
    public void EditorShellWiresProjectPickerMenuAndLayout()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string shellSource = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));

        // Assert：验证预期结果
        Assert.Contains("project.pixelproj", shellSource, StringComparison.Ordinal);
        Assert.Contains("EngineProject", shellSource, StringComparison.Ordinal);
        Assert.Contains("RecentProjectsStore.LoadDefault()", shellSource, StringComparison.Ordinal);
        Assert.Contains("ProjectPicker.Draw(this)", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorMainMenuBar", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorShellLayout", shellSource, StringComparison.Ordinal);
        Assert.Contains("EditorDockSpace", shellSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证项目选择器的 Browse 按钮接入 Windows 原生文件夹选择器，并会把当前输入路径作为默认目录。
    /// </summary>
    [Fact]

    // —— 解决方案与项目引用纪律 ——
    public void EditorShellProjectPickerUsesNativeFolderDialogWithInitialPath()
    {
        string root = FindRepositoryRoot();
        string projectPicker = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "ProjectPickerWindow.cs"));
        string picker = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "NativeFolderPicker.cs"));

        Assert.Contains("IProjectFolderPicker", projectPicker, StringComparison.Ordinal);
        Assert.Contains("NativeProjectFolderPicker.Instance", projectPicker, StringComparison.Ordinal);
        Assert.Contains("_folderPicker.TryPickFolder(path", projectPicker, StringComparison.Ordinal);
        Assert.Contains("NativeFolderPicker.TryPickFolder(initialPath", projectPicker, StringComparison.Ordinal);
        Assert.Contains("SHCreateItemFromParsingName", picker, StringComparison.Ordinal);
        Assert.Contains("SetDefaultFolder(defaultFolder.Instance)", picker, StringComparison.Ordinal);
        Assert.Contains("SetFolder(defaultFolder.Instance)", picker, StringComparison.Ordinal);
        Assert.Contains("Path.GetDirectoryName(Path.GetFullPath(initialPath))", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = initialPath;", picker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Shell 菜单覆盖 plan/19 要求的顶级菜单，并包含 Build Settings 与 Reset Layout 入口。
    /// </summary>
    [Fact]

    // —— 编辑器壳与 UI 后端 ——
    public void EditorShellMainMenuDeclaresRequiredMenus()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string menu = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorMainMenuBar.cs"));
        string layout = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellLayout.cs"));
        string dockSpace = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "EditorDockSpace.cs"));
        string host = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellHostExtension.cs"));

        foreach (string item in new[]
        {
            "File",
            "Edit",
            "GameObject",
            "Window",
            "Play",
            "Help",
            "Open Recent",
            "New Scene",
            "Open Scene",
            "Build Settings...",
            "Exit",
            "Delete",
            "Duplicate",
            "Create Empty",
            "Create Empty Child",
            "Create with Component",
            "Rename",
            "Reset Layout",
            "Hierarchy",
            "Scene View",
            "Inspector",
            "Project",
            "Console",
            "Profiler",
            "About",
            "Shortcuts",
            "Preferences...",
            "Analysis",
            "Settings",
            "Tools",
            "New Project",
            "Open Project",
            "Save Scene",
            "Build",
            "Pause",
            "Step",
        })
        {
            // Assert：验证预期结果
            Assert.Contains(item, menu, StringComparison.Ordinal);
        }

        Assert.Contains("DrawPanelMenuItem", menu, StringComparison.Ordinal);
        Assert.Contains("app.ShowPanel(panelTitle)", menu, StringComparison.Ordinal);
        Assert.Contains("DrawToolbar(app)", menu, StringComparison.Ordinal);
        Assert.Contains("EditorMainToolbarState", menu, StringComparison.Ordinal);
        Assert.Contains("CaptureToolbarState", menu, StringComparison.Ordinal);
        Assert.Contains("##PixelEngineEditorToolbar", menu, StringComparison.Ordinal);
        Assert.Contains("ImGuiP.BeginViewportSideBar", menu, StringComparison.Ordinal);
        Assert.Contains("ImGuiDir.Up", menu, StringComparison.Ordinal);
        Assert.Contains("ToolbarHeight", menu, StringComparison.Ordinal);
        Assert.Contains("IEditorChromePanel", host, StringComparison.Ordinal);
        Assert.Contains("DrawPanels(in context, chromeOnly: true)", File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "EditorApp.cs")), StringComparison.Ordinal);
        Assert.Contains("app.FocusProjectPicker(ProjectPickerMode.NewProject)", menu, StringComparison.Ordinal);
        Assert.Contains("app.FocusProjectPicker(ProjectPickerMode.OpenProject)", menu, StringComparison.Ordinal);
        Assert.Contains("app.SaveScene()", menu, StringComparison.Ordinal);
        Assert.Contains("app.ShowBuildSettings()", menu, StringComparison.Ordinal);
        Assert.Contains("app.EnterPlayMode()", menu, StringComparison.Ordinal);
        Assert.Contains("app.EnterEditMode()", menu, StringComparison.Ordinal);
        Assert.Contains("app.StepOnce()", menu, StringComparison.Ordinal);
        Assert.Contains("StatusText", menu, StringComparison.Ordinal);
        Assert.Contains("SceneModel.IsDirty", menu, StringComparison.Ordinal);
        Assert.Contains("CaptureEditorPlaySession", menu, StringComparison.Ordinal);
        Assert.Contains("app.NewScene()", menu, StringComparison.Ordinal);
        Assert.Contains("app.OpenScene(scene.Path)", menu, StringComparison.Ordinal);
        Assert.Contains("app.RequestExit()", menu, StringComparison.Ordinal);
        Assert.Contains("app.DeleteSelectedGameObject()", menu, StringComparison.Ordinal);
        Assert.Contains("app.DuplicateSelectedGameObject()", menu, StringComparison.Ordinal);
        Assert.Contains("app.RenameSelectedGameObject()", menu, StringComparison.Ordinal);
        Assert.Contains("app.CreateChildGameObject()", menu, StringComparison.Ordinal);
        Assert.Contains("app.AddComponentToSelected", menu, StringComparison.Ordinal);
        Assert.Contains("public const string BuildSettingsWindowTitle", dockSpace, StringComparison.Ordinal);
        Assert.Contains("ViewportWindowTitle = \"Scene\"", dockSpace, StringComparison.Ordinal);
        Assert.Contains("SceneHierarchyWindowTitle = \"Hierarchy\"", dockSpace, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserWindowTitle = \"Project\"", dockSpace, StringComparison.Ordinal);
        Assert.Contains("ConsoleDiagnosticsWindowTitle = \"Console\"", dockSpace, StringComparison.Ordinal);
        Assert.Contains("UiManifestWindowTitle = \"UI Manifest\"", dockSpace, StringComparison.Ordinal);
        Assert.Contains("PerformanceHudWindowTitle = \"Profiler\"", dockSpace, StringComparison.Ordinal);
        Assert.Contains("DockBuilderDockWindow(BuildSettingsWindowTitle", dockSpace, StringComparison.Ordinal);
        Assert.Contains("DockBuilderDockWindow(AssetBrowserWindowTitle, bottomNode)", dockSpace, StringComparison.Ordinal);
        Assert.Contains("DockBuilderDockWindow(UiManifestWindowTitle, bottomNode)", dockSpace, StringComparison.Ordinal);
        Assert.Contains("File.Delete(LayoutPath)", layout, StringComparison.Ordinal);
        Assert.Contains("ResetLayoutState(buildDefaultLayout: true)", layout, StringComparison.Ordinal);
        Assert.Contains("new EditorConsolePanel(_app)", host, StringComparison.Ordinal);
        Assert.Contains("new UiManifestPanel(new EditorAssetManifestStore(_project))", host, StringComparison.Ordinal);
        Assert.Contains("new BuildSettingsPanel(_project, console: _app.ConsoleStore)", host, StringComparison.Ordinal);
        string assetBrowserDataSource = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "AssetBrowserDataSource.cs"));
        string assetBrowserPanel = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "AssetBrowserPanel.cs"));
        string shellAssetBrowserDataSource = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorAssetBrowserDataSource.cs"));
        string shellAssetManifestStore = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorAssetManifestStore.cs"));
        Assert.Contains("AssetBrowserCreateRequest", assetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserImportRequest", assetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserImportSourcePickResult", assetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("TryCreateAsset", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("TryImportAsset", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("TryPickImportSource", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserItemKind.Folder", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserItemKind.Material", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserItemKind.UiScreen", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserItemKind.Texture", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserItemKind.Audio", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("\"Folder\"", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("\"Material\"", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("\"UI Screen\"", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("Browse Source", assetBrowserPanel, StringComparison.Ordinal);
        Assert.Contains("createAsset: assetBrowserDataSource.CreateAsset", host, StringComparison.Ordinal);
        Assert.Contains("importAsset: assetBrowserDataSource.ImportAsset", host, StringComparison.Ordinal);
        Assert.Contains("pickImportSource: static (initialPath, _) => NativeFolderPicker.TryPickFile", host, StringComparison.Ordinal);
        Assert.Contains("deleteFolder: request => assetBrowserDataSource.DeleteFolder(request, _sceneModel)", host, StringComparison.Ordinal);
        Assert.Contains("moveFolder: request => assetBrowserDataSource.MoveFolder(request, _sceneModel)", host, StringComparison.Ordinal);
        Assert.Contains("public AssetBrowserFolderDeleteResult DeleteFolder", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("public AssetBrowserFolderMoveResult MoveFolder", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("public AssetBrowserCreateResult CreateAsset", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("public AssetBrowserImportResult ImportAsset", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("IAssetBrowserFolderDataSource", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<AssetBrowserFolderItem> ListFolders", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("_assets.DeleteFolder(request.Path, activeScene, request.Confirmed)", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("_assets.MoveFolder(request.Path, request.NewPath, activeScene)", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("_assets.CreateAsset(request.Path, type)", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("_assets.ImportAsset(request.SourceFullPath, request.Path, type)", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("_assets.CreateFolder(request.Path)", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("public string CreateFolder", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("public EditorAssetFolderDeleteResult DeleteFolder", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("public EditorAssetFolderMoveResult MoveFolder", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("RemoveUiManifestScreenEntries", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("RewriteUiManifestScreenPaths", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<EditorUiManifestScreenEntry> ListUiManifestScreens", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("public EditorUiManifestSyncResult SyncUiManifestScreens", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("TrySetUiManifestScreenPreload", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("DrawPanelMenuItem(app, \"UI Manifest\", UiManifestPanel.PanelTitle)", menu, StringComparison.Ordinal);
        Assert.Contains("EditorAssetType.Material", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("EditorAssetType.UiScreen", shellAssetBrowserDataSource, StringComparison.Ordinal);
        Assert.Contains("UpsertUiScreenManifestEntry", shellAssetManifestStore, StringComparison.Ordinal);
        Assert.Contains("ui-manifest.json", shellAssetManifestStore, StringComparison.Ordinal);
        string shellApp = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellApp.cs"));
        Assert.Contains("--scripted-menu-layout-probe", File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellOptions.cs")), StringComparison.Ordinal);
        Assert.Contains("editor_menu_layout_probe", shellApp, StringComparison.Ordinal);
        Assert.Contains("RunScriptedMenuLayoutProbeActions", shellApp, StringComparison.Ordinal);
        Assert.Contains("NewSceneAuto", File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorProjectSession.cs")), StringComparison.Ordinal);
        Assert.Contains("ReplaceWith", File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorSceneModel.cs")), StringComparison.Ordinal);
        Assert.Contains("ResetDockLayout", string.Join(
            '\n',
            File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "IEditorImGuiBackend.cs")),
            File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "ImGuiController.cs")),
            File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "EditorApp.cs")),
            File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "HexaImGuiBackend.cs")),
            File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellApp.cs")),
            File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellHostExtension.cs"))), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Preferences 属于用户作用域、同时驱动两套 ImGui context，并让辅助面板默认隐藏。
    /// </summary>
    [Fact]
    public void EditorPreferencesStayUserScopedAndDriveBothImGuiContexts()
    {
        string root = FindRepositoryRoot();
        string shellRoot = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string shellApp = File.ReadAllText(Path.Combine(shellRoot, "EditorShellApp.cs"));
        string shellWindow = File.ReadAllText(Path.Combine(shellRoot, "EditorShellWindow.cs"));
        string host = File.ReadAllText(Path.Combine(shellRoot, "EditorShellHostExtension.cs"));
        string menu = File.ReadAllText(Path.Combine(shellRoot, "EditorMainMenuBar.cs"));
        string preferencesStore = File.ReadAllText(Path.Combine(shellRoot, "Settings", "EditorPreferencesStore.cs"));
        string preferencesWindow = File.ReadAllText(Path.Combine(shellRoot, "Settings", "EditorPreferencesWindow.cs"));
        string projectSettings = File.ReadAllText(Path.Combine(shellRoot, "Settings", "ProjectSettingsPanel.cs"));
        string scriptOpener = File.ReadAllText(Path.Combine(shellRoot, "EditorScriptAssetOpenService.cs"));
        string guiBackend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Gui", "HexaImGuiBackend.cs"));
        string editorBackend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "HexaImGuiBackend.cs"));

        Assert.Contains("EditorPreferencesStore.LoadDefault()", shellApp, StringComparison.Ordinal);
        Assert.Contains("EditorShellWindow.Create(Preferences.Current.UiScale)", shellApp, StringComparison.Ordinal);
        Assert.Contains("PreferencesWindow.Draw()", shellApp, StringComparison.Ordinal);
        Assert.Contains("DpiScale = EditorUiScale.Normalize(uiScale)", shellWindow, StringComparison.Ordinal);
        Assert.Contains("DpiScale = app.UiScale", host, StringComparison.Ordinal);
        Assert.Contains("_editor.AddPanel(_app.PreferencesWindow)", host, StringComparison.Ordinal);
        Assert.Contains("AddHiddenPanel(_projectSettingsPanel)", host, StringComparison.Ordinal);
        Assert.Contains("AddHiddenPanel(_buildSettingsPanel)", host, StringComparison.Ordinal);
        Assert.Contains("EditorUiScale.Scale(ToolbarHeight, uiScale)", menu, StringComparison.Ordinal);
        Assert.Contains("DispatchShortcuts(app)", menu, StringComparison.Ordinal);
        Assert.Contains("Appearance", preferencesWindow, StringComparison.Ordinal);
        Assert.Contains("External Tools", preferencesWindow, StringComparison.Ordinal);
        Assert.Contains("EditorShortcutCatalog.All", preferencesWindow, StringComparison.Ordinal);
        Assert.Contains("editor-preferences.json", preferencesStore, StringComparison.Ordinal);
        Assert.Contains("FileOptions.WriteThrough", preferencesStore, StringComparison.Ordinal);
        Assert.DoesNotContain("外部脚本编辑器", projectSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("退出时保存布局", projectSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSettingsDto", scriptOpener, StringComparison.Ordinal);
        foreach (string backend in new[] { guiBackend, editorBackend })
        {
            Assert.Contains("io.IniFilename = null", backend, StringComparison.Ordinal);
            Assert.Contains("_saveLayoutOnShutdown", backend, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 3 通过 Shell adapter 接入 Engine，而不是让 Hosting 重新引用 Editor。
    /// </summary>
    [Fact]
    public void EditorShellSessionAttachesEngineThroughHostExtension()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));

        // Assert：验证预期结果
        Assert.Contains("EditorProjectSession.Open", source, StringComparison.Ordinal);
        Assert.Contains(".WithProject(project.ToEngineProject(sceneRelativePath))", source, StringComparison.Ordinal);
        Assert.Contains(".ApplyRuntimeDefaults(playerSettings, applyStartupScene: false)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".UseGuiRuntime(false)", source, StringComparison.Ordinal);
        Assert.Contains(".UseGuiRuntime()", source, StringComparison.Ordinal);
        Assert.Contains(".EnableGameUi()", source, StringComparison.Ordinal);
        Assert.Contains(".AddEditorHostExtension(editorHost)", source, StringComparison.Ordinal);
        Assert.Contains("engine.AttachWindowRuntime(window)", source, StringComparison.Ordinal);
        Assert.Contains("EditorShellHostExtension : IEditorHostExtension", source, StringComparison.Ordinal);
        Assert.Contains("EditorRenderBridge.AttachIfEnabled", source, StringComparison.Ordinal);
        Assert.Contains("EditorWindowInputConnector", source, StringComparison.Ordinal);
        Assert.Contains("EngineEditorPlaySessionService", source, StringComparison.Ordinal);
        Assert.Contains("EngineWorldSnapshotStore", source, StringComparison.Ordinal);
        Assert.Contains("CurrentSession.RunOneTick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderWindow.Create", source.Replace("EditorHostBootstrap.Create", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 EditorShell 只接入 Hosting-owned 脚本 runtime 与 Console 诊断 sink，不自行装配脚本运行时。
    /// </summary>
    [Fact]

    // —— Hosting 装配与内容加载 ——
    public void EditorShellConsumesHostingOwnedScriptingRuntimeAndConsoleDiagnostics()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string shellSource = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        string hostingEngine = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "Engine.cs"));
        string scriptingRuntime = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Scripting", "ScriptRuntime.cs"));

        // Assert：验证预期结果
        Assert.DoesNotContain("EditorConsoleScriptRuntime", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ScriptRuntime(", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ScriptHotReloadController(", shellSource, StringComparison.Ordinal);
        Assert.Contains("RegisterService<IScriptHotReloadDiagnosticSink>", shellSource, StringComparison.Ordinal);
        Assert.Contains("new EditorConsoleScriptHotReloadDiagnosticSink", shellSource, StringComparison.Ordinal);
        Assert.Contains("hotReload: new ScriptHotReloadRuntimeOptions", shellSource, StringComparison.Ordinal);
        Assert.Contains("CreateScriptRuntime(scriptScene, scriptContext, hotReload)", hostingEngine, StringComparison.Ordinal);
        Assert.Contains("Context.RegisterService(controller)", hostingEngine, StringComparison.Ordinal);
        Assert.Contains("RegisterOrReplaceByName", hostingEngine, StringComparison.Ordinal);
        Assert.Contains("new ScriptRuntime(controller, diagnosticSink, scriptAssemblies.RegisterOrReplaceByName)", hostingEngine, StringComparison.Ordinal);
        Assert.Contains("IScriptHotReloadDiagnosticSink", scriptingRuntime, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 4 拥有独立 authoring 模型、StableId 映射、层级面板与命令栈。
    /// </summary>
    [Fact]

    // —— 编辑器壳与 UI 后端 ——
    public void EditorShellDeclaresGameObjectAuthoringModelAndHierarchy()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        string editorSelection = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "EditorSelection.cs"));
        string hostingEngine = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "Engine.cs"));

        // Assert：验证预期结果
        Assert.Contains("class EditorSceneModel", source, StringComparison.Ordinal);
        Assert.Contains("class EditorGameObject", source, StringComparison.Ordinal);
        Assert.Contains("class EditorComponentModel", source, StringComparison.Ordinal);
        Assert.Contains("class EditorUndoStack", source, StringComparison.Ordinal);
        Assert.Contains("interface IEditorCommand", source, StringComparison.Ordinal);
        Assert.Contains("class GameObjectHierarchyPanel", source, StringComparison.Ordinal);
        Assert.Contains("SyncSelection", source, StringComparison.Ordinal);
        Assert.Contains("selection.FolderPath is not null", source, StringComparison.Ordinal);
        Assert.Contains("class GameObjectInspectorPanel", source, StringComparison.Ordinal);
        Assert.Contains("EditorSceneRuntimeProjection", source, StringComparison.Ordinal);
        Assert.Contains("StableIdToEntityId", source, StringComparison.Ordinal);
        Assert.Contains("EngineSceneDocument", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureAuthoring(sceneModel, undoStack, prefabs)", source, StringComparison.Ordinal);
        Assert.Contains("GameObjectHierarchyPanel(_sceneModel, _undoStack, _prefabs)", source, StringComparison.Ordinal);
        Assert.Contains("GameObjectInspectorPanel(", source, StringComparison.Ordinal);
        Assert.Contains("assetBrowserDataSource,", source, StringComparison.Ordinal);
        Assert.Contains("CaptureAssetInspector", source, StringComparison.Ordinal);
        Assert.Contains("AssetInspectorSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("DrawAssetInspector", source, StringComparison.Ordinal);
        Assert.Contains("CaptureFolderInspector", source, StringComparison.Ordinal);
        Assert.Contains("FolderInspectorSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("DrawFolderInspector", source, StringComparison.Ordinal);
        Assert.Contains("TryInvokePrimaryAssetAction", source, StringComparison.Ordinal);
        Assert.Contains("_app.InstantiatePrefab", source, StringComparison.Ordinal);
        Assert.Contains("_app.OpenScriptAsset", source, StringComparison.Ordinal);
        Assert.Contains("engine.Context.GetService<ScriptAssemblyRegistry>()", source, StringComparison.Ordinal);
        Assert.Contains("new CreateGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("new DeleteGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("new ReparentGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("new DuplicateGameObjectCommand", source, StringComparison.Ordinal);
        Assert.Contains("SetTransformCommand", source, StringComparison.Ordinal);
        Assert.Contains("AddComponentCommand", source, StringComparison.Ordinal);
        Assert.Contains("RemoveComponentCommand", source, StringComparison.Ordinal);
        Assert.Contains("MoveComponentCommand", source, StringComparison.Ordinal);
        Assert.Contains("SetComponentFieldCommand", source, StringComparison.Ordinal);
        Assert.Contains("ScriptInspector.InspectFields", source, StringComparison.Ordinal);
        Assert.Contains("GameObjectStableId", editorSelection, StringComparison.Ordinal);
        Assert.Contains("SelectGameObject", editorSelection, StringComparison.Ordinal);
        Assert.Contains("SelectAsset", editorSelection, StringComparison.Ordinal);
        Assert.Contains("SelectFolder", editorSelection, StringComparison.Ordinal);
        Assert.Contains("FolderPath", editorSelection, StringComparison.Ordinal);
        Assert.Contains("AssetPath = null;", editorSelection, StringComparison.Ordinal);
        Assert.Contains("FolderPath = null;", editorSelection, StringComparison.Ordinal);
        Assert.Contains("GameObjectStableId = null;", editorSelection, StringComparison.Ordinal);
        Assert.Contains("AttachScriptScene(Scripting.Scene scriptScene)", hostingEngine, StringComparison.Ordinal);
        Assert.Contains("--scripted-hierarchy-probe", File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellOptions.cs")), StringComparison.Ordinal);
        Assert.Contains("editor_hierarchy_probe", source, StringComparison.Ordinal);
        Assert.Contains("RunScriptedHierarchyProbeActions", source, StringComparison.Ordinal);
        Assert.Contains("cycle_rejected", source, StringComparison.Ordinal);
        Assert.Contains("selection_linked", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SceneHierarchyPanel", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证默认工作台体验的自动化 scripted route 覆盖新建工程、面板、脚本源、保存、Play/Exit 与构建入口。
    /// </summary>
    [Fact]
    public void EditorShellDeclaresDefaultWorkbenchScriptedRoute()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        string options = File.ReadAllText(Path.Combine(shellDirectory, "EditorShellOptions.cs"));

        // Assert：验证预期结果
        Assert.Contains("--scripted-default-workbench-probe", options, StringComparison.Ordinal);
        Assert.Contains("ScriptedDefaultWorkbenchProbe", source, StringComparison.Ordinal);
        Assert.Contains("RunScriptedDefaultWorkbenchProbeActions", source, StringComparison.Ordinal);
        Assert.Contains("editor_default_workbench_probe", source, StringComparison.Ordinal);
        Assert.Contains("completed={state.Completed}", source, StringComparison.Ordinal);
        Assert.Contains("succeeded={state.Succeeded}", source, StringComparison.Ordinal);
        Assert.Contains("CreateProject(projectRoot, \"Default Workbench Probe\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateGameObject()", source, StringComparison.Ordinal);
        Assert.Contains("CreateDefaultWorkbenchScriptSource", source, StringComparison.Ordinal);
        Assert.Contains("SaveScene()", source, StringComparison.Ordinal);
        Assert.Contains("EnterPlayTemporary", source, StringComparison.Ordinal);
        Assert.Contains("ExitEditorPlay", source, StringComparison.Ordinal);
        Assert.Contains("BuildSettingsPanel.PanelTitle", source, StringComparison.Ordinal);
        Assert.Contains("TryStartScriptedBuildProbe", source, StringComparison.Ordinal);
        Assert.Contains("CaptureScriptedBuildProbe", source, StringComparison.Ordinal);
        Assert.Contains("build_started", source, StringComparison.Ordinal);
        Assert.Contains("build_completed", source, StringComparison.Ordinal);
        Assert.Contains("build_ok", source, StringComparison.Ordinal);
        Assert.Contains("build_package_archive", source, StringComparison.Ordinal);
        Assert.Contains("ScriptHotReloadController", source, StringComparison.Ordinal);
        Assert.Contains("RequestReloadFromDirectory", source, StringComparison.Ordinal);
        Assert.Contains("GetBehaviourTypeNames", source, StringComparison.Ordinal);
        Assert.Contains("AddComponentToSelected", source, StringComparison.Ordinal);
        Assert.Contains("script_source_created", source, StringComparison.Ordinal);
        Assert.Contains("script_hot_reload_applied", source, StringComparison.Ordinal);
        Assert.Contains("behaviour_registered", source, StringComparison.Ordinal);
        Assert.Contains("behaviour_attached", source, StringComparison.Ordinal);
        Assert.Contains("build_output_ready", source, StringComparison.Ordinal);
        Assert.Contains("玩家包构建探针完成", source, StringComparison.Ordinal);
        Assert.Contains("真实窗口 Console 可读性与人工 UX", File.ReadAllText(Path.Combine(root, "plan", "19-standalone-editor-app.md")), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 6 接入 Scene View、真实相机控制、ImGuizmo 变换与点选拾取。
    /// </summary>
    [Fact]
    public void EditorShellDeclaresSceneViewGizmoCameraAndPicking()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        string editorBackend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "HexaImGuiBackend.cs"));
        XDocument shellProject = XDocument.Load(Path.Combine(shellDirectory, "PixelEngine.Editor.Shell.csproj"));

        // Assert：验证预期结果
        Assert.Contains("Hexa.NET.ImGuizmo", ReadIncludes(shellProject, "PackageReference"));
        Assert.Contains("class SceneViewPanel", source, StringComparison.Ordinal);
        Assert.Contains("new SceneViewPanel(", source, StringComparison.Ordinal);
        Assert.Contains("engine.Context.GetService<ScriptCameraApi>()", source, StringComparison.Ordinal);
        Assert.Contains("ScriptCameraApi camera", source, StringComparison.Ordinal);
        Assert.Contains("_camera.SetZoom", source, StringComparison.Ordinal);
        Assert.Contains("_camera.SetCenter", source, StringComparison.Ordinal);
        Assert.Contains("ViewportPanel.FitTexture", source, StringComparison.Ordinal);
        Assert.Contains("ViewportPanel.CreateTextureRef", source, StringComparison.Ordinal);
        Assert.Contains("MaterialBrushPalettePanel? brushPanel", source, StringComparison.Ordinal);
        Assert.Contains("brushPanel.ApplyAt", source, StringComparison.Ordinal);
        Assert.Contains("WantCaptureMouse", source, StringComparison.Ordinal);
        Assert.Contains("IsGizmoCapturingMouse", source, StringComparison.Ordinal);
        Assert.Contains("hasSelection && IsGizmoCapturingMouse()", source, StringComparison.Ordinal);
        Assert.Contains("TryPick", source, StringComparison.Ordinal);
        Assert.Contains("SelectGameObject", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmo.Manipulate", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmo.SetImGuiContext(_context)", editorBackend, StringComparison.Ordinal);
        Assert.Contains("ImGuizmoOperation.Translate", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmoOperation.RotateZ", source, StringComparison.Ordinal);
        Assert.Contains("ImGuizmoOperation.Scale", source, StringComparison.Ordinal);
        Assert.Contains("new SetTransformCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ViewportPanel(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 7 接入 .scene 保存/Save As、当前场景路径与 prefab authoring 边界。
    /// </summary>
    [Fact]
    public void EditorShellDeclaresSceneSaveAndPrefabAuthoring()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        string editorAssetSource = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Editor", "AssetBrowserDataSource.cs"));
        string hostingSceneDocument = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "EngineSceneDocument.cs"));

        // Assert：验证预期结果
        Assert.Contains("SceneOverridePath", source, StringComparison.Ordinal);
        Assert.Contains("CurrentSceneRelativePath", source, StringComparison.Ordinal);
        Assert.Contains("SaveSceneAsAuto", source, StringComparison.Ordinal);
        Assert.Contains("SaveSceneAs(", source, StringComparison.Ordinal);
        Assert.Contains("Project.UpsertScene", source, StringComparison.Ordinal);
        Assert.Contains("Engine.SaveSceneDocument", source, StringComparison.Ordinal);
        Assert.Contains("class EditorPrefabAssetStore", source, StringComparison.Ordinal);
        Assert.Contains("CreatePrefabFromSubtree", source, StringComparison.Ordinal);
        Assert.Contains("InstantiatePrefab", source, StringComparison.Ordinal);
        Assert.Contains("CreatePrefabAssetCommand", source, StringComparison.Ordinal);
        Assert.Contains("InstantiatePrefabCommand", source, StringComparison.Ordinal);
        Assert.Contains("RevertPrefabOverridesCommand", source, StringComparison.Ordinal);
        Assert.Contains("RecordPrefabOverride", source, StringComparison.Ordinal);
        Assert.Contains("EngineScenePrefabDocument", hostingSceneDocument, StringComparison.Ordinal);
        Assert.Contains("AssetBrowserItemKind.Prefab", editorAssetSource, StringComparison.Ordinal);
        Assert.Contains(".prefab", editorAssetSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorPrefabAssetStore", File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Scripting", "Scene.cs")), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证编辑器壳按 plan/19 节点 8/9 接入 Build Settings 模型、面板与 build-player 子进程编排。
    /// </summary>
    [Fact]
    public void EditorShellDeclaresBuildSettingsPanelAndPlayerBuildService()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

        // Assert：验证预期结果
        Assert.Contains("namespace PixelEngine.Editor.Shell.Build", source, StringComparison.Ordinal);
        Assert.Contains("class BuildSettingsPanel", source, StringComparison.Ordinal);
        Assert.Contains("BuildProfileDto", source, StringComparison.Ordinal);
        Assert.Contains("BuildProfileSceneDto", source, StringComparison.Ordinal);
        Assert.Contains("BuildRequest", source, StringComparison.Ordinal);
        Assert.Contains("BuildResult", source, StringComparison.Ordinal);
        Assert.Contains("BuildProgressEvent", source, StringComparison.Ordinal);
        Assert.Contains("BuildLog", source, StringComparison.Ordinal);
        Assert.Contains("BuildRunView", source, StringComparison.Ordinal);
        Assert.Contains("ScriptedBuildProbeSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("BuildPreflight", source, StringComparison.Ordinal);
        Assert.Contains("BuildHostRid", source, StringComparison.Ordinal);
        Assert.Contains("SupportsAot", source, StringComparison.Ordinal);
        Assert.Contains("PixelEngineEditorShellBuildJsonContext", source, StringComparison.Ordinal);
        Assert.Contains("EngineProjectSettingsStore.BuildSettingsFileName", source, StringComparison.Ordinal);
        Assert.Contains("RefreshScenes", source, StringComparison.Ordinal);
        Assert.Contains("PackageWholeContent ? []", source, StringComparison.Ordinal);
        Assert.Contains("Version", source, StringComparison.Ordinal);
        Assert.Contains("InformationalVersion", source, StringComparison.Ordinal);
        Assert.Contains("BuildSettingsPanel(_project, console: _app.ConsoleStore)", source, StringComparison.Ordinal);
        Assert.Contains("ShowBuildSettings", source, StringComparison.Ordinal);
        Assert.Contains("ContentRoot = _project.ContentRootPath", source, StringComparison.Ordinal);
        Assert.Contains("TryStartScriptedBuildProbe", source, StringComparison.Ordinal);
        Assert.Contains("CaptureScriptedBuildProbe", source, StringComparison.Ordinal);
        Assert.Contains("--scripted-build-probe", source, StringComparison.Ordinal);
        Assert.Contains("--scripted-build-run-probe", source, StringComparison.Ordinal);
        Assert.Contains("--scripted-build-cancel-probe", source, StringComparison.Ordinal);
        Assert.Contains("--scripted-build-settings-probe", source, StringComparison.Ordinal);
        Assert.Contains("--build-output", source, StringComparison.Ordinal);
        Assert.Contains("editor_build_probe", source, StringComparison.Ordinal);
        Assert.Contains("editor_build_run_probe", source, StringComparison.Ordinal);
        Assert.Contains("editor_build_cancel_probe", source, StringComparison.Ordinal);
        Assert.Contains("editor_build_settings_probe", source, StringComparison.Ordinal);
        Assert.Contains("phase_timing_count", source, StringComparison.Ordinal);
        Assert.Contains("phase_timings", source, StringComparison.Ordinal);
        Assert.Contains("ui_frame_count", source, StringComparison.Ordinal);
        Assert.Contains("ui_max_delta_ms", source, StringComparison.Ordinal);
        Assert.Contains("error_present", source, StringComparison.Ordinal);
        Assert.Contains("SanitizeSummaryValue", source, StringComparison.Ordinal);
        Assert.Contains("ApplyScriptedBuildSettingsProbe", source, StringComparison.Ordinal);
        Assert.Contains("CaptureScriptedBuildSettingsProbe", source, StringComparison.Ordinal);
        Assert.Contains("ScriptedBuildSettingsProbeSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("TryReadCancelChildPid", source, StringComparison.Ordinal);
        Assert.Contains("child_alive_after_cancel", source, StringComparison.Ordinal);
        Assert.Contains("CancelScriptedBuildProbe", source, StringComparison.Ordinal);
        Assert.Contains("KillProcessTree", source, StringComparison.Ordinal);
        Assert.Contains("CancellationToken.None", source, StringComparison.Ordinal);
        Assert.Contains("window_completed", source, StringComparison.Ordinal);
        Assert.Contains("content_loaded", source, StringComparison.Ordinal);
        Assert.Contains("window_frame_probe", source, StringComparison.Ordinal);
        Assert.Contains("class BuildToolLocator", source, StringComparison.Ordinal);
        Assert.Contains("PIXELENGINE_BUILD_PLAYER_PATH", source, StringComparison.Ordinal);
        Assert.Contains("interface IPlayerBuildService", source, StringComparison.Ordinal);
        Assert.Contains("class PlayerBuildService", source, StringComparison.Ordinal);
        Assert.Contains("PreflightAsync", source, StringComparison.Ordinal);
        Assert.Contains("build-player.ps1", source, StringComparison.Ordinal);
        Assert.Contains("build-player.sh", source, StringComparison.Ordinal);
        Assert.Contains("dotnet", source, StringComparison.Ordinal);
        Assert.Contains("pwsh", source, StringComparison.Ordinal);
        Assert.Contains("powershell.exe", source, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardOutput = true", source, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError = true", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", source, StringComparison.Ordinal);
        Assert.Contains("ConcurrentQueue<BuildProgressEvent>", source, StringComparison.Ordinal);
        Assert.Contains("TryParseProgressLine", source, StringComparison.Ordinal);
        Assert.Contains("pixelengine.build/v1", source, StringComparison.Ordinal);
        Assert.Contains("TryGetString(root, \"ts\"", source, StringComparison.Ordinal);
        Assert.Contains("item.Phase == BuildPhase.Unknown && item.Level == BuildLogLevel.Error", source, StringComparison.Ordinal);
        Assert.Contains("build-result.json", source, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
        Assert.Contains("Process.Start(startInfo)", source, StringComparison.Ordinal);
        Assert.Contains("build.log", source, StringComparison.Ordinal);
        Assert.Contains("\"-Rid\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-Channel\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-Configuration\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-Output\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-Version\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-InformationalVersion\"", source, StringComparison.Ordinal);

        string fixture = File.ReadAllText(
            Path.Combine(
                root,
                "tests",
                "fixtures",
                "editor-shell-build-player-cancel-probe.ps1"));
        Assert.Contains("cancel-child.pid", fixture, StringComparison.Ordinal);
        Assert.Contains("Start-Process -FilePath $dotnet", fixture, StringComparison.Ordinal);
        Assert.Contains("'run', '--project'", fixture, StringComparison.Ordinal);
        Assert.Contains("tools/build-player.ps1", fixture, StringComparison.Ordinal);
        Assert.Contains("\"-ProductName\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-IconPath\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-IncludeSymbols\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-StartScene\"", source, StringComparison.Ordinal);
        Assert.Contains("\"-IncludeScene\"", source, StringComparison.Ordinal);
        Assert.Contains("NativeAOT 仅支持当前宿主 RID", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证独立编辑器壳复用 plan/12 的材质/反应编辑器，并接到真实内容文件与运行时热重载服务。
    /// </summary>
    [Fact]
    public void EditorShellRegistersMaterialReactionEditorWithRuntimeReloadServices()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellHostExtension.cs"));

        // Assert：验证预期结果
        Assert.Contains("TryCreateMaterialReactionPanel", source, StringComparison.Ordinal);
        Assert.Contains("MaterialReactionEditorPanel panel = new(content)", source, StringComparison.Ordinal);
        Assert.Contains("FileMaterialReactionContentService content = new(", source, StringComparison.Ordinal);
        Assert.Contains("EngineContentLoader.MaterialsFileName", source, StringComparison.Ordinal);
        Assert.Contains("EngineContentLoader.ReactionsFileName", source, StringComparison.Ordinal);
        Assert.Contains("ReactionEngine reactions", source, StringComparison.Ordinal);
        Assert.Contains("SimulationKernel kernel", source, StringComparison.Ordinal);
        Assert.Contains("IChunkSource chunks", source, StringComparison.Ordinal);
        Assert.Contains("reactions.ReloadReactions", source, StringComparison.Ordinal);
        Assert.Contains("kernel.ReloadMaterialHotTable", source, StringComparison.Ordinal);
        Assert.Contains("engine.Context.Counters", source, StringComparison.Ordinal);
        Assert.Contains("panel.Reload()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证独立编辑器壳复用 plan/12 的 Edit/Play 模式面板，并接到 Hosting 的临时快照 Play session。
    /// </summary>
    [Fact]
    public void EditorShellRegistersEditorModePanelWithPlaySessionAdapter()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));

        // Assert：验证预期结果
        Assert.Contains("new EditorModePanel(new EditorPlaySessionAdapter(_app))", source, StringComparison.Ordinal);
        Assert.Contains("class EditorPlaySessionAdapter", source, StringComparison.Ordinal);
        Assert.Contains("IEditorPlaySessionService", source, StringComparison.Ordinal);
        Assert.Contains("CaptureEditorPlaySession", source, StringComparison.Ordinal);
        Assert.Contains("EnterPlayCurrent", source, StringComparison.Ordinal);
        Assert.Contains("EnterPlayTemporary", source, StringComparison.Ordinal);
        Assert.Contains("ExitEditorPlay", source, StringComparison.Ordinal);
        Assert.Contains("EngineEditorPlaySessionService", source, StringComparison.Ordinal);
        Assert.Contains("EngineWorldSnapshotStore", source, StringComparison.Ordinal);
        Assert.Contains("TemporarySnapshot", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证独立编辑器壳复用 plan/12 的存读档面板，并通过 Hosting 公开持久世界存读档 API 恢复运行时计数。
    /// </summary>
    [Fact]
    public void EditorShellRegistersSaveLoadPanelThroughHostingWorldSaveApi()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string shellDirectory = Path.Combine(root, "apps", "PixelEngine.Editor.Shell");
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(shellDirectory, "*.cs").Select(File.ReadAllText));
        string engine = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "Engine.cs"));

        // Assert：验证预期结果
        Assert.Contains("new SaveLoadPanel(new EditorWorldSaveLoadService", source, StringComparison.Ordinal);
        Assert.Contains("class EditorWorldSaveLoadService", source, StringComparison.Ordinal);
        Assert.Contains("ISaveLoadService", source, StringComparison.Ordinal);
        Assert.Contains("SaveWorldToDirectory", source, StringComparison.Ordinal);
        Assert.Contains("LoadWorldFromDirectory", source, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(_project.ProjectRoot, \"saves\")", source, StringComparison.Ordinal);
        Assert.Contains("public void SaveWorldToDirectory", engine, StringComparison.Ordinal);
        Assert.Contains("public WorldLoadResult LoadWorldFromDirectory", engine, StringComparison.Ordinal);
        Assert.Contains("Context.Clock.RestoreCounters", engine, StringComparison.Ordinal);
        Assert.Contains("RestoreFrameState", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("new WorldSaveLoadPanelService", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 plan/15 §3.11 的 build-player 一键玩家包编排器与 player-only audit 契约落地。
    /// </summary>
    [Fact]
    public void BuildPlayerScriptsDeclareNdjsonResultStartupAndPlayerOnlyAuditContracts()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string buildPlayerPs1 = File.ReadAllText(Path.Combine(root, "tools", "build-player.ps1"));
        string buildPlayerSh = File.ReadAllText(Path.Combine(root, "tools", "build-player.sh"));
        string packagePs1 = File.ReadAllText(Path.Combine(root, "tools", "package.ps1"));
        string packageSh = File.ReadAllText(Path.Combine(root, "tools", "package.sh"));
        string auditPs1 = File.ReadAllText(Path.Combine(root, "tools", "audit-release-artifacts.ps1"));
        string auditSh = File.ReadAllText(Path.Combine(root, "tools", "audit-release-artifacts.sh"));
        string startupOptions = File.ReadAllText(Path.Combine(root, "demo", "PixelEngine.Demo", "DemoStartupOptions.cs"));

        foreach (string script in new[] { buildPlayerPs1, buildPlayerSh })
        {
            // Assert：验证预期结果
            Assert.Contains("pixelengine.build/v1", script, StringComparison.Ordinal);
            Assert.Contains("build-result.json", script, StringComparison.Ordinal);
            Assert.Contains("build-native", script, StringComparison.Ordinal);
            Assert.Contains("publish-$Channel", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("verify-publish", script, StringComparison.Ordinal);
            Assert.Contains("package", script, StringComparison.Ordinal);
            Assert.Contains("audit-release-artifacts", script, StringComparison.Ordinal);
            Assert.Contains("phaseTimingsMs", script, StringComparison.Ordinal);
            Assert.Contains("launcherExe", script, StringComparison.Ordinal);
            Assert.Contains("release-rids.json", script, StringComparison.Ordinal);
            Assert.Contains("load-only", script, StringComparison.Ordinal);
            Assert.DoesNotContain("NativeAOT 仅支持当前宿主 RID", script, StringComparison.Ordinal);
        }

        Assert.Contains("AllowLoadOnly", buildPlayerPs1, StringComparison.Ordinal);
        Assert.Contains("--allow-load-only", buildPlayerSh, StringComparison.Ordinal);
        Assert.Contains("NativeAOT 仅支持当前宿主 OS", buildPlayerPs1, StringComparison.Ordinal);
        Assert.Contains("NativeAOT 仅支持当前宿主 OS", buildPlayerSh, StringComparison.Ordinal);
        Assert.Contains("SkipPublishContentAudit", buildPlayerPs1, StringComparison.Ordinal);
        Assert.Contains("SkipDemoContentAudit", buildPlayerPs1, StringComparison.Ordinal);
        Assert.Contains("--skip-publish-content-audit", buildPlayerSh, StringComparison.Ordinal);
        Assert.Contains("--skip-demo-content-audit", buildPlayerSh, StringComparison.Ordinal);

        foreach (string script in new[] { packagePs1, packageSh })
        {
            string lower = script.ToLowerInvariant();
            Assert.Contains("product", lower, StringComparison.Ordinal);
            Assert.Contains("start", lower, StringComparison.Ordinal);
            Assert.Contains("include", lower, StringComparison.Ordinal);
            Assert.Contains("startup.json", script, StringComparison.Ordinal);
            Assert.Contains("Debug symbols", script, StringComparison.Ordinal);
        }

        foreach (string script in new[] { auditPs1, auditSh })
        {
            string lower = script.ToLowerInvariant();
            Assert.Contains("product", lower, StringComparison.Ordinal);
            Assert.Contains("required", lower, StringComparison.Ordinal);
            Assert.Contains("active", lower, StringComparison.Ordinal);
            Assert.Contains("release-rids.json", script, StringComparison.Ordinal);
            Assert.Contains("dev", lower, StringComparison.Ordinal);
            Assert.Contains("skip", lower, StringComparison.Ordinal);
            Assert.Contains("publish", lower, StringComparison.Ordinal);
            Assert.Contains("content", lower, StringComparison.Ordinal);
            Assert.Contains("PixelEngine.Editor.dll", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ImGuizmo", script, StringComparison.Ordinal);
            Assert.Contains("ImPlot", script, StringComparison.Ordinal);
            Assert.DoesNotContain("Hexa.NET.ImGui*", script, StringComparison.Ordinal);
        }

        Assert.Contains("ResolveStartupSettings", startupOptions, StringComparison.Ordinal);
        Assert.Contains("EngineProjectSettingsStore.LoadStartupSettings", startupOptions, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument.Parse", startupOptions, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 plan/15 §2.1 的发行 RID 激活真相源与矩阵生成器契约落地。
    /// </summary>
    [Fact]
    public void ReleaseRidGateDeclaresWindowsActiveSetAndMatrixOutputs()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string ridConfigPath = Path.Combine(root, "tools", "release-rids.json");
        string matrixScript = File.ReadAllText(Path.Combine(root, "tools", "release-matrix.ps1"));
        string releaseWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        string ciWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        string ciMatrixPreflight = File.ReadAllText(Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"));
        JsonObject config = JsonNode.Parse(File.ReadAllText(ridConfigPath))!.AsObject();
        JsonArray channels = config["channels"]!.AsArray();
        JsonArray rids = config["rids"]!.AsArray();

        // Assert：验证预期结果
        Assert.Equal(["r2r", "aot"], [.. channels.Select(node => node!.GetValue<string>())]);
        Assert.Equal(6, rids.Count);

        Dictionary<string, JsonObject> byRid = rids
            .Select(node => node!.AsObject())
            .ToDictionary(node => node["rid"]!.GetValue<string>(), StringComparer.Ordinal);

        Assert.True(byRid["win-x64"]["active"]!.GetValue<bool>());
        Assert.True(byRid["win-arm64"]["active"]!.GetValue<bool>());
        Assert.Equal("load-only", byRid["win-arm64"]["smoke"]!.GetValue<string>());
        Assert.False(byRid["linux-x64"]["active"]!.GetValue<bool>());
        Assert.False(byRid["linux-arm64"]["active"]!.GetValue<bool>());
        Assert.False(byRid["osx-x64"]["active"]!.GetValue<bool>());
        Assert.False(byRid["osx-arm64"]["active"]!.GetValue<bool>());
        Assert.True(byRid["osx-x64"]["codesign"]!.GetValue<bool>());
        Assert.True(byRid["osx-arm64"]["codesign"]!.GetValue<bool>());

        Assert.Contains("native-matrix", matrixScript, StringComparison.Ordinal);
        Assert.Contains("native_matrix", matrixScript, StringComparison.Ordinal);
        Assert.Contains("build-matrix", matrixScript, StringComparison.Ordinal);
        Assert.Contains("build_matrix", matrixScript, StringComparison.Ordinal);
        Assert.Contains("expected", matrixScript, StringComparison.Ordinal);
        Assert.Contains("packageCount", matrixScript, StringComparison.Ordinal);
        Assert.Contains("assetCount", matrixScript, StringComparison.Ordinal);
        Assert.Contains("GITHUB_OUTPUT", matrixScript, StringComparison.Ordinal);
        Assert.Contains("ExcludeWinArm64", matrixScript, StringComparison.Ordinal);

        Assert.Contains("include_win_arm64", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("tools/release-matrix.ps1", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("fromJSON(needs.setup.outputs.native_matrix)", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("fromJSON(needs.setup.outputs.build_matrix)", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("RELEASE_EXPECTED", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("-ActiveRids $activeRids", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("rid: [win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64]", releaseWorkflow, StringComparison.Ordinal);

        foreach (string rid in byRid.Keys)
        {
            Assert.Contains($"rid: {rid}", ciWorkflow, StringComparison.Ordinal);
            Assert.Contains($"\"{rid}\"", ciMatrixPreflight, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("release-rids.json", ciWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("release-matrix.ps1", ciWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("release-rids.json", ciMatrixPreflight, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 dormant RID 只需翻 active 标志即可进入发行矩阵，不需要改 YAML 或审计脚本逻辑。
    /// </summary>
    [Fact]
    public void ReleaseRidGateDryRunActivatesDormantRidFromConfigOnly()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string tempDirectory = Path.Combine(Path.GetTempPath(), "PixelEngine.ReleaseRidGate." + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDirectory);

        try
        {
            string tempConfigPath = Path.Combine(tempDirectory, "release-rids.json");
            JsonObject config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tools", "release-rids.json")))!.AsObject();
            JsonObject linuxX64 = config["rids"]!
                .AsArray()
                .Select(node => node!.AsObject())
                .Single(node => node["rid"]!.GetValue<string>() == "linux-x64");
            linuxX64["active"] = true;
            File.WriteAllText(tempConfigPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            // Act：执行被测操作
            JsonObject matrix = JsonNode.Parse(RunPowerShellScript(root, Path.Combine(root, "tools", "release-matrix.ps1"), "-RidConfigPath", tempConfigPath, "-Print"))!.AsObject();
            JsonObject expected = matrix["expected"]!.AsObject();
            JsonArray activeRids = expected["activeRids"]!.AsArray();
            JsonArray buildEntries = matrix["build-matrix"]!["include"]!.AsArray();

            // Assert：验证不变式与预期结果
            Assert.Equal(["win-x64", "win-arm64", "linux-x64"], [.. activeRids.Select(node => node!.GetValue<string>())]);
            Assert.Equal(6, expected["packageCount"]!.GetValue<int>());
            Assert.Equal(7, expected["assetCount"]!.GetValue<int>());
            Assert.Contains(buildEntries, node =>
            {
                JsonObject entry = node!.AsObject();
                return entry["rid"]!.GetValue<string>() == "linux-x64" &&
                    entry["channel"]!.GetValue<string>() == "r2r" &&
                    entry["runner"]!.GetValue<string>() == "ubuntu-latest" &&
                    entry["shell"]!.GetValue<string>() == "bash";
            });
            Assert.Contains(buildEntries, node =>
                node!.AsObject()["rid"]!.GetValue<string>() == "linux-x64" &&
                node.AsObject()["channel"]!.GetValue<string>() == "aot");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 plan/15 §3.7.1 的发行布局选型被工程文件与脚本共同锁定。
    /// </summary>
    [Fact]
    public void ReleaseLayoutLocksPublishIntermediateAndSingleFileDecision()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        string publishR2rPs1 = File.ReadAllText(Path.Combine(root, "tools", "publish-r2r.ps1"));
        string publishAotPs1 = File.ReadAllText(Path.Combine(root, "tools", "publish-aot.ps1"));
        string packagePs1 = File.ReadAllText(Path.Combine(root, "tools", "package.ps1"));
        string packageSh = File.ReadAllText(Path.Combine(root, "tools", "package.sh"));
        string plan = File.ReadAllText(Path.Combine(root, "plan", "15-build-packaging-distribution.md"));

        // Assert：验证预期结果
        Assert.Contains("plan/15 §3.7.1", props, StringComparison.Ordinal);
        Assert.Contains("<PublishSingleFile>false</PublishSingleFile>", props, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", publishR2rPs1, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", publishAotPs1, StringComparison.Ordinal);
        Assert.Contains("not the player-facing package", publishR2rPs1, StringComparison.Ordinal);
        Assert.Contains("not the player-facing package", publishAotPs1, StringComparison.Ordinal);
        Assert.Contains("PlayerOutputDir", packagePs1, StringComparison.Ordinal);
        Assert.Contains("artifacts/PixelEngine Demo", packagePs1, StringComparison.Ordinal);
        Assert.Contains("player_output_dir", packageSh, StringComparison.Ordinal);
        Assert.Contains("artifacts/PixelEngine Demo", packageSh, StringComparison.Ordinal);
        Assert.Contains("方案 (b) 单文件", plan, StringComparison.Ordinal);
        Assert.Contains("否决 (b)", plan, StringComparison.Ordinal);
    }

    // —— 性能、原生与发布模式 ——
    /// <summary>
    /// 验证 native superbuild 把绝对 toolchain 与 Ninja 路径传给 Box2D 子构建，确保干净检出可直接构建。
    /// </summary>
    [Fact]
    public void NativeSuperbuildForwardsAbsoluteToolchainAndNinjaToBox2D()
    {
        string root = FindRepositoryRoot();
        string nativeCMake = File.ReadAllText(Path.Combine(root, "native", "CMakeLists.txt"));

        Assert.Contains("PIXELENGINE_TOOLCHAIN_FILE_ABSOLUTE", nativeCMake, StringComparison.Ordinal);
        Assert.Contains("BASE_DIR \"${CMAKE_CURRENT_SOURCE_DIR}\"", nativeCMake, StringComparison.Ordinal);
        Assert.Contains("-DCMAKE_TOOLCHAIN_FILE:FILEPATH=${PIXELENGINE_TOOLCHAIN_FILE_ABSOLUTE}", nativeCMake, StringComparison.Ordinal);
        Assert.Contains("-DCMAKE_MAKE_PROGRAM:FILEPATH=${CMAKE_MAKE_PROGRAM}", nativeCMake, StringComparison.Ordinal);
        Assert.DoesNotContain("-DCMAKE_TOOLCHAIN_FILE:FILEPATH=${CMAKE_TOOLCHAIN_FILE}", nativeCMake, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 HTML UI native 作为 dynamic-only 依赖随 UI/Demo publish 落入 runtimes/native，不进入 Box2D dual-build 静态链。
    /// </summary>
    [Fact]
    public void HtmlUiNativePackagingUsesDynamicOnlyRuntimeLayout()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string directoryTargets = File.ReadAllText(Path.Combine(root, "Directory.Build.targets"));
        string uiNativeTargets = File.ReadAllText(Path.Combine(root, "native", "PixelEngine.UiNative.targets"));
        string uiNativeCMake = File.ReadAllText(Path.Combine(root, "native", "ui_native", "CMakeLists.txt"));
        string buildNativePs1 = File.ReadAllText(Path.Combine(root, "tools", "build-native.ps1"));
        string buildNativeSh = File.ReadAllText(Path.Combine(root, "tools", "build-native.sh"));
        string packagePs1 = File.ReadAllText(Path.Combine(root, "tools", "package.ps1"));
        string packageSh = File.ReadAllText(Path.Combine(root, "tools", "package.sh"));
        string auditPs1 = File.ReadAllText(Path.Combine(root, "tools", "audit-release-artifacts.ps1"));
        string auditSh = File.ReadAllText(Path.Combine(root, "tools", "audit-release-artifacts.sh"));

        // Assert：验证预期结果
        Assert.Contains("PixelEngine.UiNative.targets", directoryTargets, StringComparison.Ordinal);
        Assert.Contains("'$(MSBuildProjectName)' == 'PixelEngine.UI' or '$(MSBuildProjectName)' == 'PixelEngine.Demo'", directoryTargets, StringComparison.Ordinal);
        Assert.Contains(@"out\$(PixelEngineUiNativeRid)\shared\", uiNativeTargets, StringComparison.Ordinal);
        Assert.Contains(@"runtimes\$(PixelEngineUiNativeRid)\native\$(PixelEngineUiNativeLibraryName)", uiNativeTargets, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", uiNativeTargets, StringComparison.Ordinal);
        Assert.Contains("'$(PublishAot)' != 'true'", uiNativeTargets, StringComparison.Ordinal);
        Assert.DoesNotContain("<NativeLibrary", uiNativeTargets, StringComparison.Ordinal);
        Assert.Contains("add_library(pixelengine_ui_native SHARED", uiNativeCMake, StringComparison.Ordinal);
        Assert.Contains("OUTPUT_NAME \"PixelEngine.UI.Native\"", uiNativeCMake, StringComparison.Ordinal);
        Assert.Contains("RUNTIME_OUTPUT_DIRECTORY \"${PIXELENGINE_NATIVE_OUT_DIR}/shared\"", uiNativeCMake, StringComparison.Ordinal);
        Assert.Contains("pixelengine_ui_native", buildNativePs1, StringComparison.Ordinal);
        Assert.Contains("pixelengine_ui_native", buildNativeSh, StringComparison.Ordinal);
        Assert.Contains("NOTICE.txt", packagePs1, StringComparison.Ordinal);
        Assert.Contains("RmlUi: MIT license", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Ultralight: inactive optional commercial-license profile", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Requests fall back to ManagedFallback", packagePs1, StringComparison.Ordinal);
        Assert.Contains("NOTICE.txt", packageSh, StringComparison.Ordinal);
        Assert.Contains("RmlUi: MIT license", packageSh, StringComparison.Ordinal);
        Assert.Contains("Ultralight: inactive optional commercial-license profile", packageSh, StringComparison.Ordinal);
        Assert.Contains("Requests fall back to ManagedFallback", packageSh, StringComparison.Ordinal);
        Assert.Contains("NOTICE.txt", auditPs1, StringComparison.Ordinal);
        Assert.Contains("Ultralight optional profile inactive", auditPs1, StringComparison.Ordinal);
        Assert.Contains("Assert-NoInactiveUltralightNative", auditPs1, StringComparison.Ordinal);
        Assert.Contains("NOTICE.txt", auditSh, StringComparison.Ordinal);
        Assert.Contains("Ultralight optional profile inactive", auditSh, StringComparison.Ordinal);
        Assert.Contains("assert_no_inactive_ultralight_native", auditSh, StringComparison.Ordinal);
        Assert.Contains("R2R 产物缺少动态 UI native", auditPs1, StringComparison.Ordinal);
        Assert.Contains("R2R 产物缺少动态 UI native", auditSh, StringComparison.Ordinal);
        Assert.Contains("AOT 产物不应携带动态 UI native", auditPs1, StringComparison.Ordinal);
        Assert.Contains("AOT 产物不应携带动态 UI native", auditSh, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Ultralight 设置入口和计划口径都明确写成未激活 optional profile，避免误导为真后端完成。
    /// </summary>
    [Fact]

    // —— 编辑器壳与 UI 后端 ——
    public void UltralightOptionalProfileIsVisibleInactiveGateNotCompletedBackend()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string playerSettingsPanel = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "Settings", "PlayerSettingsPanel.cs"));
        string uiBackendKind = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.UI", "UiBackendKind.cs"));
        string gate = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.UI", "UltralightOptionalProfileGate.cs"));
        string backend = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.UI", "UltralightBackend.cs"));
        string plan20 = File.ReadAllText(Path.Combine(root, "plan", "20-interactive-html-ui.md"));
        string plan14 = File.ReadAllText(Path.Combine(root, "plan", "14-testing-benchmarking.md"));
        string plan15 = File.ReadAllText(Path.Combine(root, "plan", "15-build-packaging-distribution.md"));

        // Assert：验证预期结果
        Assert.Contains("UltralightOptionalProfileGate.GetDisplayLabel", playerSettingsPanel, StringComparison.Ordinal);
        Assert.Contains("UltralightOptionalProfileGate.InactiveReason", playerSettingsPanel, StringComparison.Ordinal);
        Assert.Contains("未满足 native SDK / commercial license / release gate 前保持未激活", uiBackendKind, StringComparison.Ordinal);
        Assert.Contains("Ultralight (inactive optional profile → ManagedFallback)", gate, StringComparison.Ordinal);
        Assert.Contains("public const bool IsActive = false", gate, StringComparison.Ordinal);
        Assert.Contains("commercial redistribution license", gate, StringComparison.Ordinal);
        Assert.Contains("release artifact evidence", gate, StringComparison.Ordinal);
        Assert.Contains("public sealed class UltralightBackend", backend, StringComparison.Ordinal);
        Assert.Contains("public bool IsAvailable => UltralightOptionalProfileGate.IsActive", backend, StringComparison.Ordinal);
        Assert.Contains("throw new NotSupportedException(ActivationFailureReason)", backend, StringComparison.Ordinal);
        Assert.Contains("return UiHitResult.None", backend, StringComparison.Ordinal);
        Assert.Contains("未激活 optional profile", plan20, StringComparison.Ordinal);
        Assert.Contains("release audit 不允许 Ultralight native 混入", plan20, StringComparison.Ordinal);
        Assert.Contains("optional profile 默认 inactive", plan14, StringComparison.Ordinal);
        Assert.Contains("plan 状态不误勾 M15", plan14, StringComparison.Ordinal);
        Assert.Contains("Ultralight optional profile inactive", plan15, StringComparison.Ordinal);
        Assert.Contains("不得把 Ultralight native 混入包当成发行闭合", plan15, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 RmlUi ANGLE/GLES 使用独立 #version 300 es profile，不得用 desktop GL3 冒充，且 M15 真实窗口证据仍未勾选完成。
    /// </summary>
    [Fact]
    public void RmlUiAngleGlesProfileGateFallsBackAndDoesNotMarkM15Complete()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string gate = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.UI", "RmlUiNativeProfileGate.cs"));
        string bootstrap = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.UI", "RmlUiGlBootstrap.cs"));
        string engine = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "Engine.cs"));
        string uiNativeCMake = File.ReadAllText(Path.Combine(root, "native", "ui_native", "CMakeLists.txt"));
        string uiNativeCpp = File.ReadAllText(Path.Combine(root, "native", "ui_native", "PixelEngineUiNative.cpp"));
        string gl3Renderer = File.ReadAllText(Path.Combine(root, "native", "rmlui", "Backends", "RmlUi_Renderer_GL3.cpp"));
        string plan20 = File.ReadAllText(Path.Combine(root, "plan", "20-interactive-html-ui.md"));
        string plan14 = File.ReadAllText(Path.Combine(root, "plan", "14-testing-benchmarking.md"));
        string plan15 = File.ReadAllText(Path.Combine(root, "plan", "15-build-packaging-distribution.md"));

        // Assert：验证预期结果
        Assert.Contains("RenderBackend.GlEs30Angle", gate, StringComparison.Ordinal);
        Assert.Contains("capabilities.IsGles || capabilities.IsAngle", gate, StringComparison.Ordinal);
        Assert.Contains("RmlUi_Renderer_GLES3_ANGLE", gate, StringComparison.Ordinal);
        Assert.Contains("#version 300 es", gate, StringComparison.Ordinal);
        Assert.Contains("CanUseNativeRenderer", gate, StringComparison.Ordinal);
        Assert.Contains("NativeProfileGles3Angle", gate, StringComparison.Ordinal);
        Assert.Contains("RmlUiNativeProfileGate.CanUseNativeRenderer(window.Backend, window.Capabilities", bootstrap, StringComparison.Ordinal);
        Assert.Contains("RmlUiNative.SetRendererProfile", bootstrap, StringComparison.Ordinal);
        Assert.Contains("RmlUiNativeProfileGate.Evaluate(window.Backend, window.Capabilities)", engine, StringComparison.Ordinal);
        Assert.Contains("if (!decision.CanUseNativeRenderer)", engine, StringComparison.Ordinal);
        Assert.Contains("RmlUi_Renderer_GL3.cpp", uiNativeCMake, StringComparison.Ordinal);
        Assert.Contains("RenderInterface_GL3", uiNativeCpp, StringComparison.Ordinal);
        Assert.Contains("peui_native_set_renderer_profile", uiNativeCpp, StringComparison.Ordinal);
        Assert.Contains("RewriteShaderSource", gl3Renderer, StringComparison.Ordinal);
        Assert.Contains("#version 300 es", gl3Renderer, StringComparison.Ordinal);
        Assert.Contains("Gles300Angle", gl3Renderer, StringComparison.Ordinal);
        // M15 真实窗口/发行证据仍未闭合，不得用工程 profile 冒充最终验收。
        Assert.Contains("M15 RmlUi ANGLE/GLES", plan20, StringComparison.Ordinal);
        Assert.Contains("真实窗口", plan20, StringComparison.Ordinal);
        Assert.Contains("RmlUi/Ultralight native 专属上传、真实平台 composition 与高保真浏览器语义仍按 plan/20/M15 标 blocked/pending", plan14, StringComparison.Ordinal);
        Assert.Contains("RmlUi、Ultralight 归 dynamic-only 或系统分发并可门控回退", plan15, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Hosting 用同一个 UI 字符串池连接脚本服务和 RmlUi 后端，避免 StringHandle 只能裸整数往返。
    /// </summary>
    [Fact]
    public void HostingSharesUiStringPoolBetweenScriptServiceAndRmlUiBackend()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string engine = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "Engine.cs"));
        string bridge = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "GameUiServiceBridge.cs"));
        string scripting = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Scripting", "GameUiFacades.cs"));
        string plan20 = File.ReadAllText(Path.Combine(root, "plan", "20-interactive-html-ui.md"));

        // Assert：验证预期结果
        Assert.Contains("UiStringPool strings = new();", engine, StringComparison.Ordinal);
        Assert.Contains("new RmlUiBackend(window, stringResolver: strings)", engine, StringComparison.Ordinal);
        Assert.Contains("new(host, Context.Options.ContentRoot, stringPool: strings)", engine, StringComparison.Ordinal);
        Assert.Contains("UiStringHandle InternString(string value)", scripting, StringComparison.Ordinal);
        Assert.Contains("RuntimeUi.UiStringPool? stringPool = null", bridge, StringComparison.Ordinal);
        Assert.Contains("_strings.Intern(value)", bridge, StringComparison.Ordinal);
        Assert.Contains("脚本公开 `IGameUiService.InternString`", plan20, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo gameplay 内容资产与材质纹理引用在源 content 中闭合，打包脚本可原样拷贝。
    /// </summary>
    [Fact]
    public void DemoContentDeclaresWeaponsAndResolvableMaterialTextures()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string contentRoot = Path.Combine(root, "demo", "PixelEngine.Demo", "content");
        string weaponsPath = Path.Combine(contentRoot, "weapons.json");
        string materialsPath = Path.Combine(contentRoot, "materials.json");
        string reactionsPath = Path.Combine(contentRoot, "reactions.json");

        // Assert：验证预期结果
        Assert.True(File.Exists(weaponsPath), "Demo content 必须包含 weapons.json。");
        Assert.True(File.Exists(materialsPath), "Demo content 必须包含 materials.json。");
        Assert.True(File.Exists(reactionsPath), "Demo content 必须包含 reactions.json。");

        JsonObject weapons = JsonNode.Parse(File.ReadAllText(weaponsPath))!.AsObject();
        JsonArray weaponItems = weapons["weapons"]!.AsArray();
        Assert.Equal(6, weaponItems.Count);
        Assert.Equal(
            ["singleShot", "laser", "grenade", "bomb", "excavator", "builder"],
            [.. weaponItems.Select(node => node!.AsObject()["kind"]!.GetValue<string>())]);
        Assert.Contains(
            weaponItems.Select(node => node!.AsObject()),
            weapon => weapon["id"]!.GetValue<string>() == "grenade" &&
                weapon["fuseSeconds"]!.GetValue<double>() > 0 &&
                weapon["impulse"]!.GetValue<double>() > 0);
        Assert.Contains(
            weaponItems.Select(node => node!.AsObject()),
            weapon => weapon["id"]!.GetValue<string>() == "excavator" &&
                weapon["radius"]!.GetValue<int>() > 0 &&
                weapon["cooldownSeconds"]!.GetValue<double>() > 0);
        Assert.Contains(
            weaponItems.Select(node => node!.AsObject()),
            weapon => weapon["id"]!.GetValue<string>() == "builder" &&
                weapon["spawnMaterial"]!.GetValue<string>() == "stone");

        JsonObject materials = JsonNode.Parse(File.ReadAllText(materialsPath))!.AsObject();
        foreach (JsonObject material in materials["materials"]!.AsArray().Select(node => node!.AsObject()))
        {
            if (!material.TryGetPropertyValue("textureId", out JsonNode? textureNode) || textureNode is null)
            {
                continue;
            }

            int textureId = textureNode.GetValue<int>();
            string texturePrefix = textureId.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "_";
            Assert.Contains(
                Directory.EnumerateFiles(Path.Combine(contentRoot, "textures"), texturePrefix + "*.png").Select(Path.GetFileName),
                fileName => fileName is not null && fileName.StartsWith(texturePrefix, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 验证 Demo 源码不绕过 Hosting/Scripting 公开入口访问内容或模拟实现。
    /// </summary>
    [Fact]
    public void DemoSourcesDoNotBypassHostingFacade()
    {
        string root = FindRepositoryRoot();
        string source = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "demo", "PixelEngine.Demo"), "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("using PixelEngine.Content", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using PixelEngine.Simulation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EngineContentLoader", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Materials.Count", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Reactions.Count", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo 启动器与 benchmark probe 不直接解析具体 Physics/Rendering 服务，统一消费 Hosting probe facade。
    /// </summary>
    [Fact]
    public void DemoAndBenchmarkProbesUseStableHostingFacade()
    {
        string root = FindRepositoryRoot();
        string demoSource = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "demo", "PixelEngine.Demo"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        string benchmarkSource = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(root, "bench", "PixelEngine.Benchmarks"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        string probeSource = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Hosting", "EngineProbeApi.cs"));

        Assert.DoesNotContain("Context.GetService", demoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Context.TryGetService", demoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<PhysicsSystem>", demoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<RenderPipeline>", demoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Context.GetService", benchmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Context.TryGetService", benchmarkSource, StringComparison.Ordinal);
        Assert.Contains("engine.Probe", demoSource, StringComparison.Ordinal);
        Assert.Contains("engine.RegisterScriptAssembly", demoSource, StringComparison.Ordinal);
        Assert.Contains("engine.CurrentScene", demoSource, StringComparison.Ordinal);
        Assert.Contains("ParticleRenderProbeResult", probeSource, StringComparison.Ordinal);
        Assert.Contains("RegisterBeforeSwapBuffers", probeSource, StringComparison.Ordinal);

        Type[] publicProbePropertyTypes =
        [
            .. typeof(EngineProbeApi).GetProperties().Select(static property => property.PropertyType),
        ];
        Assert.DoesNotContain(
            publicProbePropertyTypes,
            static type => type.FullName is "PixelEngine.Physics.PhysicsSystem" or "PixelEngine.Rendering.RenderPipeline");
    }

    /// <summary>
    /// 验证 Demo 已收敛为纯玩家运行时，不再暴露内嵌编辑器启动入口或旧 editor-window 证据字段。
    /// </summary>
    [Fact]
    public void DemoRuntimeDoesNotExposeInProcessEditorEntry()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string demoDirectory = Path.Combine(root, "demo", "PixelEngine.Demo");
        string startupOptions = File.ReadAllText(Path.Combine(demoDirectory, "DemoStartupOptions.cs"));
        string demoProgram = File.ReadAllText(Path.Combine(demoDirectory, "DemoProgram.cs"));
        string pauseMenu = File.ReadAllText(Path.Combine(demoDirectory, "PauseMenu.cs"));

        // Assert：验证预期结果
        Assert.DoesNotContain("--editor", startupOptions, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableEditor", startupOptions, StringComparison.Ordinal);
        Assert.DoesNotContain("editor_enabled=", demoProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("editor_running=", demoProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("editor_panels=", demoProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("editor_bridge_frames=", demoProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("打开 Editor", pauseMenu, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo lava-mine 验收场景以 .scene 文件落盘，并通过公开场景文档格式引用 LevelDirector。
    /// </summary>
    [Fact]
    public void DemoLavaMineSceneFileUsesLevelDirectorBehaviour()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine.scene");

        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);

        // Assert：验证预期结果
        Assert.Equal("lava-mine", document.Name);
        EngineSceneEntityDocument[] entities = document.Entities!;
        EngineSceneEntityDocument entity = Assert.Single(entities);
        EngineSceneBehaviourDocument behaviour = Assert.Single(entity.Behaviours!);
        Assert.Equal("PixelEngine.Demo.LevelDirector", behaviour.TypeName);
        Assert.Equal("640", behaviour.SerializedFields!["LevelWidth"]);
        Assert.Equal("360", behaviour.SerializedFields["LevelHeight"]);
        Assert.Equal("true", behaviour.SerializedFields["BuildScriptEntities"]);
        Assert.Equal("true", behaviour.SerializedFields["BuildGoalTrigger"]);
        Assert.Equal("570", behaviour.SerializedFields["GoalX"]);
        Assert.Equal("208", behaviour.SerializedFields["GoalY"]);
        Assert.DoesNotContain(
            entities.SelectMany(item => item.Behaviours!),
            item => item.TypeName is
                "PixelEngine.Demo.MissionDirector" or
                "PixelEngine.Demo.RisingHazardDirector" or
                "PixelEngine.Demo.ExtractionTrigger" or
                "PixelEngine.Demo.ObjectiveCrystal");
    }

    /// <summary>
    /// 验证音频窗口探针不是黑屏空场景，截图门禁能观察到真实可见内容。
    /// </summary>
    [Fact]
    public void DemoAudioProbeSceneMaterializesVisibleLevelDirector()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine-audio-probe.scene");

        AssertProbeSceneUsesVisibleLevelDirector(scenePath, "lava-mine-audio-probe");
    }

    /// <summary>
    /// 验证粒子 / 光照窗口探针不是黑屏空场景，截图门禁能观察到真实可见内容。
    /// </summary>
    [Fact]
    public void DemoParticleLightProbeSceneMaterializesVisibleLevelDirector()
    {
        string root = FindRepositoryRoot();
        string scenePath = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "scenes", "lava-mine-particle-light-probe.scene");

        AssertProbeSceneUsesVisibleLevelDirector(scenePath, "lava-mine-particle-light-probe");
    }

    /// <summary>
    /// 验证 Demo 可见内容包 API 不泄漏 Content / Simulation 实现类型。
    /// </summary>
    [Fact]
    public void EngineContentPackagePublicApiDoesNotExposeImplementationAssemblies()
    {
        foreach (MemberInfo member in typeof(EngineContentPackage).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (member is MethodInfo method)
            {
                AssertAllowedPublicType(method.ReturnType, member.Name);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertAllowedPublicType(parameter.ParameterType, member.Name);
                }
            }
            else if (member is PropertyInfo property)
            {
                AssertAllowedPublicType(property.PropertyType, member.Name);
            }
            else if (member is ConstructorInfo constructor)
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    AssertAllowedPublicType(parameter.ParameterType, member.Name);
                }
            }
        }
    }

    /// <summary>
    /// 验证 Hosting 公开 API 都带中文 XML 文档注释。
    /// </summary>
    [Fact]
    public void HostingPublicApiMembersHaveChineseXmlDocumentation()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string projectDirectory = Path.Combine(root, "src", "PixelEngine.Hosting");
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string[] lines = File.ReadAllLines(file);
            int braceDepth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (braceDepth <= 1 && IsPublicApiDeclaration(lines[i]))
                {
                    int previous = PreviousNonAttributeLine(lines, i);
                    // Assert：验证预期结果
                    Assert.True(
                        previous >= 0 && IsChineseXmlDocumentationBlock(lines, previous),
                        $"{file}:{i + 1} 公开 API 缺少中文 XML 文档注释。");
                }

                braceDepth += GetBraceDepthDelta(lines[i]);
            }
        }
    }

    private static bool IsPublicApiDeclaration(string line)
    {
        string trimmed = line.TrimStart();
        return Regex.IsMatch(
            trimmed,
            @"^public\s+(sealed\s+|static\s+|readonly\s+|record\s+|enum\s+|interface\s+|class\s+|struct\s+|delegate\s+|[A-Z_a-z])");
    }

    private static bool IsChineseXmlDocumentationBlock(string[] lines, int lastDocumentationLine)
    {
        bool hasDocumentation = false;
        bool hasChinese = false;
        bool hasInheritdoc = false;
        for (int i = lastDocumentationLine; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                break;
            }

            hasDocumentation = true;
            hasChinese |= Regex.IsMatch(trimmed, @"\p{IsCJKUnifiedIdeographs}");
            hasInheritdoc |= trimmed.Contains("<inheritdoc", StringComparison.Ordinal);
        }

        return hasDocumentation && hasChinese && !hasInheritdoc;
    }

    private static int PreviousNonAttributeLine(string[] lines, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetBraceDepthDelta(string line)
    {
        int delta = 0;
        foreach (char character in line)
        {
            if (character == '{')
            {
                delta++;
            }
            else if (character == '}')
            {
                delta--;
            }
        }

        return delta;
    }

    private static void AssertAllowedPublicType(Type type, string memberName)
    {
        Type publicType = UnwrapType(type);
        Assert.False(
            publicType.Namespace?.StartsWith("PixelEngine.Simulation", StringComparison.Ordinal) == true ||
            publicType.Namespace?.StartsWith("PixelEngine.Content", StringComparison.Ordinal) == true,
            $"{memberName} 泄漏了实现类型 {publicType.FullName}。");
        foreach (Type argument in publicType.GenericTypeArguments)
        {
            AssertAllowedPublicType(argument, memberName);
        }
    }

    private static void AssertProbeSceneUsesVisibleLevelDirector(string scenePath, string expectedName)
    {
        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);

        Assert.Equal(expectedName, document.Name);
        EngineSceneBehaviourDocument behaviour = Assert.Single(Assert.Single(document.Entities!).Behaviours!);
        Assert.Equal("PixelEngine.Demo.LevelDirector", behaviour.TypeName);
        Assert.Equal("true", behaviour.SerializedFields!["BuildScriptEntities"]);
        Assert.Equal("640", behaviour.SerializedFields["LevelWidth"]);
        Assert.Equal("360", behaviour.SerializedFields["LevelHeight"]);
    }

    private static Type UnwrapType(Type type)
    {
        Type current = type;
        while (current.IsByRef || current.IsPointer || current.IsArray)
        {
            current = current.GetElementType()!;
        }

        return current;
    }

    private static string[] ReadIncludes(XDocument project, string elementName)
    {
        return
        [
            .. project
                .Descendants(elementName)
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!),
        ];
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

    private static bool IsRepositoryProjectPath(string path)
    {
        return !PathContainsDirectory(path, ".git") &&
            !PathContainsDirectory(path, ".claude") &&
            !PathContainsDirectory(path, "artifacts") &&
            !PathContainsDirectory(path, "bin") &&
            !PathContainsDirectory(path, "obj") &&
            !PathContainsDirectory(path, "最终输出");
    }

    private static bool PathContainsDirectory(string path, string directoryName)
    {
        string[] parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => string.Equals(part, directoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static string RunPowerShellScript(string workingDirectory, string scriptPath, params string[] arguments)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo.FileName = "pwsh";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        _ = process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), $"脚本超时: {scriptPath}");
        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        return stdout;
    }

    private static ProcessResult RunPowerShellScriptRaw(string workingDirectory, string scriptPath, params string[] arguments)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo.FileName = "pwsh";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        _ = process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), $"脚本超时: {scriptPath}");
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string ReadCurrentGitHead(string root)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo.FileName = "git";
        process.StartInfo.WorkingDirectory = root;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(root);
        process.StartInfo.ArgumentList.Add("rev-parse");
        process.StartInfo.ArgumentList.Add("HEAD");

        _ = process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "git rev-parse HEAD 超时。");
        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        return stdout.Trim();
    }

    private static void WriteMinimalFinalOutput(string outputRoot, string gitCommit)
    {
        WriteTextFile(outputRoot, "编辑器/PixelEngine.Editor.Shell.exe", "editor");
        WriteTextFile(outputRoot, "游戏Demo/PixelEngine Demo.exe", "demo");
        WriteTextFile(
            outputRoot,
            "_验证记录/logs/editor-default-workbench.stdout.log",
            "editor_default_workbench_probe completed=True succeeded=True build_completed=True build_ok=True");
        WriteTextFile(outputRoot, "_验证记录/logs/editor-default-workbench.stderr.log", "");
        WriteTextFile(outputRoot, "_验证记录/editor-default-workbench.bmp", "editor capture");
        WriteTextFile(outputRoot, "_验证记录/logs/demo-window.stdout.log", "PixelEngine.Demo window_frame_probe frames=80");
        WriteTextFile(outputRoot, "_验证记录/logs/demo-window.stderr.log", "");
        WriteTextFile(outputRoot, "_验证记录/demo-window.bmp", "demo capture");
        WriteTextFile(outputRoot, "README.txt", "PixelEngine final output");
        WriteTextFile(
            outputRoot,
            "_验证记录/demo-build-result.json",
            JsonSerializer.Serialize(new
            {
                ok = true,
                runtimeUiBackend = "RmlUi",
            }));
        WriteTextFile(
            outputRoot,
            "_验证记录/manifest.json",
            JsonSerializer.Serialize(new
            {
                schema = "pixelengine.final-output/v1",
                gitCommit,
                sourceWorktreePolicy = "tracked-clean-required",
                sourceTrackedWorktreeClean = true,
                rid = "win-x64",
                configuration = "Release",
                demoChannel = "r2r",
                demoRuntimeUiBackendRequested = "RmlUi",
                editorSymbolsIncluded = false,
                editorDeveloperMetadataPolicy = "pdb-and-xml-pruned",
                editorExecutable = "编辑器/PixelEngine.Editor.Shell.exe",
                demoExecutable = "游戏Demo/PixelEngine Demo.exe",
                checksumFile = "SHA256SUMS",
                validation = new
                {
                    editorDefaultWorkbenchProbe = new
                    {
                        completed = true,
                        succeeded = true,
                        buildOk = true,
                        stdout = "_验证记录/logs/editor-default-workbench.stdout.log",
                        stderr = "_验证记录/logs/editor-default-workbench.stderr.log",
                        capture = "_验证记录/editor-default-workbench.bmp",
                    },
                    demoWindowProbe = new
                    {
                        completed = true,
                        stdout = "_验证记录/logs/demo-window.stdout.log",
                        stderr = "_验证记录/logs/demo-window.stderr.log",
                        capture = "_验证记录/demo-window.bmp",
                    },
                    demoBuildResult = "_验证记录/demo-build-result.json",
                },
            }));
        WriteFinalOutputChecksums(outputRoot);
    }

    private static void WriteTextFile(string outputRoot, string relativePath, string content)
    {
        string fullPath = Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static void WriteFinalOutputChecksums(string outputRoot)
    {
        string[] files =
        [
            .. Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
                .Where(static path => !string.Equals(Path.GetFileName(path), "SHA256SUMS", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        string[] lines =
        [
            .. files.Select(path =>
                Sha256Hex(path) + "  " + Path.GetRelativePath(outputRoot, path).Replace('\\', '/')),
        ];
        File.WriteAllLines(Path.Combine(outputRoot, "SHA256SUMS"), lines);
    }

    private static string Sha256Hex(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr)
    {
        public string CombinedOutput => Stdout + Environment.NewLine + Stderr;
    }
}
