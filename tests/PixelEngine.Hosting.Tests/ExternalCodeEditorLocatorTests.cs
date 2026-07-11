using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Windows 外部代码编辑器安装探测与 command shim 启动测试。
/// </summary>
public sealed class ExternalCodeEditorLocatorTests
{
    /// <summary>
    /// 验证 PATH 只有 code.cmd 时优先反推同一安装的 Code.exe，避免 CreateProcess 直接执行 cmd shim。
    /// </summary>
    [Fact]
    public void PathCodeCommandShimResolvesAdjacentCodeExecutable()
    {
        FakeDiscoveryEnvironment environment = new();
        string shim = @"C:\Program Files\Microsoft VS Code\bin\code.cmd";
        string executable = @"C:\Program Files\Microsoft VS Code\Code.exe";
        environment.PathCommands["code"] = shim;
        _ = environment.Files.Add(shim);
        _ = environment.Files.Add(executable);
        ExternalCodeEditorLocator locator = new(environment);

        bool found = locator.TryLocate(ExternalCodeEditorKind.VsCode, out ExternalCodeEditorInstallation installation, out string diagnostic);

        Assert.True(found, diagnostic);
        Assert.Equal(executable, installation.ExecutablePath);
    }

    /// <summary>
    /// 验证自动探测不会把缺少真实 executable 的 command shim 当作 IDE。
    /// </summary>
    [Fact]
    public void AutomaticCommandShimWithoutRealExecutableIsRejected()
    {
        FakeDiscoveryEnvironment environment = new();
        string shim = @"C:\Portable Code & Tools\bin\code.cmd";
        environment.PathCommands["code"] = shim;
        _ = environment.Files.Add(shim);
        ExternalCodeEditorLocator locator = new(environment);

        bool found = locator.TryLocate(
            ExternalCodeEditorKind.VsCode,
            out _,
            out string diagnostic);

        Assert.False(found);
        Assert.Contains("未找到 Visual Studio Code", diagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证用户明确配置的 cmd/bat 通过 Windows shell association 启动，并保持参数边界。
    /// </summary>
    [Fact]
    public void ExplicitCustomCommandScriptUsesShellAssociationAndArgumentList()
    {
        string shim = @"C:\Portable Code & Tools\bin\code.cmd";

        System.Diagnostics.ProcessStartInfo startInfo = ExternalCodeEditorProcess.CreateStartInfo(
            shim,
            ["--reuse-window", @"D:\游戏 & Tools"],
            @"D:\游戏 & Tools");

        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(shim, startInfo.FileName);
        Assert.Equal(new[] { "--reuse-window", @"D:\游戏 & Tools" }, startInfo.ArgumentList.ToArray());
    }

    /// <summary>
    /// 验证标准 User Installer 路径和注册表带空格命令均可定位。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VsCodeDiscoverySupportsUserInstallerAndRegistryCommand(bool useUserInstaller)
    {
        FakeDiscoveryEnvironment environment = new();
        string executable = @"C:\Users\dev\AppData\Local\Programs\Microsoft VS Code\Code.exe";
        if (useUserInstaller)
        {
            environment.EnvironmentVariables["LOCALAPPDATA"] = @"C:\Users\dev\AppData\Local";
        }
        else
        {
            environment.RegistryCommands.Add('"' + executable + '"' + " \"%1\"");
        }

        _ = environment.Files.Add(executable);
        ExternalCodeEditorLocator locator = new(environment);

        Assert.True(locator.TryLocate(ExternalCodeEditorKind.VsCode, out ExternalCodeEditorInstallation installation, out string diagnostic), diagnostic);
        Assert.Equal(executable, installation.ExecutablePath);
    }

    /// <summary>
    /// 验证单个损坏的 PATH 候选不会让 Editor 崩溃，后续标准安装来源仍可命中。
    /// </summary>
    [Fact]
    public void MalformedPathCandidateDoesNotBlockValidInstalledEditor()
    {
        FakeDiscoveryEnvironment environment = new();
        environment.PathCommands["code"] = "\0invalid";
        environment.EnvironmentVariables["LOCALAPPDATA"] = @"C:\Users\dev\AppData\Local";
        string executable = @"C:\Users\dev\AppData\Local\Programs\Microsoft VS Code\Code.exe";
        _ = environment.Files.Add(executable);
        ExternalCodeEditorLocator locator = new(environment);

        bool found = locator.TryLocate(
            ExternalCodeEditorKind.VsCode,
            out ExternalCodeEditorInstallation installation,
            out string diagnostic);

        Assert.True(found, diagnostic);
        Assert.Equal(executable, installation.ExecutablePath);
    }

    /// <summary>
    /// 验证 Rider Toolbox 的 PATH cmd shim 不会抢在真实 rider64.exe 前被选中。
    /// </summary>
    [Fact]
    public void RiderToolboxCommandShimFallsThroughToRealExecutable()
    {
        FakeDiscoveryEnvironment environment = new();
        environment.EnvironmentVariables["LOCALAPPDATA"] = @"C:\Users\dev\AppData\Local";
        string shim = @"C:\Users\dev\AppData\Local\JetBrains\Toolbox\scripts\Rider.cmd";
        string install = @"C:\Users\dev\AppData\Local\Programs\Rider";
        string executable = Path.Combine(install, "bin", "rider64.exe");
        environment.PathCommands["rider"] = shim;
        environment.Directories.Add(install);
        _ = environment.Files.Add(shim);
        _ = environment.Files.Add(executable);
        ExternalCodeEditorLocator locator = new(environment);

        bool found = locator.TryLocate(
            ExternalCodeEditorKind.Rider,
            out ExternalCodeEditorInstallation installation,
            out string diagnostic);

        Assert.True(found, diagnostic);
        Assert.Equal(executable, installation.ExecutablePath);
    }

    /// <summary>
    /// 验证 custom 裸命令先从 PATH 解析为绝对路径，且拒绝相对目录，避免工程根同名文件劫持。
    /// </summary>
    [Fact]
    public void CustomExecutableResolutionNeverDependsOnProjectWorkingDirectory()
    {
        FakeDiscoveryEnvironment environment = new();
        string executable = @"C:\Users\dev\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd";
        environment.PathCommands["code.cmd"] = executable;
        _ = environment.Files.Add(executable);
        ExternalCodeEditorLocator locator = new(environment);

        bool found = locator.TryResolveCustomExecutable(
            "code.cmd",
            out string resolved,
            out string diagnostic);
        bool relativeFound = locator.TryResolveCustomExecutable(
            @".\code.cmd",
            out _,
            out string relativeDiagnostic);

        Assert.True(found, diagnostic);
        Assert.Equal(executable, resolved);
        Assert.False(relativeFound);
        Assert.Contains("不能包含目录", relativeDiagnostic, StringComparison.Ordinal);
    }

    private sealed class FakeDiscoveryEnvironment : IExternalCodeEditorDiscoveryEnvironment
    {
        public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> PathCommands { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Directories { get; } = [];

        public List<string> RegistryCommands { get; } = [];

        public string? GetEnvironmentVariable(string name)
        {
            return EnvironmentVariables.GetValueOrDefault(name);
        }

        public string? FindOnPath(string command)
        {
            return PathCommands.GetValueOrDefault(command);
        }

        public bool FileExists(string path)
        {
            return Files.Contains(path);
        }

        public IEnumerable<string> EnumerateDirectories(string root, string pattern, SearchOption searchOption)
        {
            return Directories.Where(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        public string Capture(string executable, string arguments)
        {
            return string.Empty;
        }

        public IEnumerable<string> ReadRegistryCommands(ExternalCodeEditorKind kind)
        {
            return RegistryCommands;
        }
    }
}
