using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell.Build;

/// <summary>
/// 玩家包构建服务接口。
/// </summary>
internal interface IPlayerBuildService
{
    Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default);

    Task<BuildResult> RunAsync(
        BuildRequest request,
        IProgress<BuildProgressEvent> progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// 调用 build-player 脚本执行玩家包构建。
/// </summary>
internal sealed class PlayerBuildService(BuildToolLocator? locator = null) : IPlayerBuildService
{
    private const string EventSchema = "pixelengine.build/v1";
    internal const int MaximumBuildOutputLineCharacters = 16 * 1024;
    internal const long MaximumBuildLogBytes = 16L * 1024 * 1024;
    private const int MaximumToolVersionCharacters = 16 * 1024;
    private const int OutputReadBufferCharacters = 4096;
    private const string TruncatedLineSuffix = " [truncated at 16384 characters]";
    private const string TruncatedLogMessage = "Build output exceeded the 16 MiB build.log limit; remaining output was discarded.";
    private const long MaximumBuildResultBytes = 1024 * 1024;
    private readonly BuildToolLocator _locator = locator ?? new BuildToolLocator();

    public async Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default)
    {
        BuildToolLocatorResult tools;
        try
        {
            tools = _locator.Locate();
        }
        catch (Exception ex)
        {
            return new BuildPreflight
            {
                Ok = false,
                Diagnostic = ex.Message,
            };
        }

        string dotnetVersion = await TryReadVersionAsync(tools.DotnetPath, "--version", tools.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        string shellVersion;
        if (tools.UsesPowerShell)
        {
            shellVersion = await TryReadVersionAsync(
                    tools.ShellPath,
                    "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"",
                    tools.RepositoryRoot,
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(shellVersion))
            {
                const string fallbackShell = "powershell.exe";
                shellVersion = await TryReadVersionAsync(
                        fallbackShell,
                        "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"",
                        tools.RepositoryRoot,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(shellVersion))
                {
                    tools = tools with { ShellPath = fallbackShell };
                }
            }
        }
        else
        {
            shellVersion = await TryReadVersionAsync(tools.ShellPath, "--version", tools.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        }

        List<string> diagnostics = [];
        if (string.IsNullOrWhiteSpace(dotnetVersion))
        {
            diagnostics.Add("未检测到 dotnet SDK：请确认 dotnet 在 PATH 中可执行。");
        }

        if (string.IsNullOrWhiteSpace(shellVersion))
        {
            diagnostics.Add(tools.UsesPowerShell
                ? "未检测到 pwsh/powershell.exe：Windows 构建需要 PowerShell 执行 tools/build-player.ps1。"
                : "未检测到 sh：当前平台构建需要 sh 执行 tools/build-player.sh。");
        }

        if (!tools.BuildPlayerExists)
        {
            diagnostics.Add($"未找到 build-player 入口：{tools.BuildPlayerPath}");
        }

        return new BuildPreflight
        {
            Ok = diagnostics.Count == 0,
            Tools = tools,
            DotnetVersion = dotnetVersion,
            ShellVersion = shellVersion,
            Diagnostic = diagnostics.Count == 0 ? "构建工具预检通过。" : string.Join(Environment.NewLine, diagnostics),
        };
    }

    // 启动 build-player 子进程，按行解析 JSON 事件并上报进度
    public async Task<BuildResult> RunAsync(
        BuildRequest request,
        IProgress<BuildProgressEvent> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        if (!BuildHostRid.SupportsAot(request))
        {
            string message = $"NativeAOT 仅支持当前宿主 RID：{BuildHostRid.Current}，当前选择为 {request.Rid}。";
            progress.Report(CreateEvent(BuildEventKind.Log, BuildPhase.Unknown, 0, BuildLogLevel.Error, message));
            return CreateFailedResult(request, message, -1, []);
        }

        BuildPreflight preflight = await PreflightAsync(cancellationToken).ConfigureAwait(false);
        if (!preflight.Ok)
        {
            progress.Report(CreateEvent(BuildEventKind.Log, BuildPhase.Unknown, 0, BuildLogLevel.Error, preflight.Diagnostic));
            return CreateFailedResult(request, preflight.Diagnostic, -1, []);
        }

        string outputDirectory = ResolveOutputDirectory(preflight.Tools.RepositoryRoot, request.OutputDirectory);
        EnsureLocalBuildPath(outputDirectory);
        _ = Directory.CreateDirectory(outputDirectory);
        EditorAutomationPathSafety.EnsureNoReparsePoints(outputDirectory, requireLeaf: true);
        string logPath = Path.Combine(outputDirectory, "build.log");
        string resultPath = Path.Combine(outputDirectory, "build-result.json");
        PrepareOutputLeaf(logPath, deleteExisting: false);
        PrepareOutputLeaf(resultPath, deleteExisting: true);
        FixedTail tail = new(64);
        object logLock = new();
        using StreamWriter logWriter = new(logPath, append: false, Encoding.UTF8)
        {
            AutoFlush = true,
        };
        BuildOutputState outputState = new();
        BuildLogWriteState logState = new();

        using Process process = CreateBuildProcess(preflight.Tools, request, outputDirectory);
        try
        {
            progress.Report(CreateEvent(BuildEventKind.Progress, BuildPhase.Native, 0, BuildLogLevel.Info, "启动 build-player。"));
            if (!process.Start())
            {
                const string message = "无法启动 build-player 子进程。";
                progress.Report(CreateEvent(BuildEventKind.Log, BuildPhase.Unknown, 0, BuildLogLevel.Error, message));
                return CreateFailedResult(request, message, -1, tail.Items);
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
            {
                KillProcessTree((Process)state!);
            }, process);

            Task stdoutTask = PumpOutputAsync(
                process.StandardOutput,
                BuildLogLevel.Info,
                progress,
                tail,
                logWriter,
                logLock,
                logState,
                outputState,
                CancellationToken.None);
            Task stderrTask = PumpOutputAsync(
                process.StandardError,
                BuildLogLevel.Error,
                progress,
                tail,
                logWriter,
                logLock,
                logState,
                outputState,
                CancellationToken.None);

            Task waitForExit = process.WaitForExitAsync(CancellationToken.None);
            Task cancellationWait = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Task completed = await Task.WhenAny(waitForExit, cancellationWait).ConfigureAwait(false);
            if (completed == cancellationWait)
            {
                KillProcessTree(process);
                await waitForExit.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                return CreateCanceledResult(request, progress, logWriter, logLock, logState, tail.Items);
            }

            await waitForExit.ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceledResult(request, progress, logWriter, logLock, logState, tail.Items);
        }

        // 子进程已退出后，迟到的取消请求不能把已经完成的构建谎报为 cancelled。
        BuildResult result = await ReadResultAsync(resultPath, CancellationToken.None).ConfigureAwait(false)
            ?? CreateFailedResult(
                request,
                process.ExitCode == 0
                    ? "build-player 未写入 build-result.json。"
                    : $"build-player 失败，exit code={process.ExitCode}。{Environment.NewLine}{string.Join(Environment.NewLine, tail.Items)}",
                process.ExitCode,
                tail.Items);
        if (process.ExitCode != 0 && result.Ok)
        {
            result = result with
            {
                Ok = false,
                Error = $"build-player 失败，exit code={process.ExitCode}。",
            };
        }

        result = ValidateBuildResultPaths(result, outputDirectory);
        result = result with { ExitCode = process.ExitCode };
        BuildLogLevel resultLevel = result.Ok && process.ExitCode == 0 ? BuildLogLevel.Info : BuildLogLevel.Error;
        progress.Report(CreateEvent(
            BuildEventKind.Result,
            result.Ok ? BuildPhase.Done : BuildPhase.Unknown,
            result.Ok ? 1 : 0,
            resultLevel,
            LimitText(
                result.Ok ? "构建完成。" : result.Error ?? $"构建失败，exit code={process.ExitCode}。",
                8192)));
        return result;
    }

    internal static bool TryParseProgressLine(string line, out BuildProgressEvent buildEvent)
    {
        buildEvent = default!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "schema", out string schema) ||
                !string.Equals(schema, EventSchema, StringComparison.Ordinal))
            {
                return false;
            }

            BuildEventKind kind = TryGetString(root, "kind", out string kindText) && Enum.TryParse(kindText, ignoreCase: true, out BuildEventKind parsedKind)
                ? parsedKind
                : BuildEventKind.Log;
            BuildPhase phase = TryGetString(root, "phase", out string phaseText) && Enum.TryParse(phaseText, ignoreCase: true, out BuildPhase parsedPhase)
                ? parsedPhase
                : BuildPhase.Unknown;
            BuildLogLevel level = TryGetString(root, "level", out string levelText) && Enum.TryParse(levelText, ignoreCase: true, out BuildLogLevel parsedLevel)
                ? parsedLevel
                : BuildLogLevel.Info;
            float percent = TryGetSingle(root, "percent", out float parsedPercent)
                ? Math.Clamp(parsedPercent > 1 ? parsedPercent / 100 : parsedPercent, 0, 1)
                : 0;
            string message = TryGetString(root, "message", out string parsedMessage) ? parsedMessage : string.Empty;
            DateTimeOffset timestamp = (TryGetString(root, "timestamp", out string timestampText) ||
                TryGetString(root, "ts", out timestampText)) &&
                DateTimeOffset.TryParse(timestampText, out DateTimeOffset parsedTimestamp)
                    ? parsedTimestamp
                    : DateTimeOffset.UtcNow;
            buildEvent = new BuildProgressEvent(kind, phase, percent, level, message, timestamp);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Process CreateBuildProcess(BuildToolLocatorResult tools, BuildRequest request, string outputDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = tools.ShellPath,
            WorkingDirectory = tools.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (tools.UsesPowerShell)
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(BuildPowerShellBuildPlayerCommand(tools.BuildPlayerPath, request, outputDirectory));
        }
        else
        {
            startInfo.ArgumentList.Add(tools.BuildPlayerPath);
            AddBuildPlayerArguments(startInfo, request, outputDirectory);
        }

        return new Process { StartInfo = startInfo };
    }

    private static string BuildPowerShellBuildPlayerCommand(string buildPlayerPath, BuildRequest request, string outputDirectory)
    {
        StringBuilder command = new();
        _ = command.Append("& ").Append(ToPowerShellLiteral(buildPlayerPath));
        AppendPowerShellArgument(command, "-Rid", request.Rid);
        AppendPowerShellArgument(command, "-Channel", request.Channel == BuildProfileChannel.Aot ? "aot" : "r2r");
        AppendPowerShellArgument(command, "-Configuration", request.Configuration);
        AppendPowerShellArgument(command, "-Output", outputDirectory);
        if (!string.IsNullOrWhiteSpace(request.ContentRoot))
        {
            AppendPowerShellArgument(command, "-ContentRoot", request.ContentRoot);
        }

        AppendPowerShellArgument(command, "-Version", request.Version);
        if (!string.IsNullOrWhiteSpace(request.InformationalVersion))
        {
            AppendPowerShellArgument(command, "-InformationalVersion", request.InformationalVersion);
        }

        AppendPowerShellArgument(command, "-ProductName", request.ProductName);
        if (!string.IsNullOrWhiteSpace(request.IconPath))
        {
            AppendPowerShellArgument(command, "-IconPath", request.IconPath);
        }

        if (request.IncludeSymbols)
        {
            _ = command.Append(" -IncludeSymbols");
        }

        AppendPowerShellArgument(command, "-StartScene", request.StartScene);
        AppendPowerShellArgument(command, "-WindowWidth", request.PlayerWindowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendPowerShellArgument(command, "-WindowHeight", request.PlayerWindowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendPowerShellArgument(command, "-WindowMode", request.PlayerWindowMode.ToString());
        AppendPowerShellArgument(command, "-VSync", request.PlayerVSync ? "true" : "false");
        AppendPowerShellArgument(command, "-RuntimeUiBackend", request.RuntimeUiBackend.ToString());
        AppendPowerShellArgument(command, "-ReleaseChannel", request.ReleaseChannel.ToString());
        if (request.IncludedScenes.Length > 0)
        {
            _ = command.Append(" -IncludeScene @(");
            for (int i = 0; i < request.IncludedScenes.Length; i++)
            {
                if (i > 0)
                {
                    _ = command.Append(',');
                }

                _ = command.Append(ToPowerShellLiteral(request.IncludedScenes[i]));
            }

            _ = command.Append(')');
        }

        _ = command.Append("; exit $LASTEXITCODE");
        return command.ToString();
    }

    private static void AppendPowerShellArgument(StringBuilder command, string name, string value)
    {
        _ = command.Append(' ').Append(name).Append(' ').Append(ToPowerShellLiteral(value));
    }

    private static string ToPowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static void AddBuildPlayerArguments(ProcessStartInfo startInfo, BuildRequest request, string outputDirectory)
    {
        AddArgument(startInfo, "-Rid", request.Rid);
        AddArgument(startInfo, "-Channel", request.Channel == BuildProfileChannel.Aot ? "aot" : "r2r");
        AddArgument(startInfo, "-Configuration", request.Configuration);
        AddArgument(startInfo, "-Output", outputDirectory);
        if (!string.IsNullOrWhiteSpace(request.ContentRoot))
        {
            AddArgument(startInfo, "-ContentRoot", request.ContentRoot);
        }

        AddArgument(startInfo, "-Version", request.Version);
        if (!string.IsNullOrWhiteSpace(request.InformationalVersion))
        {
            AddArgument(startInfo, "-InformationalVersion", request.InformationalVersion);
        }

        AddArgument(startInfo, "-ProductName", request.ProductName);
        if (!string.IsNullOrWhiteSpace(request.IconPath))
        {
            AddArgument(startInfo, "-IconPath", request.IconPath);
        }

        if (request.IncludeSymbols)
        {
            startInfo.ArgumentList.Add("-IncludeSymbols");
        }

        AddArgument(startInfo, "-StartScene", request.StartScene);
        AddArgument(startInfo, "-WindowWidth", request.PlayerWindowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "-WindowHeight", request.PlayerWindowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "-WindowMode", request.PlayerWindowMode.ToString());
        AddArgument(startInfo, "-VSync", request.PlayerVSync ? "true" : "false");
        AddArgument(startInfo, "-RuntimeUiBackend", request.RuntimeUiBackend.ToString());
        AddArgument(startInfo, "-ReleaseChannel", request.ReleaseChannel.ToString());
        for (int i = 0; i < request.IncludedScenes.Length; i++)
        {
            AddArgument(startInfo, "-IncludeScene", request.IncludedScenes[i]);
        }
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static async Task PumpOutputAsync(
        StreamReader reader,
        BuildLogLevel fallbackLevel,
        IProgress<BuildProgressEvent> progress,
        FixedTail tail,
        StreamWriter logWriter,
        object logLock,
        BuildLogWriteState logState,
        BuildOutputState outputState,
        CancellationToken cancellationToken)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(OutputReadBufferCharacters);
        StringBuilder line = new(MaximumBuildOutputLineCharacters);
        bool truncated = false;
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(
                    buffer.AsMemory(0, OutputReadBufferCharacters),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                for (int i = 0; i < read; i++)
                {
                    char character = buffer[i];
                    if (character == '\n')
                    {
                        EmitBuildOutputLine(
                            line,
                            truncated,
                            fallbackLevel,
                            progress,
                            tail,
                            logWriter,
                            logLock,
                            logState,
                            outputState);
                        _ = line.Clear();
                        truncated = false;
                        continue;
                    }

                    if (line.Length < MaximumBuildOutputLineCharacters)
                    {
                        _ = line.Append(character);
                    }
                    else
                    {
                        truncated = true;
                    }
                }
            }

            if (line.Length != 0 || truncated)
            {
                EmitBuildOutputLine(
                    line,
                    truncated,
                    fallbackLevel,
                    progress,
                    tail,
                    logWriter,
                    logLock,
                    logState,
                    outputState);
            }
        }
        finally
        {
            _ = line.Clear();
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void EmitBuildOutputLine(
        StringBuilder source,
        bool truncated,
        BuildLogLevel fallbackLevel,
        IProgress<BuildProgressEvent> progress,
        FixedTail tail,
        StreamWriter logWriter,
        object logLock,
        BuildLogWriteState logState,
        BuildOutputState outputState)
    {
        if (source.Length != 0 && source[^1] == '\r')
        {
            _ = source.Remove(source.Length - 1, 1);
        }

        if (truncated)
        {
            int retained = Math.Max(0, MaximumBuildOutputLineCharacters - TruncatedLineSuffix.Length);
            if (source.Length > retained)
            {
                _ = source.Remove(retained, source.Length - retained);
            }

            _ = source.Append(TruncatedLineSuffix);
        }

        string line = source.ToString();
        BuildProgressEvent buildEvent = TryParseProgressLine(line, out BuildProgressEvent parsed)
            ? parsed
            : CreateEvent(BuildEventKind.Log, outputState.CurrentPhase, 0, fallbackLevel, line);
        buildEvent = buildEvent with
        {
            Message = LimitText(
                buildEvent.Message,
                8192,
                truncated ? TruncatedLineSuffix : null),
        };
        if (buildEvent.Phase != BuildPhase.Unknown)
        {
            outputState.CurrentPhase = buildEvent.Phase;
        }

        tail.Add(line);
        lock (logLock)
        {
            WriteBuildLogLine(logWriter, logState, buildEvent, outputState.CurrentPhase);
        }

        progress.Report(buildEvent);
    }

    private sealed class BuildOutputState
    {
        private int _currentPhase = (int)BuildPhase.Unknown;

        public BuildPhase CurrentPhase
        {
            get => (BuildPhase)Volatile.Read(ref _currentPhase);
            set => Volatile.Write(ref _currentPhase, (int)value);
        }
    }

    private sealed class BuildLogWriteState
    {
        public long WrittenBytes { get; set; } = Encoding.UTF8.GetPreamble().Length;

        public bool TruncationRecorded { get; set; }
    }

    private static async Task<BuildResult?> ReadResultAsync(string resultPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            return null;
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(resultPath, requireLeaf: true);
        await using FileStream stream = new(
            resultPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long resultLength = stream.Length;
        _ = resultLength is > 0 and <= MaximumBuildResultBytes
            ? resultLength
            : throw new InvalidDataException(
                $"build-result.json 必须为 1..{MaximumBuildResultBytes} 字节。");

        return await JsonSerializer.DeserializeAsync(
                stream,
                PixelEngineEditorShellBuildJsonContext.Default.BuildResult,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void PrepareOutputLeaf(string path, bool deleteExisting)
    {
        if (Directory.Exists(path))
        {
            EditorAutomationPathSafety.EnsureNoReparsePoints(path, requireLeaf: true);
            throw new IOException($"Build 输出文件路径被目录占用：{path}");
        }

        if (!File.Exists(path))
        {
            return;
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(path, requireLeaf: true);
        if (deleteExisting)
        {
            File.Delete(path);
        }
    }

    internal static BuildResult ValidateBuildResultPaths(BuildResult result, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        try
        {
            string outputRoot = Path.GetFullPath(outputDirectory);
            EnsureLocalBuildPath(outputRoot);
            if (!Directory.Exists(outputRoot))
            {
                throw new DirectoryNotFoundException($"Build output root 不存在：{outputRoot}");
            }

            EditorAutomationPathSafety.EnsureNoReparsePoints(outputRoot, requireLeaf: true);
            string? packageArchive = ValidateResultPath(
                result.PackageArchive,
                outputRoot,
                requireDirectory: false);
            string? packageDirectory = ValidateResultPath(
                result.PackageDir,
                outputRoot,
                requireDirectory: true);
            string? playerDirectory = ValidateResultPath(
                result.PlayerDir,
                outputRoot,
                requireDirectory: true);
            string? launcher = ValidateResultPath(
                result.LauncherExe,
                outputRoot,
                requireDirectory: false);
            if (result.Ok)
            {
                string expectedPlayerDirectory = Path.GetFullPath(Path.Combine(outputRoot, "player"));
                if (playerDirectory is null || launcher is null ||
                    !PathEquals(playerDirectory, expectedPlayerDirectory) ||
                    !IsStrictDescendant(playerDirectory, launcher))
                {
                    throw new InvalidOperationException(
                        "成功 build 的 playerDir/launcherExe 未绑定到本次 output/player root。");
                }
            }

            return result with
            {
                PackageArchive = packageArchive,
                PackageDir = packageDirectory,
                PlayerDir = playerDirectory,
                LauncherExe = launcher,
            };
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            return result with
            {
                Ok = false,
                PackageArchive = null,
                PackageDir = null,
                PlayerDir = null,
                LauncherExe = null,
                Error = result.Ok
                    ? $"build-result 路径校验失败：{exception.Message}"
                    : result.Error,
            };
        }
    }

    private static string? ValidateResultPath(
        string? path,
        string outputRoot,
        bool requireDirectory)
    {
        if (path is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException("build-result 只能包含 fully-qualified path。");
        }

        string fullPath = Path.GetFullPath(path);
        EnsureLocalBuildPath(fullPath);
        if (!IsStrictDescendant(outputRoot, fullPath))
        {
            throw new InvalidOperationException("build-result path 越出本次 output root。");
        }

        bool exists = requireDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath);
        if (!exists)
        {
            throw new FileNotFoundException("build-result path 不存在。", fullPath);
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(fullPath, requireLeaf: true);
        return fullPath;
    }

    private static bool IsStrictDescendant(string root, string candidate)
    {
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return candidate.StartsWith(
            prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void EnsureLocalBuildPath(string path)
    {
        if (OperatingSystem.IsWindows() && path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Build path 不得使用 UNC 或 device root。");
        }
    }

    private static async Task<string> TryReadVersionAsync(
        string executable,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return string.Empty;
        }

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            if (!process.Start())
            {
                return string.Empty;
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => KillProcessTree((Process)state!),
                process);
            Task<string?> outputTask = ReadBoundedTextAsync(
                process.StandardOutput,
                MaximumToolVersionCharacters,
                cancellationToken);
            Task<string?> errorTask = ReadBoundedTextAsync(
                process.StandardError,
                MaximumToolVersionCharacters,
                cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string? output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            return process.ExitCode == 0 && output is not null ? output.Trim() : string.Empty;
        }
        catch (Exception) when (!OperatingSystem.IsBrowser())
        {
            return string.Empty;
        }
    }

    private static async Task<string?> ReadBoundedTextAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(OutputReadBufferCharacters);
        StringBuilder text = new(Math.Min(maximumCharacters, OutputReadBufferCharacters));
        bool exceeded = false;
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(
                    buffer.AsMemory(0, OutputReadBufferCharacters),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                int remaining = maximumCharacters - text.Length;
                if (remaining > 0)
                {
                    int retained = Math.Min(remaining, read);
                    _ = text.Append(buffer, 0, retained);
                    exceeded |= retained != read;
                }
                else
                {
                    exceeded = true;
                }
            }

            return exceeded ? null : text.ToString();
        }
        finally
        {
            _ = text.Clear();
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string ResolveOutputDirectory(string repositoryRoot, string outputDirectory)
    {
        return Path.IsPathRooted(outputDirectory)
            ? Path.GetFullPath(outputDirectory)
            : Path.GetFullPath(Path.Combine(repositoryRoot, outputDirectory));
    }

    private static BuildResult CreateFailedResult(BuildRequest request, string message, int exitCode, IReadOnlyList<string> tail)
    {
        string error = tail.Count == 0 ? message : $"{message}{Environment.NewLine}{string.Join(Environment.NewLine, tail)}";
        return new BuildResult
        {
            Ok = false,
            Rid = request.Rid,
            Channel = request.Channel == BuildProfileChannel.Aot ? "aot" : "r2r",
            ReleaseChannel = request.ReleaseChannel.ToString(),
            WindowMode = request.PlayerWindowMode.ToString(),
            Configuration = request.Configuration,
            Version = request.Version,
            InformationalVersion = request.InformationalVersion,
            Error = LimitText(error, 32768),
            ExitCode = exitCode,
        };
    }

    private static BuildResult CreateCanceledResult(
        BuildRequest request,
        IProgress<BuildProgressEvent> progress,
        StreamWriter logWriter,
        object logLock,
        BuildLogWriteState logState,
        IReadOnlyList<string> tail)
    {
        const string message = "构建已取消，build-player 进程树已终止。";
        BuildProgressEvent canceled = CreateEvent(BuildEventKind.Result, BuildPhase.Unknown, 0, BuildLogLevel.Warning, message);
        progress.Report(canceled);
        lock (logLock)
        {
            WriteBuildLogLine(logWriter, logState, canceled, BuildPhase.Unknown);
        }

        return CreateFailedResult(request, message, -2, tail);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static BuildProgressEvent CreateEvent(
        BuildEventKind kind,
        BuildPhase phase,
        float percent,
        BuildLogLevel level,
        string message)
    {
        return new BuildProgressEvent(kind, phase, percent, level, message, DateTimeOffset.UtcNow);
    }

    private static string FormatLogLine(BuildProgressEvent item)
    {
        return $"{item.Timestamp:O} [{item.Level}] [{item.Phase}] {item.Message}";
    }

    private static void WriteBuildLogLine(
        StreamWriter writer,
        BuildLogWriteState state,
        BuildProgressEvent item,
        BuildPhase currentPhase)
    {
        if (state.TruncationRecorded)
        {
            return;
        }

        string formatted = FormatLogLine(item);
        long encodedBytes = Encoding.UTF8.GetByteCount(formatted) + Encoding.UTF8.GetByteCount(Environment.NewLine);
        if (state.WrittenBytes + encodedBytes <= MaximumBuildLogBytes)
        {
            writer.WriteLine(formatted);
            state.WrittenBytes += encodedBytes;
            return;
        }

        state.TruncationRecorded = true;
        string marker = FormatLogLine(CreateEvent(
            BuildEventKind.Log,
            currentPhase,
            0,
            BuildLogLevel.Warning,
            TruncatedLogMessage));
        long markerBytes = Encoding.UTF8.GetByteCount(marker) + Encoding.UTF8.GetByteCount(Environment.NewLine);
        if (state.WrittenBytes + markerBytes <= MaximumBuildLogBytes)
        {
            writer.WriteLine(marker);
            state.WrittenBytes += markerBytes;
        }
    }

    private static string LimitText(string? value, int maximumLength, string? retainedSuffix = null)
    {
        string text = value ?? string.Empty;
        return text.Length <= maximumLength
            ? text
            : retainedSuffix is { Length: > 0 } suffix &&
                suffix.Length < maximumLength &&
                text.EndsWith(suffix, StringComparison.Ordinal)
                ? text[..(maximumLength - suffix.Length)] + suffix
                : text[..maximumLength];
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }

    private static bool TryGetSingle(JsonElement element, string propertyName, out float value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetSingle(out value);
    }

    private sealed class FixedTail(int capacity)
    {
        private readonly Queue<string> _items = new(capacity);

        public IReadOnlyList<string> Items => [.. _items];

        public void Add(string item)
        {
            if (_items.Count == capacity)
            {
                _ = _items.Dequeue();
            }

            _items.Enqueue(item);
        }
    }
}
