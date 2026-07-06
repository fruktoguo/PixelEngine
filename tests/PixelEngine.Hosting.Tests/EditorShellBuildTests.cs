using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PixelEngine.Editor.Shell.Build;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 独立编辑器内 build-player 编排、设置校验与玩家包审计测试。
/// </summary>
public sealed class EditorShellBuildTests
{
    /// <summary>
    /// 验证 build-player NDJSON、普通输出、stderr 与 build-result 会合成为真实进度和结果。
    /// </summary>
    [Fact]
    public async Task PlayerBuildServiceParsesNdjsonFallbackStderrAndBuildResult()
    {
        using TempDir temp = new();
        string script = WriteBuildPlayerScript(
            temp.Path,
            """
            Write-Output '{"schema":"pixelengine.build/v1","kind":"progress","phase":"native","percent":10,"level":"info","message":"native ready","ts":"2026-07-06T00:00:00Z"}'
            Write-Output 'plain current phase log'
            Write-Output '{"schema":"pixelengine.build/v1","kind":"progress","phase":"audit","percent":90,"level":"warning","message":"audit running","ts":"2026-07-06T00:00:01Z"}'
            [Console]::Error.WriteLine('stderr audit line')
            $result = @{
              ok = $true
              rid = $Rid
              channel = $Channel
              configuration = $Configuration
              version = $Version
              informationalVersion = 'test-info'
              packageArchive = 'PixelEngine-Demo-test-win-x64-r2r.zip'
              packageDir = 'PixelEngine-Demo-test-win-x64-r2r'
              playerDir = 'player'
              launcherExe = 'PixelEngine Demo.exe'
              sha256 = 'abc'
              sizeBytes = 123
              phaseTimingsMs = @{ native = 1.5; audit = 2.5 }
              warnings = @('warn')
              error = $null
              exitCode = 0
            }
            $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'build-result.json') -Encoding UTF8
            exit 0
            """);
        PlayerBuildService service = new(new FakeLocator(temp.Path, script));
        RecordingProgress progress = new();

        BuildResult result = await service.RunAsync(CreateRequest(temp.Path), progress, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("win-x64", result.Rid);
        Assert.Equal("PixelEngine-Demo-test-win-x64-r2r.zip", result.PackageArchive);
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Progress && e.Phase == BuildPhase.Native && Math.Abs(e.Percent - 0.1f) < 0.001f);
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Log && e.Phase == BuildPhase.Native && e.Message == "plain current phase log");
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Progress && e.Phase == BuildPhase.Audit && e.Level == BuildLogLevel.Warning);
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Log && e.Phase == BuildPhase.Audit && e.Level == BuildLogLevel.Error && e.Message == "stderr audit line");
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Result && e.Phase == BuildPhase.Done);

        Assert.True(PlayerBuildService.TryParseProgressLine(
            """{"schema":"pixelengine.build/v1","kind":"progress","phase":"publish","percent":50,"level":"info","message":"half","timestamp":"2026-07-06T00:00:00Z"}""",
            out BuildProgressEvent parsed));
        Assert.Equal(BuildPhase.Publish, parsed.Phase);
        Assert.Equal(0.5f, parsed.Percent, precision: 3);
        Assert.False(PlayerBuildService.TryParseProgressLine("""{"schema":"other","kind":"progress"}""", out _));
        Assert.False(PlayerBuildService.TryParseProgressLine("not-json", out _));
    }

    /// <summary>
    /// 验证非零退出码不会被 ok=true 结果掩盖，且缺失 build-result 时回退到尾部日志。
    /// </summary>
    [Fact]
    public async Task PlayerBuildServiceCombinesNonZeroExitAndMissingResultFailures()
    {
        using TempDir okResultTemp = new();
        string okResultScript = WriteBuildPlayerScript(
            okResultTemp.Path,
            """
            $result = @{
              ok = $true
              rid = $Rid
              channel = $Channel
              configuration = $Configuration
              version = $Version
              informationalVersion = ''
              packageArchive = 'pkg.zip'
              packageDir = 'pkg'
              playerDir = 'player'
              launcherExe = 'PixelEngine Demo.exe'
              sha256 = 'abc'
              sizeBytes = 1
              phaseTimingsMs = @{}
              warnings = @()
              error = $null
              exitCode = 0
            }
            $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'build-result.json') -Encoding UTF8
            exit 7
            """);
        PlayerBuildService okResultService = new(new FakeLocator(okResultTemp.Path, okResultScript));

        BuildResult nonZero = await okResultService.RunAsync(CreateRequest(okResultTemp.Path), new RecordingProgress(), CancellationToken.None);

        Assert.False(nonZero.Ok);
        Assert.Equal(7, nonZero.ExitCode);
        Assert.Contains("exit code=7", nonZero.Error, StringComparison.Ordinal);

        using TempDir missingTemp = new();
        string missingScript = WriteBuildPlayerScript(
            missingTemp.Path,
            """
            Write-Output 'stdout tail'
            [Console]::Error.WriteLine('stderr tail')
            exit 6
            """);
        PlayerBuildService missingService = new(new FakeLocator(missingTemp.Path, missingScript));

        BuildResult missing = await missingService.RunAsync(CreateRequest(missingTemp.Path), new RecordingProgress(), CancellationToken.None);

        Assert.False(missing.Ok);
        Assert.Equal(6, missing.ExitCode);
        Assert.Contains("exit code=6", missing.Error, StringComparison.Ordinal);
        Assert.Contains("stdout tail", missing.Error, StringComparison.Ordinal);
        Assert.Contains("stderr tail", missing.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证取消会终止 build-player 进程树，并且取消后同一服务可再次成功构建。
    /// </summary>
    [Fact]
    public async Task PlayerBuildServiceCancellationKillsProcessTreeAndAllowsRerun()
    {
        using TempDir temp = new();
        string script = WriteBuildPlayerScript(
            temp.Path,
            """
            Write-Output '{"schema":"pixelengine.build/v1","kind":"progress","phase":"publish","percent":35,"level":"info","message":"waiting","ts":"2026-07-06T00:00:00Z"}'
            if (Test-Path -LiteralPath (Join-Path $Output 'complete.flag')) {
              $result = @{
                ok = $true
                rid = $Rid
                channel = $Channel
                configuration = $Configuration
                version = $Version
                informationalVersion = ''
                packageArchive = 'pkg.zip'
                packageDir = 'pkg'
                playerDir = 'player'
                launcherExe = 'PixelEngine Demo.exe'
                sha256 = 'abc'
                sizeBytes = 1
                phaseTimingsMs = @{}
                warnings = @()
                error = $null
                exitCode = 0
              }
              $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'build-result.json') -Encoding UTF8
              exit 0
            }
            $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 600') -WindowStyle Hidden -PassThru
            Set-Content -LiteralPath (Join-Path $Output 'child.pid') -Value $child.Id -Encoding ASCII
            while ($true) { Start-Sleep -Milliseconds 200 }
            """);
        PlayerBuildService service = new(new FakeLocator(temp.Path, script));
        string output = Path.Combine(temp.Path, "out");
        using CancellationTokenSource cts = new();
        Task<BuildResult> running = service.RunAsync(CreateRequest(output), new RecordingProgress(), cts.Token);
        string childPidPath = Path.Combine(output, "child.pid");
        int childPid = await WaitForPidAsync(childPidPath);

        await cts.CancelAsync();
        BuildResult canceled = await running;

        Assert.False(canceled.Ok);
        Assert.Equal(-2, canceled.ExitCode);
        Assert.Contains("已取消", canceled.Error, StringComparison.Ordinal);
        Assert.False(await ProcessExistsAfterDelayAsync(childPid), $"子进程未被杀死: pid={childPid}");

        File.WriteAllText(Path.Combine(output, "complete.flag"), "ok");
        BuildResult rerun = await service.RunAsync(CreateRequest(output), new RecordingProgress(), CancellationToken.None);
        Assert.True(rerun.Ok, rerun.Error);
    }

    /// <summary>
    /// 验证 BuildTargetSettings 归一化规则和构建工具预检的可执行诊断。
    /// </summary>
    [Fact]
    public async Task BuildTargetSettingsValidationAndPreflightReportActionableErrors()
    {
        BuildTargetSettings valid = new()
        {
            OutputDirectory = "artifacts/player",
            ProductName = "PixelEngine Demo",
            Version = "0.1.0",
            PackageWholeContent = false,
            Scenes =
            [
                new SceneBuildEntry { SceneName = "a", Source = "scenes/a.scene", Included = true, IsStartup = true },
                new SceneBuildEntry { SceneName = "b", Source = "scenes/b.scene", Included = true },
                new SceneBuildEntry { SceneName = "c", Source = "scenes/c.scene", Included = false },
            ],
        };

        Assert.Same(valid, valid.Normalize());
        BuildRequest request = valid.ToRequest();
        Assert.Equal("scenes/a.scene", request.StartScene);
        Assert.Equal(["scenes/a.scene", "scenes/b.scene"], request.IncludedScenes);
        AssertInvalid(valid with { Scenes = [valid.Scenes[0] with { IsStartup = true }, valid.Scenes[1] with { IsStartup = true }] }, "只能选择一个启动场景");
        AssertInvalid(valid with { Scenes = [valid.Scenes[0] with { Included = false }] }, "启动场景必须入包");
        AssertInvalid(valid with { Scenes = [valid.Scenes[0] with { Included = false, IsStartup = false }] }, "至少需要一个入包场景");
        AssertInvalid(valid with { ProductName = " " }, "产物名不能为空");
        AssertInvalid(valid with { OutputDirectory = "bad\0path" }, "输出目录包含非法路径字符");
        AssertInvalid(valid with { IconPath = "bad\0icon.ico" }, "图标路径包含非法路径字符");

        using TempDir temp = new();
        PlayerBuildService service = new(new FakeLocator(
            temp.Path,
            buildPlayerPath: Path.Combine(temp.Path, "missing-build-player.ps1"),
            dotnetPath: Path.Combine(temp.Path, "missing-dotnet.exe"),
            shellPath: Path.Combine(temp.Path, "missing-pwsh.exe"),
            buildPlayerExists: false,
            usesPowerShell: false));
        BuildPreflight preflight = await service.PreflightAsync();

        Assert.False(preflight.Ok);
        Assert.Contains("dotnet SDK", preflight.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("sh", preflight.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("build-player", preflight.Diagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证真实 release audit 脚本拒绝编辑器闭包、允许玩家 HUD 所需 ImGui，并区分 dev/strict 布局。
    /// </summary>
    [Fact]
    public void PlayerPackageAuditRejectsEditorClosureAllowsImGuiAndSupportsDevLayout()
    {
        string shell = FindPowerShell();
        Assert.False(string.IsNullOrWhiteSpace(shell), "player-only audit 测试需要 pwsh。");

        using TempDir temp = new();
        string publish = Path.Combine(temp.Path, "publish");
        string package = Path.Combine(temp.Path, "package");
        string clean = CreateExpandedPackage(package, includeEditor: false, includeGuizmo: false, includeImPlot: false, includeImGui: true, includeDebugFiles: false);
        CreatePackageRootChecksum(package, clean);

        RunAudit(shell, publish, package, devLayout: false, expectSuccess: true);

        File.WriteAllText(Path.Combine(clean, "app", "PixelEngine.Demo.pdb"), "symbols");
        RewriteExpandedPackageChecksum(clean);
        RunAudit(shell, publish, package, devLayout: false, expectSuccess: false, "调试");
        RunAudit(shell, publish, package, devLayout: true, expectSuccess: true);

        File.Delete(Path.Combine(clean, "app", "PixelEngine.Demo.pdb"));
        File.WriteAllText(Path.Combine(clean, "app", "PixelEngine.Editor.dll"), "editor");
        RewriteExpandedPackageChecksum(clean);
        RunAudit(shell, publish, package, devLayout: true, expectSuccess: false, "编辑器专属闭包");

        File.Delete(Path.Combine(clean, "app", "PixelEngine.Editor.dll"));
        File.WriteAllText(Path.Combine(clean, "app", "Hexa.NET.ImGuizmo.dll"), "guizmo");
        RewriteExpandedPackageChecksum(clean);
        RunAudit(shell, publish, package, devLayout: true, expectSuccess: false, "编辑器专属闭包");

        File.Delete(Path.Combine(clean, "app", "Hexa.NET.ImGuizmo.dll"));
        File.WriteAllText(Path.Combine(clean, "app", "ImPlot.Native.dll"), "implot");
        RewriteExpandedPackageChecksum(clean);
        RunAudit(shell, publish, package, devLayout: true, expectSuccess: false, "编辑器专属闭包");
    }

    private static BuildRequest CreateRequest(string outputDirectory)
    {
        return new BuildRequest
        {
            Rid = "win-x64",
            Channel = BuildChannel.R2R,
            Configuration = "Release",
            OutputDirectory = outputDirectory,
            Version = "0.1.0",
            InformationalVersion = "test",
            ProductName = "PixelEngine Demo",
            StartScene = "scenes/lava-mine.scene",
            IncludedScenes = ["scenes/lava-mine.scene"],
        };
    }

    private static string WriteBuildPlayerScript(string directory, string body)
    {
        string path = Path.Combine(directory, "build-player-test.ps1");
        string script = $$"""
        param(
          [string]$Rid,
          [string]$Channel,
          [string]$Configuration,
          [string]$Output,
          [string]$Version,
          [string]$InformationalVersion,
          [string]$ProductName,
          [string]$IconPath,
          [switch]$IncludeSymbols,
          [string]$StartScene,
          [string[]]$IncludeScene
        )
        $ErrorActionPreference = 'Stop'
        New-Item -ItemType Directory -Force -Path $Output | Out-Null
        {{body}}
        """;
        File.WriteAllText(path, script, Encoding.UTF8);
        return path;
    }

    private static void AssertInvalid(BuildTargetSettings settings, string expected)
    {
        Assert.False(settings.TryNormalize(out string error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => settings.Normalize());
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static async Task<int> WaitForPidAsync(string path)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (File.Exists(path) && int.TryParse((await File.ReadAllTextAsync(path)).Trim(), out int pid))
            {
                return pid;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"等待子进程 PID 超时：{path}");
    }

    private static async Task<bool> ProcessExistsAfterDelayAsync(int pid)
    {
        for (int i = 0; i < 40; i++)
        {
            if (!IsProcessAlive(pid))
            {
                return false;
            }

            await Task.Delay(50);
        }

        return IsProcessAlive(pid);
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string CreateExpandedPackage(
        string packageRoot,
        bool includeEditor,
        bool includeGuizmo,
        bool includeImPlot,
        bool includeImGui,
        bool includeDebugFiles)
    {
        string root = Path.Combine(packageRoot, "PixelEngine-Demo-test-win-x64-r2r");
        Directory.CreateDirectory(Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.Combine(root, "content", "textures"));
        foreach ((string Relative, string Contents) file in RequiredPackageFiles(includeImGui))
        {
            string path = Path.Combine(root, file.Relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Contents);
        }

        if (includeEditor)
        {
            File.WriteAllText(Path.Combine(root, "app", "PixelEngine.Editor.dll"), "editor");
        }

        if (includeGuizmo)
        {
            File.WriteAllText(Path.Combine(root, "app", "Hexa.NET.ImGuizmo.dll"), "guizmo");
        }

        if (includeImPlot)
        {
            File.WriteAllText(Path.Combine(root, "app", "ImPlot.Native.dll"), "implot");
        }

        if (includeDebugFiles)
        {
            File.WriteAllText(Path.Combine(root, "app", "PixelEngine.Demo.pdb"), "symbols");
        }

        RewriteExpandedPackageChecksum(root);
        return root;
    }

    private static IEnumerable<(string Relative, string Contents)> RequiredPackageFiles(bool includeImGui)
    {
        yield return ("README.txt", "readme");
        yield return ("NOTICE.txt", "notice");
        yield return ("PixelEngine Demo.exe", "launcher");
        yield return ("app/PixelEngine.Demo.dll", "demo");
        yield return ("app/runtimes/win-x64/native/PixelEngine.UI.Native.dll", "ui native");
        yield return ("content/materials.json", "{}");
        yield return ("content/reactions.json", "{}");
        yield return ("content/weapons.json", "{}");
        yield return ("content/textures/17_gravel.png", "png");
        yield return ("content/textures/18_boundary_stone.png", "png");
        yield return ("content/scenes/lava-mine.scene", "{}");
        if (includeImGui)
        {
            yield return ("app/Hexa.NET.ImGui.dll", "imgui");
        }
    }

    private static void RewriteExpandedPackageChecksum(string expandedRoot)
    {
        string[] files = Directory.GetFiles(expandedRoot, "*", SearchOption.AllDirectories)
            .Where(static file => !string.Equals(Path.GetFileName(file), "SHA256SUMS", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        using StreamWriter writer = new(Path.Combine(expandedRoot, "SHA256SUMS"), append: false, Encoding.UTF8);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(expandedRoot, file).Replace('\\', '/');
            writer.WriteLine($"{Sha256(file)}  {relative}");
        }
    }

    private static void CreatePackageRootChecksum(string packageRoot, string expandedRoot)
    {
        Directory.CreateDirectory(packageRoot);
        string packagePath = Path.Combine(packageRoot, "PixelEngine-Demo-test-win-x64-r2r.zip");
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        string rootName = Path.GetFileName(expandedRoot);
        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.GetFiles(expandedRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(expandedRoot, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, $"{rootName}/{relative}", CompressionLevel.NoCompression);
            }
        }

        File.WriteAllText(Path.Combine(packageRoot, "SHA256SUMS"), $"{Sha256(packagePath)}  {Path.GetFileName(packagePath)}{Environment.NewLine}");
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RunAudit(string shell, string publish, string package, bool devLayout, bool expectSuccess, string? expected = null)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = shell,
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot(), "tools", "audit-release-artifacts.ps1"));
        startInfo.ArgumentList.Add("-PublishRoot");
        startInfo.ArgumentList.Add(publish);
        startInfo.ArgumentList.Add("-PackageRoot");
        startInfo.ArgumentList.Add(package);
        startInfo.ArgumentList.Add("-ActiveRids");
        startInfo.ArgumentList.Add("win-x64");
        if (devLayout)
        {
            startInfo.ArgumentList.Add("-DevLayout");
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 audit-release-artifacts.ps1。");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        string output = stdout + Environment.NewLine + stderr;
        if (expectSuccess)
        {
            Assert.True(process.ExitCode == 0, output);
        }
        else
        {
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(expected!, output, StringComparison.Ordinal);
        }
    }

    private static string FindPowerShell()
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return string.Empty;
    }

    private static string RepositoryRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "PixelEngine.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory) ?? string.Empty;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine.sln。");
    }

    private sealed class FakeLocator(
        string root,
        string buildPlayerPath,
        string dotnetPath = "dotnet",
        string? shellPath = null,
        bool buildPlayerExists = true,
        bool usesPowerShell = true) : BuildToolLocator
    {
        public override BuildToolLocatorResult Locate()
        {
            return new BuildToolLocatorResult
            {
                RepositoryRoot = root,
                BuildPlayerPath = buildPlayerPath,
                BuildPlayerExists = buildPlayerExists && File.Exists(buildPlayerPath),
                DotnetPath = dotnetPath,
                ShellPath = shellPath ?? FindPowerShell(),
                UsesPowerShell = usesPowerShell,
            };
        }
    }

    private sealed class RecordingProgress : IProgress<BuildProgressEvent>
    {
        private readonly ConcurrentQueue<BuildProgressEvent> _events = new();

        public IReadOnlyCollection<BuildProgressEvent> Events => _events.ToArray();

        public void Report(BuildProgressEvent value)
        {
            _events.Enqueue(value);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelengine-build-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
