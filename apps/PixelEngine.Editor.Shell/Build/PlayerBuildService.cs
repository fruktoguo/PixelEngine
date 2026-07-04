using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PixelEngine.Editor.Shell.Build;

internal interface IPlayerBuildService
{
    Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default);

    Task<BuildResult> RunAsync(
        BuildRequest request,
        IProgress<BuildProgressEvent> progress,
        CancellationToken cancellationToken);
}

internal sealed class PlayerBuildService(BuildToolLocator? locator = null) : IPlayerBuildService
{
    private const string EventSchema = "pixelengine.build/v1";
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
        _ = Directory.CreateDirectory(outputDirectory);
        string logPath = Path.Combine(outputDirectory, "build.log");
        FixedTail tail = new(64);
        object logLock = new();
        using StreamWriter logWriter = new(logPath, append: false, Encoding.UTF8)
        {
            AutoFlush = true,
        };

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
                Process target = (Process)state!;
                try
                {
                    if (!target.HasExited)
                    {
                        target.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }, process);

            Task stdoutTask = PumpOutputAsync(
                process.StandardOutput,
                BuildLogLevel.Info,
                progress,
                tail,
                logWriter,
                logLock,
                cancellationToken);
            Task stderrTask = PumpOutputAsync(
                process.StandardError,
                BuildLogLevel.Error,
                progress,
                tail,
                logWriter,
                logLock,
                cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            const string message = "构建已取消，build-player 进程树已终止。";
            BuildProgressEvent canceled = CreateEvent(BuildEventKind.Log, BuildPhase.Unknown, 0, BuildLogLevel.Warning, message);
            progress.Report(canceled);
            lock (logLock)
            {
                logWriter.WriteLine(FormatLogLine(canceled));
            }

            return CreateFailedResult(request, message, -2, tail.Items);
        }

        BuildResult result = await ReadResultAsync(outputDirectory, cancellationToken).ConfigureAwait(false)
            ?? CreateFailedResult(
                request,
                process.ExitCode == 0
                    ? "build-player 未写入 build-result.json。"
                    : $"build-player 失败，exit code={process.ExitCode}。{Environment.NewLine}{string.Join(Environment.NewLine, tail.Items)}",
                process.ExitCode,
                tail.Items);

        result = result with { ExitCode = process.ExitCode };
        BuildLogLevel resultLevel = result.Ok && process.ExitCode == 0 ? BuildLogLevel.Info : BuildLogLevel.Error;
        progress.Report(CreateEvent(
            BuildEventKind.Result,
            result.Ok ? BuildPhase.Done : BuildPhase.Unknown,
            result.Ok ? 1 : 0,
            resultLevel,
            result.Ok ? "构建完成。" : result.Error ?? $"构建失败，exit code={process.ExitCode}。"));
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
            float percent = TryGetSingle(root, "percent", out float parsedPercent) ? Math.Clamp(parsedPercent, 0, 1) : 0;
            string message = TryGetString(root, "message", out string parsedMessage) ? parsedMessage : string.Empty;
            DateTimeOffset timestamp = TryGetString(root, "timestamp", out string timestampText) &&
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
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(tools.BuildPlayerPath);
        }
        else
        {
            startInfo.ArgumentList.Add(tools.BuildPlayerPath);
        }

        AddBuildPlayerArguments(startInfo, request, outputDirectory);
        return new Process { StartInfo = startInfo };
    }

    private static void AddBuildPlayerArguments(ProcessStartInfo startInfo, BuildRequest request, string outputDirectory)
    {
        AddArgument(startInfo, "-Rid", request.Rid);
        AddArgument(startInfo, "-Channel", request.Channel == BuildChannel.Aot ? "aot" : "r2r");
        AddArgument(startInfo, "-Configuration", request.Configuration);
        AddArgument(startInfo, "-Output", outputDirectory);
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
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            BuildProgressEvent buildEvent = TryParseProgressLine(line, out BuildProgressEvent parsed)
                ? parsed
                : CreateEvent(BuildEventKind.Log, BuildPhase.Unknown, 0, fallbackLevel, line);
            tail.Add(line);
            lock (logLock)
            {
                logWriter.WriteLine(FormatLogLine(buildEvent));
            }

            progress.Report(buildEvent);
        }
    }

    private static async Task<BuildResult?> ReadResultAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        string resultPath = Path.Combine(outputDirectory, "build-result.json");
        if (!File.Exists(resultPath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(resultPath);
        return await JsonSerializer.DeserializeAsync(
                stream,
                PixelEngineEditorShellBuildJsonContext.Default.BuildResult,
                cancellationToken)
            .ConfigureAwait(false);
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

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? output.Trim() : string.Empty;
        }
        catch (Exception) when (!OperatingSystem.IsBrowser())
        {
            return string.Empty;
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
            Channel = request.Channel == BuildChannel.Aot ? "aot" : "r2r",
            Configuration = request.Configuration,
            Version = request.Version,
            InformationalVersion = request.InformationalVersion,
            Error = error,
            ExitCode = exitCode,
        };
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
