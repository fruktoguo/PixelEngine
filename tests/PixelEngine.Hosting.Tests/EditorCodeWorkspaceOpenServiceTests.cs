using System.Diagnostics;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// VS Code 默认脚本编辑与 C# project/solution 工作流测试。
/// </summary>
public sealed class EditorCodeWorkspaceOpenServiceTests
{
    /// <summary>
    /// 验证 standalone 工程生成 Library + scripts glob + 固定产品引用，且内容不变不重写并可实际 build。
    /// </summary>
    [Fact]
    public void StandaloneProjectGeneratesStableBuildableIdeModel()
    {
        using TempProject temp = new("独立 Game & Tools");
        File.WriteAllText(
            Path.Combine(temp.Project.ScriptSourcePath, "SmokeBehaviour.cs"),
            "public static class Smoke { public static System.Type Api => typeof(PixelEngine.Scripting.Behaviour); }\n");

        EditorCSharpProjectResolution first = EditorCodeWorkspaceFiles.ResolveOrGenerate(temp.Project, AppContext.BaseDirectory);
        DateTime projectWrite = File.GetLastWriteTimeUtc(first.ProjectPath);
        DateTime solutionWrite = File.GetLastWriteTimeUtc(first.SolutionPath);
        string projectBefore = File.ReadAllText(first.ProjectPath);
        string solutionBefore = File.ReadAllText(first.SolutionPath);

        EditorCSharpProjectResolution second = EditorCodeWorkspaceFiles.ResolveOrGenerate(temp.Project, AppContext.BaseDirectory);

        Assert.True(first.ProjectGenerated);
        Assert.True(first.SolutionGenerated);
        Assert.Equal(first.ProjectPath, second.ProjectPath);
        Assert.Equal(first.SolutionPath, second.SolutionPath);
        Assert.Equal(projectWrite, File.GetLastWriteTimeUtc(second.ProjectPath));
        Assert.Equal(solutionWrite, File.GetLastWriteTimeUtc(second.SolutionPath));
        Assert.Equal(projectBefore, File.ReadAllText(second.ProjectPath));
        Assert.Equal(solutionBefore, File.ReadAllText(second.SolutionPath));
        Assert.Contains(EditorCodeWorkspaceFiles.OwnershipMarker, projectBefore, StringComparison.Ordinal);
        Assert.Contains("<OutputType>Library</OutputType>", projectBefore, StringComparison.Ordinal);
        Assert.Contains("scripts/**/*.cs", projectBefore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PixelEngine.Hosting.dll", projectBefore, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Scripting.dll", projectBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("PixelEngine.Editor.dll", projectBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("PixelEngine.Editor.Shell.dll", projectBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("PixelEngine.dll", projectBefore, StringComparison.Ordinal);

        ProcessResult build = RunDotNet("build", first.ProjectPath, "-c", "Release", "--nologo");
        Assert.True(build.ExitCode == 0, build.Output);
    }

    /// <summary>
    /// 验证 Demo 复用根 csproj 与真正包含它的祖先 PixelEngine.sln，不在 Demo 根制造伪 solution。
    /// </summary>
    [Fact]
    public void DemoProjectReusesExistingProjectAndContainingAncestorSolution()
    {
        string repositoryRoot = FindRepositoryRoot();
        string demoRoot = Path.Combine(repositoryRoot, "demo", "PixelEngine.Demo");
        EditorProject project = EditorProject.Load(demoRoot);

        EditorCSharpProjectResolution resolution = EditorCodeWorkspaceFiles.ResolveOrGenerate(project, AppContext.BaseDirectory);

        Assert.False(resolution.ProjectGenerated);
        Assert.False(resolution.SolutionGenerated);
        Assert.Equal(Path.Combine(demoRoot, "PixelEngine.Demo.csproj"), resolution.ProjectPath);
        Assert.Equal(Path.Combine(repositoryRoot, "PixelEngine.sln"), resolution.SolutionPath);
        Assert.Equal(demoRoot, resolution.WorkspaceTarget);
    }

    /// <summary>
    /// 验证用户维护的 csproj/sln 字节不变；已有 slnx 也可作为包含当前工程的上下文。
    /// </summary>
    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void ExistingUserProjectAndSolutionAreNeverOverwritten(string solutionExtension)
    {
        using TempProject temp = new("User Model");
        string projectPath = Path.Combine(temp.Root, "User.Model.csproj");
        string projectText = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n";
        File.WriteAllText(projectPath, projectText);
        string solutionPath = Path.Combine(temp.Root, "User.Model" + solutionExtension);
        string solutionText = solutionExtension == ".sln"
            ? "Microsoft Visual Studio Solution File, Format Version 12.00\nProject(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"User.Model\", \"User.Model.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\nGlobal\nEndGlobal\n"
            : "<Solution><Project Path=\"User.Model.csproj\" /></Solution>\n";
        File.WriteAllText(solutionPath, solutionText);

        EditorCSharpProjectResolution resolution = EditorCodeWorkspaceFiles.ResolveOrGenerate(temp.Project, AppContext.BaseDirectory);

        Assert.False(resolution.ProjectGenerated);
        Assert.False(resolution.SolutionGenerated);
        Assert.Equal(projectPath, resolution.ProjectPath);
        Assert.Equal(solutionPath, resolution.SolutionPath);
        Assert.Equal(projectText, File.ReadAllText(projectPath));
        Assert.Equal(solutionText, File.ReadAllText(solutionPath));
    }

    /// <summary>
    /// 验证 VS Code 打开工程 folder/workspace 而非 sln；System Default 明确打开 solution。
    /// </summary>
    [Fact]
    public void OpenCodeProjectUsesWorkspaceForVsCodeAndSolutionForSystemDefault()
    {
        using TempProject temp = new("Workspace Target");
        string workspace = Path.Combine(temp.Root, "Workspace_Target.code-workspace");
        File.WriteAllText(workspace, "{ \"folders\": [{ \"path\": \".\" }] }");
        RecordingLauncher launcher = new();
        RecordingLocator locator = new(ExternalCodeEditorKind.VsCode, @"C:\Program Files\Microsoft VS Code\Code.exe");
        EditorCodeWorkspaceOpenService service = new(
            temp.Project,
            () => ExternalCodeEditorPreference.VsCode,
            launcher,
            locator,
            AppContext.BaseDirectory);

        EditorCodeWorkspaceOpenResult vscode = service.OpenCodeProject();

        Assert.True(vscode.Success, vscode.Diagnostic);
        ProcessStartInfo vscodeStart = Assert.Single(launcher.Starts);
        Assert.False(vscodeStart.UseShellExecute);
        Assert.Equal(locator.Executable, vscodeStart.FileName);
        Assert.Equal(new[] { "--reuse-window", workspace }, vscodeStart.ArgumentList.ToArray());
        Assert.NotEqual(vscode.SolutionPath, vscode.OpenedTarget);

        launcher.Starts.Clear();
        EditorCodeWorkspaceOpenService systemService = new(
            temp.Project,
            () => ExternalCodeEditorPreference.SystemDefault,
            launcher,
            locator,
            AppContext.BaseDirectory);
        EditorCodeWorkspaceOpenResult system = systemService.OpenCodeProject();

        Assert.True(system.Success, system.Diagnostic);
        ProcessStartInfo systemStart = Assert.Single(launcher.Starts);
        Assert.True(systemStart.UseShellExecute);
        Assert.Equal(system.SolutionPath, systemStart.FileName);
        Assert.Equal(system.SolutionPath, system.OpenedTarget);
    }

    /// <summary>
    /// 验证 automation preparation 不提前写目标文件，commit 才原子发布，重复准备不改写，IDE 变化使旧 plan 失效。
    /// </summary>
    [Fact]
    public void AutomationPreparationDefersFilesUntilCommitAndRejectsStaleEditorPreference()
    {
        using TempProject temp = new("Automation Workspace");
        string preference = ExternalCodeEditorPreference.VsCode;
        RecordingLauncher launcher = new();
        RecordingLocator locator = new(
            ExternalCodeEditorKind.VsCode,
            @"C:\Program Files\Microsoft VS Code\Code.exe");
        EditorCodeWorkspaceOpenService service = new(
            temp.Project,
            () => preference,
            launcher,
            locator,
            AppContext.BaseDirectory);
        EditorCodeWorkspacePreparationDescriptor descriptor = service.CapturePreparationDescriptor();

        using (EditorCodeWorkspacePreparedOpen prepared = service.PrepareCodeProject(
            descriptor,
            CancellationToken.None))
        {
            Assert.True(prepared.FilesChanged);
            Assert.Empty(Directory.GetFiles(temp.Root, "*.csproj", SearchOption.TopDirectoryOnly));
            Assert.Empty(Directory.GetFiles(temp.Root, "*.sln", SearchOption.TopDirectoryOnly));
            EditorCodeWorkspaceOpenResult committed = service.CommitPrepared(prepared);
            Assert.True(committed.Success, committed.Diagnostic);
            Assert.True(File.Exists(committed.ProjectPath));
            Assert.True(File.Exists(committed.SolutionPath));
        }

        descriptor = service.CapturePreparationDescriptor();
        using (EditorCodeWorkspacePreparedOpen unchanged = service.PrepareCodeProject(
            descriptor,
            CancellationToken.None))
        {
            Assert.False(unchanged.FilesChanged);
            EditorCodeWorkspaceOpenResult committed = service.CommitPrepared(unchanged);
            Assert.True(committed.Success, committed.Diagnostic);
        }

        using TempProject staleTemp = new("Stale Automation Workspace");
        preference = ExternalCodeEditorPreference.VsCode;
        EditorCodeWorkspaceOpenService staleService = new(
            staleTemp.Project,
            () => preference,
            launcher,
            locator,
            AppContext.BaseDirectory);
        EditorCodeWorkspacePreparationDescriptor staleDescriptor =
            staleService.CapturePreparationDescriptor();
        using EditorCodeWorkspacePreparedOpen stale = staleService.PrepareCodeProject(
            staleDescriptor,
            CancellationToken.None);
        preference = ExternalCodeEditorPreference.Rider;
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            staleService.CommitPrepared(stale));
        Assert.Contains("失效", failure.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(staleTemp.Root, "*.csproj", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFiles(staleTemp.Root, "*.sln", SearchOption.TopDirectoryOnly));
    }

    /// <summary>
    /// 验证默认脚本双击复用 VS Code workspace 并定位精确行列，同时为 standalone 工程准备项目模型。
    /// </summary>
    [Fact]
    public void VsCodeScriptOpenUsesProjectWorkspaceAndGotoLocation()
    {
        using TempProject temp = new("Script Goto");
        string script = Path.Combine(temp.Project.ScriptSourcePath, "Player Controller.cs");
        File.WriteAllText(script, "public sealed class PlayerController {}\n");
        RecordingLauncher launcher = new();
        RecordingLocator locator = new(ExternalCodeEditorKind.VsCode, @"C:\Users\dev\AppData\Local\Programs\Microsoft VS Code\Code.exe");
        EditorScriptAssetOpenService service = new(
            temp.Project,
            () => ExternalCodeEditorPreference.VsCode,
            launcher,
            locator);

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("ScriptSource/Player Controller.cs", 37, 9);

        Assert.True(result.Success, result.Diagnostic);
        ProcessStartInfo start = Assert.Single(launcher.Starts);
        Assert.Equal(locator.Executable, start.FileName);
        Assert.Equal("--reuse-window", start.ArgumentList[0]);
        Assert.Equal(temp.Root, start.ArgumentList[1]);
        Assert.Equal("--goto", start.ArgumentList[2]);
        Assert.Equal(script + ":37:9", start.ArgumentList[3]);
        Assert.NotEmpty(Directory.GetFiles(temp.Root, "*.csproj", SearchOption.TopDirectoryOnly));
        Assert.NotEmpty(Directory.GetFiles(temp.Root, "*.sln", SearchOption.TopDirectoryOnly));
    }

    /// <summary>
    /// 验证 Visual Studio/Rider 脚本启动参数同时携带 solution context 与定位信息。
    /// </summary>
    [Theory]
    [InlineData((int)ExternalCodeEditorKind.VisualStudio, "visual-studio")]
    [InlineData((int)ExternalCodeEditorKind.Rider, "rider")]
    public void SolutionIdesReceiveProjectContextWhenOpeningScript(int rawKind, string preference)
    {
        using TempProject temp = new("Context IDE");
        string script = Path.Combine(temp.Project.ScriptSourcePath, "Context.cs");
        File.WriteAllText(script, "public sealed class Context {}\n");
        ExternalCodeEditorKind kind = (ExternalCodeEditorKind)rawKind;
        RecordingLauncher launcher = new();
        RecordingLocator locator = new(kind, kind == ExternalCodeEditorKind.VisualStudio ? @"C:\VS\devenv.exe" : @"C:\Rider\rider64.exe");
        EditorScriptAssetOpenService service = new(temp.Project, () => preference, launcher, locator);

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("ScriptSource/Context.cs", 12, 4);

        Assert.True(result.Success, result.Diagnostic);
        ProcessStartInfo start = Assert.Single(launcher.Starts);
        string solution = Directory.GetFiles(temp.Root, "*.sln", SearchOption.TopDirectoryOnly).Single();
        Assert.Equal(solution, start.ArgumentList[0]);
        Assert.Contains(script, start.ArgumentList);
        Assert.Contains(start.ArgumentList, argument => argument.Contains("12", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证历史 script-only custom 命令用于 Open C# Project 时会移除定位占位符并打开工程根。
    /// </summary>
    [Fact]
    public void ScriptOnlyCustomCommandFallsBackToWorkspaceWithoutLiteralPlaceholders()
    {
        using TempProject temp = new("Custom Workspace");
        RecordingLauncher launcher = new();
        RecordingLocator locator = new(ExternalCodeEditorKind.VsCode, @"C:\Tools\code.exe");
        EditorCodeWorkspaceOpenService service = new(
            temp.Project,
            () => "code.cmd --reuse-window --goto {file}:{line}:{column}",
            launcher,
            locator,
            AppContext.BaseDirectory);

        EditorCodeWorkspaceOpenResult result = service.OpenCodeProject();

        Assert.True(result.Success, result.Diagnostic);
        ProcessStartInfo start = Assert.Single(launcher.Starts);
        Assert.Equal(new[] { "--reuse-window", temp.Root }, start.ArgumentList.ToArray());
        Assert.DoesNotContain(start.ArgumentList, argument => argument.Contains('{', StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证 IDE 未安装时返回可见诊断且可进入 Console，不静默回退或假报成功。
    /// </summary>
    [Fact]
    public void MissingVsCodeReturnsVisibleDiagnosticAndDoesNotStartProcess()
    {
        using TempProject temp = new("Missing IDE");
        RecordingLauncher launcher = new();
        MissingLocator locator = new();
        EditorCodeWorkspaceOpenService service = new(
            temp.Project,
            () => ExternalCodeEditorPreference.VsCode,
            launcher,
            locator,
            AppContext.BaseDirectory);

        EditorCodeWorkspaceOpenResult result = service.OpenCodeProject();

        Assert.False(result.Success);
        Assert.Contains("未找到 Visual Studio Code", result.Diagnostic, StringComparison.Ordinal);
        Assert.Empty(launcher.Starts);
        EditorConsoleStore console = new();
        console.AddCodeWorkspaceOpenResult(result);
        EditorConsoleEntry entry = Assert.Single(console.Snapshot());
        Assert.Equal(EditorConsoleSeverity.Error, entry.Severity);
        Assert.Contains(result.Diagnostic, entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Windows 保留设备名不会成为非法 project/solution 文件名。
    /// </summary>
    [Fact]
    public void ReservedWindowsDeviceProjectNameGetsSafeOwnedFileName()
    {
        using TempProject temp = new("CON");

        EditorCSharpProjectResolution resolution = EditorCodeWorkspaceFiles.ResolveOrGenerate(temp.Project, AppContext.BaseDirectory);

        Assert.StartsWith("_CON", Path.GetFileName(resolution.ProjectPath), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("_CON", Path.GetFileName(resolution.SolutionPath), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(resolution.ProjectPath));
        Assert.True(File.Exists(resolution.SolutionPath));
    }

    /// <summary>
    /// 验证用户 solution 中的非法路径只会被忽略，不能从 Assets 菜单打穿 Editor 主循环。
    /// </summary>
    [Fact]
    public void MalformedUserSolutionPathFallsBackWithoutThrowingOrOverwriting()
    {
        using TempProject temp = new("Malformed Model");
        string projectPath = Path.Combine(temp.Root, "Malformed.Model.csproj");
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
        string malformedSolution = Path.Combine(temp.Root, "Malformed.Model.sln");
        string malformedText =
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Broken\", \"bad\0path.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\n" +
            "EndProject\nGlobal\nEndGlobal\n";
        File.WriteAllText(malformedSolution, malformedText);
        RecordingLauncher launcher = new();
        RecordingLocator locator = new(
            ExternalCodeEditorKind.VsCode,
            @"C:\Program Files\Microsoft VS Code\Code.exe");
        EditorCodeWorkspaceOpenService service = new(
            temp.Project,
            () => ExternalCodeEditorPreference.VsCode,
            launcher,
            locator,
            AppContext.BaseDirectory);

        EditorCodeWorkspaceOpenResult result = service.OpenCodeProject();

        Assert.True(result.Success, result.Diagnostic);
        Assert.NotEqual(malformedSolution, result.SolutionPath);
        Assert.Equal(malformedText, File.ReadAllText(malformedSolution));
        _ = Assert.Single(launcher.Starts);
    }

    /// <summary>
    /// 验证自定义 editor 命令继续支持 project/solution/workspace placeholders，不被 preset 逻辑吞掉。
    /// </summary>
    [Fact]
    public void CustomEditorCommandPreservesWorkspacePlaceholders()
    {
        using TempProject temp = new("Custom Editor");
        RecordingLauncher launcher = new();
        EditorCodeWorkspaceOpenService service = new(
            temp.Project,
            () => "\"C:/Custom Tools/editor.exe\" --solution {solution} --root {project} --workspace {workspace}",
            launcher,
            new RecordingLocator(ExternalCodeEditorKind.Custom, "C:/Custom Tools/editor.exe"),
            AppContext.BaseDirectory);

        EditorCodeWorkspaceOpenResult result = service.OpenCodeProject();

        Assert.True(result.Success, result.Diagnostic);
        ProcessStartInfo start = Assert.Single(launcher.Starts);
        Assert.Equal("C:/Custom Tools/editor.exe", start.FileName);
        Assert.Equal(
            new[] { "--solution", result.SolutionPath, "--root", temp.Root, "--workspace", temp.Root },
            start.ArgumentList.ToArray());
    }

    private static ProcessResult RunDotNet(params string[] arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        for (int i = 0; i < arguments.Length; i++)
        {
            process.StartInfo.ArgumentList.Add(arguments[i]);
        }

        Assert.True(process.Start());
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "dotnet build timed out");
        return new ProcessResult(process.ExitCode, stdout + Environment.NewLine + stderr);
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

        throw new DirectoryNotFoundException("无法定位 PixelEngine.sln。");
    }

    private sealed class RecordingLauncher : IExternalScriptEditorProcessLauncher
    {
        public List<ProcessStartInfo> Starts { get; } = [];

        public bool Start(ProcessStartInfo startInfo, out string diagnostic)
        {
            Starts.Add(startInfo);
            diagnostic = string.Empty;
            return true;
        }
    }

    private sealed class RecordingLocator(ExternalCodeEditorKind kind, string executable) : IExternalCodeEditorLocator
    {
        public string Executable { get; } = executable;

        public bool TryLocate(ExternalCodeEditorKind requested, out ExternalCodeEditorInstallation installation, out string diagnostic)
        {
            Assert.Equal(kind, requested);
            installation = new ExternalCodeEditorInstallation(kind, kind.ToString(), Executable);
            diagnostic = string.Empty;
            return true;
        }

        public bool TryResolveCustomExecutable(string command, out string executablePath, out string diagnostic)
        {
            executablePath = Path.IsPathRooted(command) ? command : Executable;
            diagnostic = string.Empty;
            return true;
        }
    }

    private sealed class MissingLocator : IExternalCodeEditorLocator
    {
        public bool TryLocate(ExternalCodeEditorKind kind, out ExternalCodeEditorInstallation installation, out string diagnostic)
        {
            installation = default;
            diagnostic = "未找到 Visual Studio Code。";
            return false;
        }

        public bool TryResolveCustomExecutable(string command, out string executablePath, out string diagnostic)
        {
            _ = command;
            executablePath = string.Empty;
            diagnostic = "未找到自定义外部编辑器。";
            return false;
        }
    }

    private sealed class TempProject : IDisposable
    {
        public TempProject(string name)
        {
            Root = Path.Combine(Path.GetTempPath(), "pixelengine-code-workspace", name + "-" + Guid.NewGuid().ToString("N"));
            Project = EditorProject.CreateNew(Root, name);
        }

        public string Root { get; }

        public EditorProject Project { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private readonly record struct ProcessResult(int ExitCode, string Output);
}
