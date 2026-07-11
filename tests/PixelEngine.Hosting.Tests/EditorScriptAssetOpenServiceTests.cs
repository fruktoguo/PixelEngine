using System.Diagnostics;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Project Window 脚本外部编辑器 opener 自动化测试。
/// 不变式：脚本资产经配置的外部编辑器打开、缺失路径给出可诊断错误。
/// </summary>
public sealed class EditorScriptAssetOpenServiceTests
{
    /// <summary>
    /// 验证配置的外部编辑器优先于系统默认 opener，并正确替换 {file} 占位符。
    /// </summary>
    [Fact]
    public void ConfiguredExternalEditorUsesConfiguredCommandAndFilePlaceholder()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        EditorAssetManifestStore manifest = CreateManifestWithScript(temp.Path, out string scriptPath);
        RecordingLauncher launcher = new();
        const string command = "\"C:/Program Files/Code/code.exe\" --goto \"{file}:12\"";
        EditorScriptAssetOpenService service = new(manifest, () => command, launcher, new CustomLocator());

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("scripts/PlayerController.cs");

        // Assert：验证预期结果
        Assert.True(result.Success, result.Diagnostic);
        Assert.False(result.UsedSystemDefault);
        Assert.Equal("scripts/PlayerController.cs", result.LogicalPath);
        Assert.Equal(scriptPath, result.ResolvedPath);
        ProcessStartInfo startInfo = Assert.Single(launcher.Starts);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("C:/Program Files/Code/code.exe", startInfo.FileName);
        Assert.Equal(new[] { "--goto", scriptPath + ":12" }, startInfo.ArgumentList.ToArray());
    }

    /// <summary>
    /// 验证外部编辑器命令没有 {file} 占位符时，会把脚本路径作为最后一个参数追加。
    /// </summary>
    [Fact]
    public void ConfiguredExternalEditorWithoutFilePlaceholderAppendsScriptPathArgument()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        EditorAssetManifestStore manifest = CreateManifestWithScript(temp.Path, out string scriptPath);
        RecordingLauncher launcher = new();
        const string command = "\"C:/Program Files/Code/code.exe\" --reuse-window";
        EditorScriptAssetOpenService service = new(manifest, () => command, launcher, new CustomLocator());

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("scripts/PlayerController.cs");

        // Assert：验证预期结果
        Assert.True(result.Success, result.Diagnostic);
        Assert.False(result.UsedSystemDefault);
        Assert.Equal(scriptPath, result.ResolvedPath);
        ProcessStartInfo startInfo = Assert.Single(launcher.Starts);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("C:/Program Files/Code/code.exe", startInfo.FileName);
        Assert.Equal(new[] { "--reuse-window", scriptPath }, startInfo.ArgumentList.ToArray());
    }

    /// <summary>
    /// 验证未配置外部编辑器时回退 OS/default opener。
    /// </summary>
    [Fact]
    public void UnconfiguredExternalEditorFallsBackToSystemDefaultOpener()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        EditorAssetManifestStore manifest = CreateManifestWithScript(temp.Path, out string scriptPath);
        RecordingLauncher launcher = new();
        EditorScriptAssetOpenService service = new(manifest, () => ExternalCodeEditorPreference.SystemDefault, launcher);

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("scripts/PlayerController.cs");

        // Assert：验证预期结果
        Assert.True(result.Success, result.Diagnostic);
        Assert.True(result.UsedSystemDefault);
        Assert.Equal("system-default", result.EditorCommand);
        ProcessStartInfo startInfo = Assert.Single(launcher.Starts);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(scriptPath, startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
    }

    /// <summary>
    /// 验证启动失败会返回可见诊断，不会静默吞掉错误。
    /// </summary>
    [Fact]
    public void LaunchFailureReturnsVisibleDiagnostic()
    {
        using TempDir temp = new();
        EditorAssetManifestStore manifest = CreateManifestWithScript(temp.Path, out _);
        RecordingLauncher launcher = new(succeed: false, diagnostic: "configured editor not found");
        EditorScriptAssetOpenService service = new(
            manifest,
            () => "missing-editor --open {file}",
            launcher,
            new CustomLocator());

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("scripts/PlayerController.cs");

        Assert.False(result.Success);
        Assert.Contains("configured editor not found", result.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("启动外部脚本编辑器失败", result.Diagnostic, StringComparison.Ordinal);
        _ = Assert.Single(launcher.Starts);
    }

    /// <summary>
    /// 验证非 script 资产不会误触发外部打开。
    /// </summary>
    [Fact]
    public void NonScriptAssetIsRejectedWithoutStartingProcess()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        EditorAssetManifestStore manifest = new(temp.Path, contentRoot);
        _ = manifest.CreateAsset("textures/sand.png", EditorAssetType.Texture, textContents: "texture");
        RecordingLauncher launcher = new();
        EditorScriptAssetOpenService service = new(manifest, () => "code", launcher);

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("textures/sand.png");

        // Assert：验证预期结果
        Assert.False(result.Success);
        Assert.Contains("不是 script 类型", result.Diagnostic, StringComparison.Ordinal);
        Assert.Empty(launcher.Starts);
    }

    /// <summary>
    /// 验证缺文件 / 未登记脚本资产返回诊断且不启动进程。
    /// </summary>
    [Fact]
    public void MissingScriptAssetReturnsDiagnosticWithoutStartingProcess()
    {
        using TempDir temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        EditorAssetManifestStore manifest = new(temp.Path, contentRoot);
        RecordingLauncher launcher = new();
        EditorScriptAssetOpenService service = new(manifest, () => "code", launcher);

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("scripts/MissingBehaviour.cs");

        Assert.False(result.Success);
        Assert.Contains("不存在", result.Diagnostic, StringComparison.Ordinal);
        Assert.Empty(launcher.Starts);
    }

    /// <summary>
    /// 验证路径逃逸会被拒绝并返回可见诊断。
    /// </summary>
    [Fact]
    public void PathEscapeIsRejectedWithoutStartingProcess()
    {
        using TempDir temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        EditorAssetManifestStore manifest = new(temp.Path, contentRoot);
        RecordingLauncher launcher = new();
        EditorScriptAssetOpenService service = new(manifest, () => "code", launcher);

        EditorScriptAssetOpenResult result = service.OpenScriptAsset("../outside.cs");

        Assert.False(result.Success);
        Assert.Contains("越过", result.Diagnostic, StringComparison.Ordinal);
        Assert.Empty(launcher.Starts);
    }

    private static EditorAssetManifestStore CreateManifestWithScript(string projectRoot, out string scriptPath)
    {
        string contentRoot = Path.Combine(projectRoot, "content");
        EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
        EditorAssetRecord script = manifest.CreateAsset("scripts/PlayerController.cs", EditorAssetType.Script);
        scriptPath = Path.GetFullPath(Path.Combine(contentRoot, script.LogicalPath.Replace('/', Path.DirectorySeparatorChar)));
        return manifest;
    }

    private sealed class RecordingLauncher(bool succeed = true, string diagnostic = "") : IExternalScriptEditorProcessLauncher
    {
        public List<ProcessStartInfo> Starts { get; } = [];

        public bool Start(ProcessStartInfo startInfo, out string launchDiagnostic)
        {
            Starts.Add(startInfo);
            launchDiagnostic = diagnostic;
            return succeed;
        }
    }

    private sealed class CustomLocator : IExternalCodeEditorLocator
    {
        public bool TryLocate(ExternalCodeEditorKind kind, out ExternalCodeEditorInstallation installation, out string diagnostic)
        {
            _ = kind;
            installation = default;
            diagnostic = "preset locator 不应由 custom 测试调用。";
            return false;
        }

        public bool TryResolveCustomExecutable(string command, out string executablePath, out string diagnostic)
        {
            executablePath = Path.IsPathRooted(command) ? command : @"C:\Tools\missing-editor.exe";
            diagnostic = string.Empty;
            return true;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelengine-script-opener-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
