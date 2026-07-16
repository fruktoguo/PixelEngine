using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Shell;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Rendering;
using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 独立编辑器 build-player 编排、设置校验与玩家包审计测试。
/// 不变式：NDJSON 进度可解析、build-result 与 stderr 合并、设置非法时拒绝构建。
/// </summary>
public sealed class EditorShellBuildTests
{
    /// <summary>
    /// 验证 build-player NDJSON、普通输出、stderr 与 build-result 会合成为真实进度和结果。
    /// </summary>
    [Fact]
    public async Task PlayerBuildServiceParsesNdjsonFallbackStderrAndBuildResult()
    {
        // Arrange：搭建测试场景与依赖
        using TempDir temp = new();
        string script = WriteBuildPlayerScript(
            temp.Path,
            """
            Write-Output '{"schema":"pixelengine.build/v1","kind":"progress","phase":"native","percent":10,"level":"info","message":"native ready","ts":"2026-07-06T00:00:00Z"}'
            Write-Output 'plain current phase log'
            Write-Output '{"schema":"pixelengine.build/v1","kind":"progress","phase":"audit","percent":90,"level":"warning","message":"audit running","ts":"2026-07-06T00:00:01Z"}'
            [Console]::Error.WriteLine('stderr audit line')
            $packageArchive = Join-Path $Output 'PixelEngine-Demo-test-win-x64-r2r.zip'
            $packageDir = Join-Path $Output 'PixelEngine-Demo-test-win-x64-r2r'
            $playerDir = Join-Path $Output 'player'
            $launcherExe = Join-Path $playerDir 'PixelEngine Demo.exe'
            New-Item -ItemType Directory -Force -Path $packageDir,$playerDir | Out-Null
            Set-Content -LiteralPath $packageArchive -Value 'archive' -Encoding ASCII
            Set-Content -LiteralPath $launcherExe -Value 'launcher' -Encoding ASCII
            $result = @{
              ok = $true
              rid = $Rid
              channel = $Channel
              releaseChannel = 'Production'
              configuration = $Configuration
              version = $Version
              informationalVersion = 'test-info'
              packageArchive = $packageArchive
              packageDir = $packageDir
              playerDir = $playerDir
              launcherExe = $launcherExe
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

        // Act：执行被测操作
        BuildResult result = await service.RunAsync(CreateRequest(temp.Path), progress, CancellationToken.None);

        // Assert：验证不变式与预期结果
        Assert.True(result.Ok, result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("win-x64", result.Rid);
        Assert.Equal("Production", result.ReleaseChannel);
        Assert.Equal(
            Path.Combine(temp.Path, "PixelEngine-Demo-test-win-x64-r2r.zip"),
            result.PackageArchive);
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Progress && e.Phase == BuildPhase.Native && Math.Abs(e.Percent - 0.1f) < 0.001f);
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Log && e.Phase == BuildPhase.Native && e.Message == "plain current phase log");
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Progress && e.Phase == BuildPhase.Audit && e.Level == BuildLogLevel.Warning);
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Log && e.Phase == BuildPhase.Audit && e.Level == BuildLogLevel.Error && e.Message == "stderr audit line");
        Assert.Contains(progress.Events, e => e.Kind == BuildEventKind.Result && e.Phase == BuildPhase.Done);

        Assert.True(PlayerBuildService.TryParseProgressLine(
                                 /*lang=json,strict*/
                                 """{"schema":"pixelengine.build/v1","kind":"progress","phase":"publish","percent":50,"level":"info","message":"half","timestamp":"2026-07-06T00:00:00Z"}""",
            out BuildProgressEvent parsed));
        Assert.Equal(BuildPhase.Publish, parsed.Phase);
        Assert.Equal(0.5f, parsed.Percent, precision: 3);
        Assert.False(PlayerBuildService.TryParseProgressLine(/*lang=json,strict*/ """{"schema":"other","kind":"progress"}""", out _));
        Assert.False(PlayerBuildService.TryParseProgressLine("not-json", out _));
    }

    /// <summary>
    /// 验证非零退出码不会被 ok=true 结果掩盖，且缺失 build-result 时回退到尾部日志。
    /// </summary>
    [Fact]
    public async Task PlayerBuildServiceCombinesNonZeroExitAndMissingResultFailures()
    {
        // Arrange：搭建测试场景与依赖
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

        // Act：执行被测操作
        BuildResult nonZero = await okResultService.RunAsync(CreateRequest(okResultTemp.Path), new RecordingProgress(), CancellationToken.None);

        // Assert：验证不变式与预期结果
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

        using TempDir staleTemp = new();
        string staleScript = WriteBuildPlayerScript(staleTemp.Path, "exit 0");
        string staleOutput = Path.Combine(staleTemp.Path, "out");
        string stalePlayer = Path.Combine(staleOutput, "player");
        _ = Directory.CreateDirectory(stalePlayer);
        string staleLauncher = Path.Combine(stalePlayer, "Stale Player.exe");
        File.WriteAllText(staleLauncher, "stale");
        File.WriteAllText(
            Path.Combine(staleOutput, "build-result.json"),
            JsonSerializer.Serialize(
                new BuildResult
                {
                    Ok = true,
                    PlayerDir = stalePlayer,
                    LauncherExe = staleLauncher,
                    ExitCode = 0,
                },
                PixelEngineEditorShellBuildJsonContext.Default.BuildResult));
        PlayerBuildService staleService = new(new FakeLocator(staleTemp.Path, staleScript));

        BuildResult stale = await staleService.RunAsync(
            CreateRequest(staleOutput),
            new RecordingProgress(),
            CancellationToken.None);

        Assert.False(stale.Ok);
        Assert.Equal(0, stale.ExitCode);
        Assert.Contains("未写入 build-result.json", stale.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(staleOutput, "build-result.json")));
    }

    /// <summary>Build 输出叶子不得通过 reparse point 截断 output root 外文件。</summary>
    [Fact]
    public async Task PlayerBuildServiceRejectsReparseOutputLeafBeforeStartingProcess()
    {
        using TempDir temp = new();
        string script = WriteBuildPlayerScript(temp.Path, "exit 0");
        string output = Path.Combine(temp.Path, "out");
        _ = Directory.CreateDirectory(output);
        string outside = Path.Combine(temp.Path, "outside.log");
        File.WriteAllText(outside, "intact");
        string logLink = Path.Combine(output, "build.log");
        _ = File.CreateSymbolicLink(logLink, outside);
        try
        {
            PlayerBuildService service = new(new FakeLocator(temp.Path, script));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RunAsync(
                    CreateRequest(output),
                    new RecordingProgress(),
                    CancellationToken.None));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("intact", File.ReadAllText(outside));
        }
        finally
        {
            if (File.Exists(logLink))
            {
                File.Delete(logLink);
            }
        }
    }

    /// <summary>
    /// 验证取消会终止 build-player 进程树，并且取消后同一服务可再次成功构建。
    /// </summary>
    [Fact]
    public async Task PlayerBuildServiceCancellationKillsProcessTreeAndAllowsRerun()
    {
        // Arrange：搭建测试场景与依赖
        using TempDir temp = new();
        string script = WriteBuildPlayerScript(
            temp.Path,
            """
            Write-Output '{"schema":"pixelengine.build/v1","kind":"progress","phase":"publish","percent":35,"level":"info","message":"waiting","ts":"2026-07-06T00:00:00Z"}'
            if (Test-Path -LiteralPath (Join-Path $Output 'complete.flag')) {
              $packageArchive = Join-Path $Output 'pkg.zip'
              $packageDir = Join-Path $Output 'pkg'
              $playerDir = Join-Path $Output 'player'
              $launcherExe = Join-Path $playerDir 'PixelEngine Demo.exe'
              New-Item -ItemType Directory -Force -Path $packageDir,$playerDir | Out-Null
              Set-Content -LiteralPath $packageArchive -Value 'archive' -Encoding ASCII
              Set-Content -LiteralPath $launcherExe -Value 'launcher' -Encoding ASCII
              $result = @{
                ok = $true
                rid = $Rid
                channel = $Channel
                configuration = $Configuration
                version = $Version
                informationalVersion = ''
                packageArchive = $packageArchive
                packageDir = $packageDir
                playerDir = $playerDir
                launcherExe = $launcherExe
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
        // Act：执行被测操作
        Task<BuildResult> running = service.RunAsync(CreateRequest(output), new RecordingProgress(), cts.Token);
        string childPidPath = Path.Combine(output, "child.pid");
        int childPid = await WaitForPidAsync(childPidPath);

        await cts.CancelAsync();
        BuildResult canceled = await running;

        // Assert：验证不变式与预期结果
        Assert.False(canceled.Ok);
        Assert.Equal(-2, canceled.ExitCode);
        Assert.Contains("已取消", canceled.Error, StringComparison.Ordinal);
        Assert.False(await ProcessExistsAfterDelayAsync(childPid), $"子进程未被杀死: pid={childPid}");

        File.WriteAllText(Path.Combine(output, "complete.flag"), "ok");
        BuildResult rerun = await service.RunAsync(CreateRequest(output), new RecordingProgress(), CancellationToken.None);
        Assert.True(rerun.Ok, rerun.Error);
    }

    /// <summary>
    /// 验证 BuildProfileDto 归一化规则和构建工具预检的可执行诊断。
    /// </summary>
    [Fact]
    public async Task BuildProfileDtoValidationAndPreflightReportActionableErrors()
    {
        // Arrange：准备输入与初始状态
        BuildProfileDto valid = new()
        {
            OutputDirectory = "artifacts/player",
            ProductName = "PixelEngine Demo",
            Version = "0.1.0",
            PackageWholeContent = false,
            Scenes =
            [
                new BuildProfileSceneDto { SceneName = "a", Source = "scenes/a.scene", Included = true, IsStartup = true },
                new BuildProfileSceneDto { SceneName = "b", Source = "scenes/b.scene", Included = true },
                new BuildProfileSceneDto { SceneName = "c", Source = "scenes/c.scene", Included = false },
            ],
        };

        BuildProfileDto normalizedValid = valid.Normalize();
        // Assert：验证预期结果
        Assert.Equal(valid.Rid, normalizedValid.Rid);
        Assert.Equal(valid.Scenes.Count, normalizedValid.Scenes.Count);
        BuildRequest request = normalizedValid.ToRequest();
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
    /// 验证 Hosting settings DTO 的默认值、读写、路径校验与 JSON 往返。
    /// </summary>
    [Fact]
    public void EngineProjectSettingsStoreRoundTripsHostingDtosAndRejectsPathEscape()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        ProjectSettingsDto project = new()
        {
            Name = " PixelEngine Demo ",
            ContentRoot = "content\\",
            ScriptSourceDir = "scripts/./game",
            StartScene = "scenes\\main.scene",
        };
        EngineProjectSettingsStore.SaveProjectSettings(temp.Path, project);

        ProjectSettingsDto loadedProject = EngineProjectSettingsStore.LoadProjectSettings(temp.Path);

        // Assert：验证预期结果
        Assert.Equal("PixelEngine Demo", loadedProject.Name);
        Assert.Equal("content", loadedProject.ContentRoot);
        Assert.Equal("scripts/game", loadedProject.ScriptSourceDir);
        Assert.Equal("scenes/main.scene", loadedProject.StartScene);
        Assert.Contains("\"formatVersion\": 1", File.ReadAllText(Path.Combine(temp.Path, EngineProjectSettingsStore.ProjectSettingsFileName)), StringComparison.Ordinal);

        PlayerSettingsDto player = new()
        {
            WindowTitle = " Demo Player ",
            WindowWidth = 1600,
            WindowHeight = 900,
            WindowMode = PlayerWindowMode.MaximizedWindow,
            VSync = false,
            IconPath = "icons\\game.ico",
            Version = "1.2.3",
            StartupScene = "scenes\\lava-mine.scene",
        };
        EngineProjectSettingsStore.SavePlayerSettings(temp.Path, player);

        PlayerSettingsDto loadedPlayer = EngineProjectSettingsStore.LoadPlayerSettings(temp.Path);

        Assert.Equal("Demo Player", loadedPlayer.WindowTitle);
        Assert.Equal(1600, loadedPlayer.WindowWidth);
        Assert.Equal(900, loadedPlayer.WindowHeight);
        Assert.Equal(PlayerWindowMode.MaximizedWindow, loadedPlayer.WindowMode);
        Assert.False(loadedPlayer.VSync);
        Assert.Equal("icons/game.ico", loadedPlayer.IconPath);
        Assert.Equal("scenes/lava-mine.scene", loadedPlayer.StartupScene);

        BuildProfileDto profile = new()
        {
            Rid = " win-x64 ",
            Channel = BuildProfileChannel.R2R,
            Configuration = " Release ",
            OutputDirectory = "artifacts/player",
            ProductName = " PixelEngine Demo ",
            Version = "0.2.0",
            PackageWholeContent = false,
            Scenes =
            [
                new BuildProfileSceneDto { SceneName = "Lava Mine", Source = "scenes\\lava-mine.scene", Included = true, IsStartup = true },
            ],
        };
        EngineProjectSettingsStore.SaveBuildProfile(temp.Path, profile);

        BuildProfileDto loadedProfile = EngineProjectSettingsStore.LoadBuildProfile(temp.Path);

        Assert.Equal("win-x64", loadedProfile.Rid);
        Assert.Equal("Release", loadedProfile.Configuration);
        Assert.Equal("PixelEngine Demo", loadedProfile.ProductName);
        Assert.False(loadedProfile.PackageWholeContent);
        Assert.Equal("scenes/lava-mine.scene", Assert.Single(loadedProfile.Scenes).Source);

        using TempDir defaultTemp = new();
        BuildProfileDto defaultProfile = EngineProjectSettingsStore.LoadBuildProfile(defaultTemp.Path);
        BuildProfileSceneDto defaultScene = Assert.Single(defaultProfile.Scenes);
        Assert.True(defaultScene.Included);
        Assert.True(defaultScene.IsStartup);
        Assert.Equal("scenes/main.scene", defaultScene.Source);

        _ = Assert.Throws<InvalidOperationException>(() => EngineProjectSettingsStore.SaveProjectSettings(temp.Path, project with { ContentRoot = "../outside" }));
        _ = Assert.Throws<InvalidOperationException>(() => EngineProjectSettingsStore.SavePlayerSettings(temp.Path, player with { StartupScene = "/outside.scene" }));
        _ = Assert.Throws<InvalidOperationException>(() => EngineProjectSettingsStore.SaveBuildProfile(temp.Path, profile with
        {
            Scenes = [profile.Scenes[0] with { Source = "../outside.scene" }],
        }));
    }

    /// <summary>
    /// 验证 settings JSON 中可空嵌套对象会归一化为默认值，坏的场景条目返回可执行诊断而非空引用。
    /// </summary>
    [Fact]
    public void EngineProjectSettingsStoreCoalescesNullNestedSettingsAndReportsNullBuildScenes()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        File.WriteAllText(
            Path.Combine(temp.Path, EngineProjectSettingsStore.ProjectSettingsFileName),
                                 /*lang=json,strict*/
                                 """
            {
              "formatVersion": 1,
              "name": "Null Nested",
              "contentRoot": "content",
              "scriptSourceDir": "scripts",
              "startScene": "scenes/main.scene",
              "resourceRules": null,
              "editorPreferences": null
            }
            """);
        File.WriteAllText(
            Path.Combine(temp.Path, EngineProjectSettingsStore.PlayerSettingsFileName),
                                 /*lang=json,strict*/
                                 """
            {
              "formatVersion": 1,
              "windowTitle": "Null Input",
              "windowWidth": 1280,
              "windowHeight": 720,
              "version": "0.1.0",
              "startupScene": "scenes/main.scene",
              "inputDefaults": null
            }
            """);
        File.WriteAllText(
            Path.Combine(temp.Path, EngineProjectSettingsStore.BuildSettingsFileName),
                                 /*lang=json,strict*/
                                 """
            {
              "formatVersion": 1,
              "rid": "win-x64",
              "channel": "R2R",
              "configuration": "Release",
              "outputDirectory": "artifacts/player",
              "productName": "PixelEngine Demo",
              "version": "0.1.0",
              "scenes": [ null ]
            }
            """);

        ProjectSettingsDto project = EngineProjectSettingsStore.LoadProjectSettings(temp.Path);
        PlayerSettingsDto player = EngineProjectSettingsStore.LoadPlayerSettings(temp.Path);
        // Assert：验证预期结果
        InvalidOperationException buildError = Assert.Throws<InvalidOperationException>(() => EngineProjectSettingsStore.LoadBuildProfile(temp.Path));

        Assert.True(project.ResourceRules.RequireStableMaterialNames);
        Assert.Equal(["materials.json", "reactions.json", "scenes/**/*.scene", "ui/**/*"], project.ResourceRules.ContentFileGlobs);
        Assert.True(project.EditorPreferences.SaveLayoutOnExit);
        Assert.Equal(string.Empty, project.EditorPreferences.ExternalScriptEditor);
        Assert.True(player.InputDefaults.EnableKeyboardMouse);
        Assert.True(player.InputDefaults.EnableGamepad);
        Assert.Equal(PlayerSettingsDto.CurrentFormatVersion, player.FormatVersion);
        Assert.Equal(PlayerWindowMode.Windowed, player.WindowMode);
        Assert.Contains("场景条目不能为空", buildError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Hosting EngineProject 统一入口会合并 settings、startup、Build Profile 与 .scene 扫描。
    /// </summary>
    [Fact]
    public void EngineProjectUnifiedEntryLoadsSettingsStartupAndScannedScenes()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        string scenesRoot = Path.Combine(contentRoot, "scenes");
        _ = Directory.CreateDirectory(scenesRoot);
        WriteScene(Path.Combine(scenesRoot, "start.scene"), "start-stable");
        WriteScene(Path.Combine(scenesRoot, "build-only.scene"), "build-stable");
        WriteScene(Path.Combine(scenesRoot, "bonus.scene"), "bonus-stable");

        ProjectSettingsDto projectSettings = ProjectSettingsDto.CreateDefault("Unified Entry") with
        {
            ContentRoot = "content",
            ScriptSourceDir = "scripts/game",
            StartScene = "scenes/start.scene",
        };
        PlayerSettingsDto playerSettings = new()
        {
            WindowTitle = "Unified Player",
            WindowWidth = 1366,
            WindowHeight = 768,
            VSync = false,
            StartupScene = "scenes/start.scene",
            RuntimeUiBackend = UiBackendKind.Ultralight,
            ReleaseChannel = PlayerReleaseChannel.Production,
        };
        BuildProfileDto buildProfile = new()
        {
            Scenes =
            [
                new BuildProfileSceneDto { SceneName = "start", Source = "scenes/start.scene", SourceKind = SceneSourceKind.SceneFile, Included = true, IsStartup = true },
                new BuildProfileSceneDto { SceneName = "build", Source = "scenes/build-only.scene", SourceKind = SceneSourceKind.SceneFile, Included = true },
            ],
        };
        EngineProjectStartupSettings startupSettings = new()
        {
            StartScene = "scenes/start.scene",
            WindowTitle = "Startup Runtime",
            WindowWidth = 1440,
            WindowHeight = 810,
            VSync = false,
            RuntimeUiBackend = UiBackendKind.Ultralight,
            ReleaseChannel = PlayerReleaseChannel.Production,
        };
        EngineProjectSettingsStore.SaveProjectSettings(temp.Path, projectSettings);
        EngineProjectSettingsStore.SavePlayerSettings(temp.Path, playerSettings);
        EngineProjectSettingsStore.SaveBuildProfile(temp.Path, buildProfile);
        EngineProjectSettingsStore.SaveStartupSettings(contentRoot, startupSettings);

        EngineProject loaded = EngineProject.Load(temp.Path);
        SceneDescriptor[] scenes = loaded.Scenes.ToArray();

        // Assert：验证预期结果
        Assert.Equal(Path.GetFullPath(temp.Path), loaded.ProjectRoot);
        Assert.Equal(Path.GetFullPath(contentRoot), loaded.ContentRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, "scripts/game")), loaded.ScriptSourceDirectory);
        Assert.Equal("start-stable", loaded.StartScene);
        Assert.Equal("Unified Entry", loaded.ProjectSettings!.Name);
        Assert.Equal("Unified Player", loaded.PlayerSettings!.WindowTitle);
        Assert.Equal("Startup Runtime", loaded.StartupSettings!.WindowTitle);
        Assert.Equal(UiBackendKind.Ultralight, loaded.StartupSettings.RuntimeUiBackend);
        Assert.Equal(["start-stable", "build-stable", "bonus-stable"], [.. scenes.Select(static scene => scene.Name)]);
        Assert.All(scenes, scene => Assert.Equal(SceneSourceKind.SceneFile, scene.SourceKind));
        Assert.EndsWith("content/scenes/start.scene", scenes[0].Source!.Replace('\\', '/'), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 EditorShell Project/Player Settings 面板直接读写 Hosting DTO，非法输入不落盘。
    /// </summary>
    [Fact]
    public void EditorShellSettingsStoresAndPanelsRoundTripHostingDtos()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        string projectRoot = Path.Combine(temp.Path, "SettingsProject");
        EditorProject project = EditorProject.CreateNew(projectRoot, " Settings Project ");

        ProjectSettingsStore projectStore = new(project);
        ProjectSettingsDto fallbackProject = projectStore.Load();
        // Assert：验证预期结果
        Assert.Equal("Settings Project", fallbackProject.Name);
        Assert.Equal(project.ContentRoot, fallbackProject.ContentRoot);
        Assert.Equal(project.ScriptSourceDir, fallbackProject.ScriptSourceDir);
        Assert.Equal(project.StartScene, fallbackProject.StartScene);

        ProjectSettingsPanel projectPanel = new(project);
        int projectAppliedCount = 0;
        projectPanel.SettingsApplied += () => projectAppliedCount++;
        ScriptedProjectSettingsProbeSnapshot projectSnapshot = projectPanel.ApplyScriptedProjectSettingsProbe();
        Assert.Equal(1, projectAppliedCount);
        ProjectSettingsDto reloadedProject = projectStore.Load();
        Assert.Equal(projectSnapshot.Name, reloadedProject.Name);
        Assert.Equal(projectSnapshot.Name, project.Name);
        Assert.Equal("scripts/probe", reloadedProject.ScriptSourceDir);
        Assert.Equal("scripts/probe", project.ScriptSourceDir);
        Assert.Equal("scenes/settings-probe.scene", reloadedProject.StartScene);
        Assert.Equal("scenes/settings-probe.scene", project.StartScene);
        Assert.Equal(UiBackendKind.ManagedFallback, reloadedProject.DefaultUiBackend);
        EditorProject reopenedProject = EditorProject.Load(projectRoot);
        Assert.Equal(projectSnapshot.Name, reopenedProject.Name);
        Assert.Equal("scripts/probe", reopenedProject.ScriptSourceDir);
        Assert.Equal("scenes/settings-probe.scene", reopenedProject.StartScene);
        Assert.True(projectSnapshot.RequireStableMaterialNames);

        Assert.False(projectPanel.TryApplyProjectSettings(reloadedProject with { ContentRoot = "../outside" }, out string projectDiagnostic));
        Assert.Equal(1, projectAppliedCount);
        Assert.Contains("ContentRoot", projectDiagnostic, StringComparison.Ordinal);
        Assert.Equal("content", projectStore.Load().ContentRoot);
        Assert.True(projectPanel.TryApplyProjectSettings(reloadedProject with { DefaultUiBackend = UiBackendKind.Ultralight }, out string ultralightDiagnostic));
        Assert.Equal(2, projectAppliedCount);
        Assert.Equal(string.Empty, ultralightDiagnostic);
        ScriptedProjectSettingsProbeSnapshot ultralightProjectSnapshot = projectPanel.CaptureScriptedProjectSettingsProbe();
        Assert.Equal(UiBackendKind.Ultralight, ultralightProjectSnapshot.DefaultUiBackend);
        Assert.Contains("Ultralight optional profile inactive", ultralightProjectSnapshot.DefaultUiBackendDiagnostic, StringComparison.Ordinal);
        Assert.Contains(UltralightOptionalProfileGate.FallbackBackend.ToString(), UltralightOptionalProfileGate.InactiveDisplayLabel, StringComparison.Ordinal);
        Assert.Equal(UiBackendKind.Ultralight, projectStore.Load().DefaultUiBackend);
        Assert.True(projectPanel.TryApplyProjectSettings(
            projectPanel.AppliedSettings,
            out string unchangedProjectDiagnostic));
        Assert.Equal(string.Empty, unchangedProjectDiagnostic);
        Assert.Equal(2, projectAppliedCount);

        PlayerSettingsStore playerStore = new(project);
        PlayerSettingsDto fallbackPlayer = playerStore.Load();
        Assert.Equal(project.Name, fallbackPlayer.WindowTitle);
        Assert.Equal(project.StartScene, fallbackPlayer.StartupScene);

        PlayerSettingsPanel playerPanel = new(project);
        int playerAppliedCount = 0;
        playerPanel.SettingsApplied += () => playerAppliedCount++;
        ScriptedPlayerSettingsProbeSnapshot playerSnapshot = playerPanel.ApplyScriptedPlayerSettingsProbe();
        Assert.Equal(1, playerAppliedCount);
        PlayerSettingsDto reloadedPlayer = playerStore.Load();
        Assert.Equal(playerSnapshot.WindowTitle, reloadedPlayer.WindowTitle);
        Assert.Equal(1600, reloadedPlayer.WindowWidth);
        Assert.Equal(900, reloadedPlayer.WindowHeight);
        Assert.Equal(PlayerWindowMode.MaximizedWindow, reloadedPlayer.WindowMode);
        Assert.False(reloadedPlayer.VSync);
        Assert.Equal("icons/player-probe.ico", reloadedPlayer.IconPath);
        Assert.Equal("4.5.6", reloadedPlayer.Version);
        Assert.Equal("scenes/player-settings-probe.scene", reloadedPlayer.StartupScene);
        Assert.Equal(PlayerReleaseChannel.Production, reloadedPlayer.ReleaseChannel);

        Assert.False(playerPanel.TryApplyPlayerSettings(reloadedPlayer with { WindowWidth = 0 }, out string playerDiagnostic));
        Assert.Equal(1, playerAppliedCount);
        Assert.Contains("窗口尺寸", playerDiagnostic, StringComparison.Ordinal);
        Assert.Equal(1600, playerStore.Load().WindowWidth);
        Assert.True(playerPanel.TryApplyPlayerSettings(
            playerPanel.AppliedSettings,
            out string unchangedPlayerDiagnostic));
        Assert.Equal(string.Empty, unchangedPlayerDiagnostic);
        Assert.Equal(1, playerAppliedCount);
    }

    /// <summary>
    /// 验证 Project/Player Settings 把非法中间输入保留在草稿中，只有显式 Apply 才落盘，
    /// 且窄窗口使用居中浮窗而不是不可编辑的底部窄 dock。
    /// </summary>
    [Fact]
    public void EditorShellSettingsPanelsKeepRecoverableDraftsAndResolveNarrowWindowPlacement()
    {
        // Arrange：建立尚未生成独立 settings 文件的新工程。
        using TempDir temp = new();
        string projectRoot = Path.Combine(temp.Path, "DraftSettingsProject");
        EditorProject project = EditorProject.CreateNew(projectRoot, "Draft Settings Project");
        ProjectSettingsStore projectStore = new(project);
        PlayerSettingsStore playerStore = new(project);
        ProjectSettingsPanel projectPanel = new(project);
        PlayerSettingsPanel playerPanel = new(project);

        // Act：模拟 InputText/InputInt 编辑过程中的暂时非法值。
        projectPanel.StageProjectSettings(projectPanel.DraftSettings with { Name = string.Empty });
        playerPanel.StagePlayerSettings(playerPanel.DraftSettings with { WindowWidth = 0 });

        // Assert：非法草稿保持可见且不污染磁盘，用户仍可 Revert。
        Assert.True(projectPanel.HasPendingChanges);
        Assert.Equal(string.Empty, projectPanel.DraftSettings.Name);
        Assert.False(projectPanel.TryApplyDraft(out string projectDiagnostic));
        Assert.Contains("工程名不能为空", projectDiagnostic, StringComparison.Ordinal);
        Assert.False(File.Exists(projectStore.SettingsPath));
        projectPanel.RevertDraft();
        Assert.False(projectPanel.HasPendingChanges);
        Assert.Equal("Draft Settings Project", projectPanel.DraftSettings.Name);

        Assert.True(playerPanel.HasPendingChanges);
        Assert.Equal(0, playerPanel.DraftSettings.WindowWidth);
        Assert.False(playerPanel.TryApplyDraft(out string playerDiagnostic));
        Assert.Contains("窗口尺寸", playerDiagnostic, StringComparison.Ordinal);
        Assert.False(File.Exists(playerStore.SettingsPath));
        playerPanel.RevertDraft();
        Assert.False(playerPanel.HasPendingChanges);
        Assert.Equal(1280, playerPanel.DraftSettings.WindowWidth);

        // Act：合法草稿只在 Apply 时原子保存并同步工程模型。
        projectPanel.StageProjectSettings(projectPanel.DraftSettings with { Name = "Applied Project Name" });
        Assert.True(projectPanel.TryApplyDraft(out projectDiagnostic), projectDiagnostic);
        Assert.Equal("Applied Project Name", projectStore.Load().Name);
        Assert.Equal("Applied Project Name", project.Name);
        Assert.False(projectPanel.HasPendingChanges);

        // Assert：672x483 的真实窄窗口仍得到 640x451 的完整可滚动浮窗。
        EditorSettingsWindowPlacement placement = EditorSettingsWindowLayout.Resolve(
            Vector2.Zero,
            new Vector2(672f, 483f),
            EditorUiScale.Default);
        Assert.Equal(new Vector2(16f, 16f), placement.Position);
        Assert.Equal(new Vector2(640f, 451f), placement.Size);
        Assert.True(placement.MinimumSize.X <= placement.Size.X);
        Assert.True(placement.MinimumSize.Y <= placement.Size.Y);

        // label/value 分栏随可用宽度收缩，并始终优先保留可编辑的 value 区。
        Assert.Equal(220f, EditorSettingsWindowLayout.ResolveLabelWidth(780f, 1f));
        Assert.Equal(151.2f, EditorSettingsWindowLayout.ResolveLabelWidth(420f, 1f), precision: 3);
        Assert.Equal(120f, EditorSettingsWindowLayout.ResolveLabelWidth(300f, 1f));
        Assert.Equal(240f, EditorSettingsWindowLayout.ResolveLabelWidth(640f, 2f));
    }

    /// <summary>
    /// 验证设置写盘失败会保留草稿和既有状态并返回可执行诊断，不会让 UI 帧抛异常退出。
    /// </summary>
    [Fact]
    public void EditorShellSettingsPanelsKeepDraftWhenAtomicSaveFails()
    {
        // Arrange：用同名目录稳定制造目标文件提交失败。
        using TempDir temp = new();
        string projectRoot = Path.Combine(temp.Path, "SettingsFailureProject");
        EditorProject project = EditorProject.CreateNew(projectRoot, "Settings Failure Project");
        ProjectSettingsPanel panel = new(project);
        _ = Directory.CreateDirectory(Path.Combine(projectRoot, EngineProjectSettingsStore.ProjectSettingsFileName));
        panel.StageProjectSettings(panel.DraftSettings with { Name = "Pending Name" });

        // Act。
        bool applied = panel.TryApplyDraft(out string diagnostic);

        // Assert：失败可见、草稿可恢复、运行中的工程模型未被半更新。
        Assert.False(applied);
        Assert.Contains("Failed to save Project Settings", diagnostic, StringComparison.Ordinal);
        Assert.Equal(diagnostic, panel.ValidationMessage);
        Assert.True(panel.HasPendingChanges);
        Assert.Equal("Pending Name", panel.DraftSettings.Name);
        Assert.Equal("Settings Failure Project", project.Name);
        Assert.Empty(Directory.EnumerateFiles(projectRoot, "ProjectSettings.json.*.tmp"));
    }

    /// <summary>
    /// 验证三类损坏 settings 不再阻断工程打开，且恢复加载不会静默覆盖原文件；
    /// 只有用户显式 Apply/修复后才写入可再次严格加载的配置。
    /// </summary>
    [Fact]
    public void EditorShellRecoversCorruptSettingsAndRepairsOnlyOnExplicitApply()
    {
        // Arrange：建立有效 project.pixelproj，再分别损坏三类独立 settings。
        using TempDir temp = new();
        string projectRoot = Path.Combine(temp.Path, "CorruptSettingsProject");
        _ = EditorProject.CreateNew(projectRoot, "Recovery Project");
        const string corruptJson = "{ definitely-not-json";
        string projectSettingsPath = Path.Combine(projectRoot, EngineProjectSettingsStore.ProjectSettingsFileName);
        string playerSettingsPath = Path.Combine(projectRoot, EngineProjectSettingsStore.PlayerSettingsFileName);
        string buildSettingsPath = Path.Combine(projectRoot, EngineProjectSettingsStore.BuildSettingsFileName);
        File.WriteAllText(projectSettingsPath, corruptJson);
        File.WriteAllText(playerSettingsPath, corruptJson);
        File.WriteAllText(buildSettingsPath, corruptJson);

        // Act：重新打开工程并创建设置面板。
        EditorProject project = EditorProject.Load(projectRoot);
        ProjectSettingsPanel projectPanel = new(project);
        PlayerSettingsPanel playerPanel = new(project);
        BuildSettingsPanel buildPanel = new(project, new ImmediateBuildService());
        int buildAppliedCount = 0;
        buildPanel.SettingsApplied += () => buildAppliedCount++;

        // Assert：工程使用 project.pixelproj/default 回退值继续打开，坏文件仍原样保留并明确要求修复。
        Assert.Equal("Recovery Project", project.Name);
        Assert.Contains("已使用 project.pixelproj", project.ProjectSettingsDiagnostic, StringComparison.Ordinal);
        Assert.True(projectPanel.RequiresRepair);
        Assert.True(projectPanel.HasPendingChanges);
        Assert.False(projectPanel.HasDraftChanges);
        Assert.Contains("点击 Apply 修复", projectPanel.ValidationMessage, StringComparison.Ordinal);
        Assert.True(playerPanel.RequiresRepair);
        Assert.True(playerPanel.HasPendingChanges);
        Assert.Contains("点击 Apply 修复", playerPanel.ValidationMessage, StringComparison.Ordinal);
        Assert.True(buildPanel.RequiresRepair);
        Assert.Contains("重新保存以修复", buildPanel.SettingsDiagnostic, StringComparison.Ordinal);
        Assert.Equal(corruptJson, File.ReadAllText(projectSettingsPath));
        Assert.Equal(corruptJson, File.ReadAllText(playerSettingsPath));
        Assert.Equal(corruptJson, File.ReadAllText(buildSettingsPath));

        // Act：模拟三个设置窗口上的显式 Apply/修复操作。
        Assert.True(projectPanel.TryApplyDraft(out string projectDiagnostic), projectDiagnostic);
        Assert.True(playerPanel.TryApplyDraft(out string playerDiagnostic), playerDiagnostic);
        Assert.True(buildPanel.TryRepairSettings(out string buildDiagnostic), buildDiagnostic);
        Assert.Equal(1, buildAppliedCount);
        Assert.True(buildPanel.TryRepairSettings(out string unchangedBuildDiagnostic), unchangedBuildDiagnostic);
        Assert.Equal(1, buildAppliedCount);

        // Assert：恢复标记清空，三份文件均可由严格入口重新加载。
        Assert.False(projectPanel.RequiresRepair);
        Assert.False(playerPanel.RequiresRepair);
        Assert.False(buildPanel.RequiresRepair);
        Assert.Equal(string.Empty, project.ProjectSettingsDiagnostic);
        Assert.Equal("Recovery Project", EngineProjectSettingsStore.LoadProjectSettings(projectRoot).Name);
        Assert.Equal("Recovery Project", EngineProjectSettingsStore.LoadPlayerSettings(projectRoot).WindowTitle);
        Assert.NotEmpty(EngineProjectSettingsStore.LoadBuildProfile(projectRoot).Scenes);
    }

    /// <summary>
    /// 验证 ProjectSettings.json 已提交但 project.pixelproj 同步失败时会回滚前者，
    /// 避免磁盘两份工程设置进入半成功状态。
    /// </summary>
    [Fact]
    public void ProjectSettingsStoreRollsBackWhenProjectDocumentSynchronizationFails()
    {
        // Arrange：先落盘一份有效基线，再把 project.pixelproj 目标替换成同名目录制造第二阶段失败。
        using TempDir temp = new();
        string projectRoot = Path.Combine(temp.Path, "ProjectSettingsRollback");
        EditorProject project = EditorProject.CreateNew(projectRoot, "Rollback Baseline");
        ProjectSettingsStore store = new(project);
        ProjectSettingsDto baseline = store.Load();
        store.Save(baseline);
        string baselineJson = File.ReadAllText(store.SettingsPath);
        File.Delete(project.ProjectFilePath);
        _ = Directory.CreateDirectory(project.ProjectFilePath);

        // Act。
        Exception? error = Record.Exception(() => store.Save(baseline with { Name = "Must Roll Back" }));

        // Assert：同步失败可见，settings 恢复到精确旧内容，运行中模型也未半更新。
        Assert.NotNull(error);
        Assert.True(error is IOException or UnauthorizedAccessException or InvalidOperationException, error.ToString());
        Assert.Equal(baselineJson, File.ReadAllText(store.SettingsPath));
        Assert.Equal("Rollback Baseline", project.Name);
    }

    /// <summary>
    /// 验证 PlayerSettingsDto 同源投影到 build-player 请求与 headless EngineOptions。
    /// </summary>
    [Fact]
    public void PlayerSettingsProjectionFeedsBuildRequestAndRuntimeOptions()
    {
        // Arrange：准备输入与初始状态
        BuildRequest request = CreateRequest("artifacts/player") with
        {
            ProductName = "Build Profile Name",
            Version = "0.0.1",
            IconPath = "icons/build.ico",
            IncludedScenes = ["scenes/extra.scene"],
        };
        PlayerSettingsDto settings = new()
        {
            WindowTitle = "Player Projection",
            WindowWidth = 1440,
            WindowHeight = 810,
            WindowMode = PlayerWindowMode.BorderlessFullscreen,
            VSync = false,
            IconPath = "icons/player.ico",
            Version = "2.3.4",
            StartupScene = "scenes/player.scene",
            RuntimeUiBackend = UiBackendKind.Ultralight,
            ReleaseChannel = PlayerReleaseChannel.Production,
        };

        BuildRequest projected = PlayerSettingsEditorAdapter.ApplyToBuildRequest(request, settings);

        // Assert：验证预期结果
        Assert.Equal("Player Projection", projected.ProductName);
        Assert.Equal("2.3.4", projected.Version);
        Assert.Equal("icons/player.ico", projected.IconPath);
        Assert.Equal("scenes/player.scene", projected.StartScene);
        Assert.Equal(["scenes/extra.scene", "scenes/player.scene"], projected.IncludedScenes);
        Assert.Equal(1440, projected.PlayerWindowWidth);
        Assert.Equal(810, projected.PlayerWindowHeight);
        Assert.Equal(PlayerWindowMode.BorderlessFullscreen, projected.PlayerWindowMode);
        Assert.False(projected.PlayerVSync);
        Assert.Equal(UiBackendKind.Ultralight, projected.RuntimeUiBackend);
        Assert.Equal(PlayerReleaseChannel.Production, projected.ReleaseChannel);

        PlayerSettingsRuntimeProjectionSnapshot snapshot = PlayerSettingsEditorAdapter.CaptureRuntimeProjection(settings);
        Assert.Equal("Player Projection", snapshot.WindowTitle);
        Assert.Equal(PlayerWindowMode.BorderlessFullscreen, snapshot.WindowMode);
        Assert.Equal(UiBackendKind.Ultralight, snapshot.RuntimeUiBackend);

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .ApplyRuntimeDefaults(settings)
            .Build();
        Assert.Equal("Player Projection", engine.Context.Options.WindowTitle);
        Assert.Equal(1440, engine.Context.Options.WindowWidth);
        Assert.Equal(810, engine.Context.Options.WindowHeight);
        Assert.Equal(PlayerWindowMode.BorderlessFullscreen, engine.Context.Options.WindowMode);
        Assert.False(engine.Context.Options.VSync);
        Assert.Equal("scenes/player.scene", engine.Context.Options.StartScene);
        Assert.True(engine.Context.Options.EnableGuiRuntime);
        Assert.True(engine.Context.Options.EnableGameUi);
        Assert.Equal(UiBackendKind.Ultralight, engine.Context.Options.GameUiBackend);
    }

    /// <summary>
    /// 验证 PlayerBuildService 会把 PlayerSettings 派生的 runtime 参数传给 build-player。
    /// </summary>
    [Fact]
    public async Task PlayerBuildServicePassesPlayerSettingsRuntimeArgumentsToBuildPlayer()
    {
        // Arrange：搭建测试场景与依赖
        using TempDir temp = new();
        string script = WriteBuildPlayerScript(
            temp.Path,
            """
            $received = @{
              startScene = $StartScene
              contentRoot = $ContentRoot
              windowWidth = $WindowWidth
              windowHeight = $WindowHeight
              windowMode = $WindowMode
              vSync = $VSync
              runtimeUiBackend = $RuntimeUiBackend
              releaseChannel = $ReleaseChannel
              includeSceneCount = $IncludeScene.Count
            }
            $received | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'received-args.json') -Encoding UTF8
            $packageArchive = Join-Path $Output 'pkg.zip'
            $packageDir = Join-Path $Output 'pkg'
            $playerDir = Join-Path $Output 'player'
            $launcherExe = Join-Path $playerDir 'Player Projection.exe'
            New-Item -ItemType Directory -Force -Path $packageDir,$playerDir | Out-Null
            Set-Content -LiteralPath $packageArchive -Value 'archive' -Encoding ASCII
            Set-Content -LiteralPath $launcherExe -Value 'launcher' -Encoding ASCII
            $result = @{
              ok = $true
              rid = $Rid
              channel = $Channel
              windowMode = $WindowMode
              configuration = $Configuration
              version = $Version
              informationalVersion = ''
              packageArchive = $packageArchive
              packageDir = $packageDir
              playerDir = $playerDir
              launcherExe = $launcherExe
              sha256 = 'abc'
              sizeBytes = 1
              phaseTimingsMs = @{}
              warnings = @()
              error = $null
              exitCode = 0
            }
            $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'build-result.json') -Encoding UTF8
            exit 0
            """);
        PlayerBuildService service = new(new FakeLocator(temp.Path, script));
        string output = Path.Combine(temp.Path, "out");
        BuildRequest request = CreateRequest(output) with
        {
            StartScene = "scenes/player.scene",
            ContentRoot = Path.Combine(temp.Path, "project-content"),
            IncludedScenes = ["scenes/player.scene", "scenes/extra.scene"],
            PlayerWindowWidth = 1440,
            PlayerWindowHeight = 810,
            PlayerWindowMode = PlayerWindowMode.BorderlessFullscreen,
            PlayerVSync = false,
            RuntimeUiBackend = UiBackendKind.Ultralight,
            ReleaseChannel = PlayerReleaseChannel.Production,
        };

        // Act：执行被测操作
        BuildResult result = await service.RunAsync(request, new RecordingProgress(), CancellationToken.None);

        // Assert：验证不变式与预期结果
        Assert.True(result.Ok, result.Error);
        Assert.Equal(PlayerWindowMode.BorderlessFullscreen.ToString(), result.WindowMode);
        string received = File.ReadAllText(Path.Combine(output, "received-args.json"));
        Assert.Contains("\"startScene\": \"scenes/player.scene\"", received, StringComparison.Ordinal);
        Assert.Contains("\"contentRoot\":", received, StringComparison.Ordinal);
        Assert.Contains("project-content", received, StringComparison.Ordinal);
        Assert.Contains("\"windowWidth\": 1440", received, StringComparison.Ordinal);
        Assert.Contains("\"windowHeight\": 810", received, StringComparison.Ordinal);
        Assert.Contains("\"windowMode\": \"BorderlessFullscreen\"", received, StringComparison.Ordinal);
        Assert.Contains("\"vSync\": \"false\"", received, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"runtimeUiBackend\": \"Ultralight\"", received, StringComparison.Ordinal);
        Assert.Contains("\"releaseChannel\": \"Production\"", received, StringComparison.Ordinal);
        Assert.Contains("\"includeSceneCount\": 2", received, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证真实 release audit 脚本拒绝编辑器闭包、允许玩家 HUD 所需 ImGui，并区分 dev/strict 布局。
    /// </summary>
    [Fact]
    public void PlayerPackageAuditRejectsEditorClosureAllowsImGuiAndSupportsDevLayout()
    {
        // Arrange：准备输入与初始状态
        string shell = FindPowerShell();
        // Assert：验证预期结果
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
            Channel = BuildProfileChannel.R2R,
            Configuration = "Release",
            OutputDirectory = outputDirectory,
            Version = "0.1.0",
            InformationalVersion = "test",
            ProductName = "PixelEngine Demo",
            StartScene = "scenes/lava-mine.scene",
            IncludedScenes = ["scenes/lava-mine.scene"],
        };
    }

    private static void WriteScene(string path, string name)
    {
        File.WriteAllText(
            path,
            $$"""
            {
              "formatVersion": 2,
              "name": "{{name}}",
              "entities": []
            }
            """);
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
          [string]$ContentRoot,
          [string]$Version,
          [string]$InformationalVersion,
          [string]$ProductName,
          [string]$IconPath,
          [switch]$IncludeSymbols,
          [string]$StartScene,
          [int]$WindowWidth,
          [int]$WindowHeight,
          [string]$WindowMode,
          [string]$VSync,
          [string]$RuntimeUiBackend,
          [string]$ReleaseChannel,
          [string[]]$IncludeScene
        )
        $ErrorActionPreference = 'Stop'
        New-Item -ItemType Directory -Force -Path $Output | Out-Null
        {{body}}
        """;
        File.WriteAllText(path, script, Encoding.UTF8);
        return path;
    }

    private static void AssertInvalid(BuildProfileDto settings, string expected)
    {
        Assert.False(settings.TryNormalize(out string error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(settings.Normalize);
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
        _ = Directory.CreateDirectory(Path.Combine(root, "app"));
        _ = Directory.CreateDirectory(Path.Combine(root, "content", "textures"));
        foreach ((string Relative, string Contents) in RequiredPackageFiles(includeImGui))
        {
            string path = Path.Combine(root, Relative.Replace('/', Path.DirectorySeparatorChar));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Contents);
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
        string[] files = [.. Directory.GetFiles(expandedRoot, "*", SearchOption.AllDirectories)
            .Where(static file => !string.Equals(Path.GetFileName(file), "SHA256SUMS", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        using StreamWriter writer = new(Path.Combine(expandedRoot, "SHA256SUMS"), append: false, Encoding.UTF8);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(expandedRoot, file).Replace('\\', '/');
            writer.WriteLine($"{Sha256(file)}  {relative}");
        }
    }

    private static void CreatePackageRootChecksum(string packageRoot, string expandedRoot)
    {
        _ = Directory.CreateDirectory(packageRoot);
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
                _ = archive.CreateEntryFromFile(file, $"{rootName}/{relative}", CompressionLevel.NoCompression);
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
        List<string> arguments =
        [
            "-PublishRoot",
            publish,
            "-PackageRoot",
            package,
            "-ActiveRids",
            "win-x64",
        ];
        if (devLayout)
        {
            arguments.Add("-DevLayout");
        }

        ProcessStartInfo startInfo = Utf8TestProcess.CreatePowerShell(
            RepositoryRoot(),
            Path.Combine(RepositoryRoot(), "tools", "audit-release-artifacts.ps1"),
            arguments,
            shell);

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

    private sealed class ImmediateBuildService : IPlayerBuildService
    {
        public Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new BuildPreflight
            {
                Ok = true,
                Diagnostic = "Ready",
            });
        }

        public Task<BuildResult> RunAsync(
            BuildRequest request,
            IProgress<BuildProgressEvent> progress,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = progress;
            _ = cancellationToken;
            return Task.FromResult(new BuildResult
            {
                Ok = true,
                ExitCode = 0,
            });
        }
    }

    private sealed class RecordingProgress : IProgress<BuildProgressEvent>
    {
        private readonly ConcurrentQueue<BuildProgressEvent> _events = new();

        public IReadOnlyCollection<BuildProgressEvent> Events => [.. _events];

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
            _ = Directory.CreateDirectory(Path);
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
