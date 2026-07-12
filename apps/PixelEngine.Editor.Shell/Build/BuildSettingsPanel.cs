using System.Collections.Concurrent;
using System.Diagnostics;
using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell.Build;

/// <summary>
/// Build Settings ImGui 面板。
/// </summary>
internal sealed class BuildSettingsPanel : IEditorPanel
{
    public const string PanelTitle = EditorDockSpace.BuildSettingsWindowTitle;
    private static readonly string[] RidOptions = ["win-x64", "win-arm64"];
    private static readonly string[] ChannelOptions = ["R2R", "NativeAOT"];
    private static readonly string[] ConfigurationOptions = ["Release", "Debug"];
    private readonly EditorProject _project;
    private readonly BuildSettingsStore _store;
    private readonly IPlayerBuildService _buildService;
    private readonly IEditorConsoleSink? _console;
    private readonly ConcurrentQueue<BuildProgressEvent> _pendingEvents = new();
    private readonly BuildLog _log = new();
    private readonly BuildProfileDto _settings;
    private BuildRunView _view = new();
    private CancellationTokenSource? _buildCancellation;
    private Task<BuildPreflight>? _preflightTask;
    private Task<BuildResult>? _buildTask;
    private string _validationMessage = string.Empty;
    private bool _autoScroll = true;

    public BuildSettingsPanel(EditorProject project, IPlayerBuildService? buildService = null, IEditorConsoleSink? console = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _project = project;
        _store = new BuildSettingsStore(project);
        _buildService = buildService ?? new PlayerBuildService();
        _console = console;
        _settings = _store.Load();
        Validate();
        StartPreflight();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    public bool TryStartScriptedBuildProbe(string outputDirectory, bool runAfterBuild, out string diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        DrainEvents();
        RefreshTasks();
        if (_view.IsRunning)
        {
            diagnostic = "已有构建正在运行。";
            return false;
        }

        _settings.Rid = BuildHostRid.Current;
        _settings.Channel = BuildProfileChannel.R2R;
        _settings.Configuration = "Release";
        _settings.OutputDirectory = outputDirectory;
        _settings.ProductName = "PixelEngine Demo";
        _settings.Version = "0.1.0";
        _settings.InformationalVersion = "0.1.0+editor-shell-build-probe";
        _settings.IconPath = null;
        _settings.IncludeSymbols = false;
        _settings.PackageWholeContent = true;
        _settings.RunAfterBuild = runAfterBuild;
        _settings.RefreshScenes(_project);
        if (!_settings.TryNormalize(out diagnostic))
        {
            return false;
        }

        Save();
        StartBuild(runAfterBuild);
        diagnostic = "构建探针已启动。";
        return true;
    }

    public ScriptedBuildProbeSnapshot CaptureScriptedBuildProbe()
    {
        DrainEvents();
        RefreshTasks();
        return new ScriptedBuildProbeSnapshot
        {
            Started = _view.StartedAt is not null || _view.Result is not null,
            IsRunning = _view.IsRunning,
            Phase = _view.Phase,
            Percent = _view.Percent,
            Result = _view.Result,
            LogCount = _log.Count,
        };
    }

    /// <summary>
    /// 使用当前 Build Settings profile 启动一次真实构建命令。
    /// </summary>
    /// <param name="runAfterBuild">构建成功后是否立即启动玩家。</param>
    /// <param name="diagnostic">命令是否可执行及其原因。</param>
    /// <returns>命令已成功提交时返回 <see langword="true"/>。</returns>
    public bool TryStartBuild(bool runAfterBuild, out string diagnostic)
    {
        DrainEvents();
        RefreshTasks();
        if (!CanStartBuild(out diagnostic))
        {
            _validationMessage = diagnostic;
            return false;
        }

        StartBuild(runAfterBuild);
        diagnostic = runAfterBuild ? "Build And Run 已启动。" : "Build 已启动。";
        return true;
    }

    public ScriptedBuildSettingsProbeSnapshot ApplyScriptedBuildSettingsProbe(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        DrainEvents();
        RefreshTasks();
        if (_view.IsRunning)
        {
            throw new InvalidOperationException("构建运行中不能修改构建设置。");
        }

        _settings.Rid = BuildHostRid.Current;
        _settings.Channel = BuildProfileChannel.R2R;
        _settings.Configuration = "Debug";
        _settings.OutputDirectory = outputDirectory;
        _settings.ProductName = "PixelEngine Settings Probe";
        _settings.Version = "9.8.7";
        _settings.InformationalVersion = "9.8.7+settings-probe";
        _settings.IconPath = null;
        _settings.IncludeSymbols = true;
        _settings.PackageWholeContent = false;
        _settings.RunAfterBuild = true;
        _settings.RefreshScenes(_project);
        for (int i = 0; i < _settings.Scenes.Count; i++)
        {
            _settings.Scenes[i].Included = true;
        }

        if (!_settings.TryNormalize(out string error))
        {
            throw new InvalidOperationException(error);
        }

        Save();
        return CaptureScriptedBuildSettingsProbe();
    }

    public ScriptedBuildSettingsProbeSnapshot CaptureScriptedBuildSettingsProbe()
    {
        DrainEvents();
        RefreshTasks();
        BuildProfileSceneDto? startup = _settings.Scenes.FirstOrDefault(static scene => scene.IsStartup);
        return new ScriptedBuildSettingsProbeSnapshot
        {
            Rid = _settings.Rid,
            Channel = _settings.Channel,
            Configuration = _settings.Configuration,
            OutputDirectory = _settings.OutputDirectory,
            ProductName = _settings.ProductName,
            Version = _settings.Version,
            InformationalVersion = _settings.InformationalVersion,
            IncludeSymbols = _settings.IncludeSymbols,
            PackageWholeContent = _settings.PackageWholeContent,
            RunAfterBuild = _settings.RunAfterBuild,
            IncludedSceneCount = _settings.Scenes.Count(static scene => scene.Included),
            StartupScene = startup?.Source ?? startup?.SceneName ?? string.Empty,
        };
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        DrainEvents();
        RefreshTasks();
        // 构建面板主布局：设置 → 场景 → 操作 → 进度 → 日志 → 结果
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        // plan/19：可变长度的 scene/log/result 只能滚动 body；Build 动作必须像 Unity 一样
        // 固定在 footer，保证默认小停靠区和 720p 窗口也能完成构建。
        float footerHeight = ImGui.GetTextLineHeightWithSpacing() +
            ImGui.GetFrameHeightWithSpacing() +
            ImGui.GetStyle().ItemSpacing.Y +
            2f;
        float bodyHeight = Math.Max(1f, ImGui.GetContentRegionAvail().Y - footerHeight);
        _ = ImGui.BeginChild("build_settings_body", new System.Numerics.Vector2(0f, bodyHeight));
        ImGui.BeginDisabled(_view.IsRunning);
        DrawSettings();
        ImGui.SeparatorText("场景");
        DrawScenes();
        ImGui.EndDisabled();
        ImGui.SeparatorText("进度");
        DrawProgress();
        ImGui.SeparatorText("日志");
        DrawLog();
        ImGui.SeparatorText("结果");
        DrawResult();
        ImGui.EndChild();
        ImGui.Separator();
        DrawActions();
        ImGui.End();
    }

    private void DrawSettings()
    {
        bool changed = false;
        int rid = IndexOf(RidOptions, _settings.Rid);
        if (ImGui.Combo("目标平台", ref rid, RidOptions, RidOptions.Length) && rid >= 0)
        {
            _settings.Rid = RidOptions[rid];
            changed = true;
        }

        int channel = _settings.Channel == BuildProfileChannel.Aot ? 1 : 0;
        if (ImGui.Combo("通道", ref channel, ChannelOptions, ChannelOptions.Length))
        {
            _settings.Channel = channel == 1 ? BuildProfileChannel.Aot : BuildProfileChannel.R2R;
            changed = true;
        }

        int configuration = IndexOf(ConfigurationOptions, _settings.Configuration);
        if (ImGui.Combo("配置", ref configuration, ConfigurationOptions, ConfigurationOptions.Length) && configuration >= 0)
        {
            _settings.Configuration = ConfigurationOptions[configuration];
            changed = true;
        }

        changed |= InputText("输出目录", _settings.OutputDirectory, value => _settings.OutputDirectory = value, 512);
        changed |= InputText("产物名", _settings.ProductName, value => _settings.ProductName = value, 128);
        changed |= InputText("版本", _settings.Version, value => _settings.Version = value, 64);
        changed |= InputText("信息版本", _settings.InformationalVersion, value => _settings.InformationalVersion = value, 128);
        string icon = _settings.IconPath ?? string.Empty;
        if (InputText("图标 .ico", icon, value => _settings.IconPath = string.IsNullOrWhiteSpace(value) ? null : value, 512))
        {
            changed = true;
        }

        bool includeSymbols = _settings.IncludeSymbols;
        if (ImGui.Checkbox("调试符号", ref includeSymbols))
        {
            _settings.IncludeSymbols = includeSymbols;
            changed = true;
        }

        bool wholeContent = _settings.PackageWholeContent;
        if (ImGui.Checkbox("完整 content", ref wholeContent))
        {
            _settings.PackageWholeContent = wholeContent;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    private void DrawScenes()
    {
        bool changed = false;
        if (ImGui.BeginTable("build_scenes", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("场景名");
            ImGui.TableSetupColumn("入包");
            ImGui.TableSetupColumn("启动");
            ImGui.TableSetupColumn("来源");
            ImGui.TableHeadersRow();
            for (int i = 0; i < _settings.Scenes.Count; i++)
            {
                BuildProfileSceneDto scene = _settings.Scenes[i];
                ImGui.TableNextRow();
                _ = ImGui.TableNextColumn();
                ImGui.TextUnformatted(scene.SceneName);
                _ = ImGui.TableNextColumn();
                bool included = scene.Included;
                if (ImGui.Checkbox($"##include_{i}", ref included))
                {
                    scene.Included = included;
                    if (!included)
                    {
                        scene.IsStartup = false;
                    }

                    changed = true;
                }

                _ = ImGui.TableNextColumn();
                bool startup = scene.IsStartup;
                if (ImGui.RadioButton($"##startup_{i}", startup))
                {
                    SetStartupScene(i);
                    changed = true;
                }

                _ = ImGui.TableNextColumn();
                ImGui.TextUnformatted(scene.Source ?? scene.SourceKind.ToString());
            }

            ImGui.EndTable();
        }

        if (changed)
        {
            Save();
        }
    }

    private void DrawActions()
    {
        bool canBuild = CanStartBuild(out _validationMessage);
        ImGui.TextUnformatted(_validationMessage);
        ImGui.BeginDisabled(!canBuild);
        if (ImGui.Button("Build"))
        {
            StartBuild(runAfterBuild: false);
        }

        ImGui.SameLine();
        if (ImGui.Button("Build And Run"))
        {
            StartBuild(runAfterBuild: true);
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!_view.IsRunning);
        if (ImGui.Button("取消"))
        {
            _buildCancellation?.Cancel();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("重新预检"))
        {
            StartPreflight();
        }
    }

    private bool CanStartBuild(out string diagnostic)
    {
        if (_view.IsRunning)
        {
            diagnostic = "构建正在运行。";
            return false;
        }

        if (!_settings.TryNormalize(out diagnostic))
        {
            return false;
        }

        if (_settings.Channel == BuildProfileChannel.Aot &&
            !string.Equals(_settings.Rid, BuildHostRid.Current, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = $"NativeAOT 仅支持当前宿主 RID：{BuildHostRid.Current}。";
            return false;
        }

        if (_preflightTask is { IsCompleted: false })
        {
            diagnostic = "正在预检构建工具...";
            return false;
        }

        BuildPreflight? preflight = _view.Preflight;
        diagnostic = preflight?.Diagnostic ?? "构建工具尚未完成预检。";
        return preflight?.Ok == true;
    }

    private void DrawProgress()
    {
        ImGui.ProgressBar(_view.Percent, new System.Numerics.Vector2(-1, 0), $"{PhaseLabel(_view.Phase)} {_view.Percent:P0}");
        if (_view.StartedAt is { } startedAt)
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - startedAt;
            ImGui.TextUnformatted($"已用时 {elapsed:mm\\:ss}");
        }
    }

    private void DrawLog()
    {
        bool autoScroll = _autoScroll;
        if (ImGui.Checkbox("自动滚动", ref autoScroll))
        {
            _autoScroll = autoScroll;
        }

        ImGui.SameLine();
        if (ImGui.Button("复制日志"))
        {
            ImGui.SetClipboardText(BuildLogText());
        }

        ImGui.SameLine();
        bool hasOutput = !string.IsNullOrWhiteSpace(_settings.OutputDirectory);
        ImGui.BeginDisabled(!hasOutput);
        if (ImGui.Button("打开 build.log"))
        {
            OpenPath(Path.Combine(ResolveOutputDirectory(), "build.log"));
        }

        ImGui.SameLine();
        if (ImGui.Button("打开产物目录"))
        {
            OpenPath(ResolveOutputDirectory());
        }

        ImGui.EndDisabled();

        _ = ImGui.BeginChild("build_log");
        if (_log.Count == 0)
        {
            ImGui.TextUnformatted("Ready");
        }
        else
        {
            for (int i = 0; i < _log.Count; i++)
            {
                BuildProgressEvent item = _log.GetFromOldest(i);
                ImGui.TextUnformatted($"[{item.Level}] [{item.Phase}] {item.Message}");
            }

            if (_autoScroll)
            {
                ImGui.SetScrollHereY(1);
            }
        }

        ImGui.EndChild();
    }

    private void DrawResult()
    {
        BuildResult? result = _view.Result;
        if (result is null)
        {
            ImGui.TextUnformatted("尚无构建结果。");
            return;
        }

        ImGui.TextUnformatted(result.Ok ? "构建成功" : "构建失败");
        ImGui.TextUnformatted($"ExitCode: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.PackageArchive))
        {
            ImGui.TextUnformatted($"包: {result.PackageArchive}");
        }

        if (!string.IsNullOrWhiteSpace(result.Sha256))
        {
            ImGui.TextUnformatted($"SHA256: {result.Sha256}");
        }

        if (result.SizeBytes > 0)
        {
            ImGui.TextUnformatted($"大小: {result.SizeBytes} bytes");
        }

        if (result.PhaseTimingsMs.Count > 0)
        {
            foreach (KeyValuePair<BuildPhase, double> timing in result.PhaseTimingsMs.OrderBy(static item => item.Key))
            {
                ImGui.TextUnformatted($"{PhaseLabel(timing.Key)}: {timing.Value:F1} ms");
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            ImGui.TextWrapped(result.Error);
        }
    }

    private void StartPreflight()
    {
        _preflightTask = _buildService.PreflightAsync();
        _view = _view with { Preflight = null };
    }

    // 校验并持久化 profile，异步调用 PlayerBuildService 启动子进程构建
    private void StartBuild(bool runAfterBuild)
    {
        if (_view.IsRunning || !_settings.TryNormalize(out _validationMessage))
        {
            return;
        }

        // Build 与 Build And Run 是逐次命令；不能把上一次的启动选择粘滞到下一次 Build。
        _settings.RunAfterBuild = runAfterBuild;
        Save();
        _buildCancellation?.Dispose();
        _buildCancellation = new CancellationTokenSource();
        IProgress<BuildProgressEvent> progress = new Progress<BuildProgressEvent>(_pendingEvents.Enqueue);
        BuildRequest request = PlayerSettingsEditorAdapter.ApplyToBuildRequest(
            _settings.ToRequest() with
            {
                ContentRoot = _project.ContentRootPath,
            },
            new PlayerSettingsStore(_project).Load());
        _buildTask = _buildService.RunAsync(request, progress, _buildCancellation.Token);
        _view = _view with
        {
            IsRunning = true,
            Phase = BuildPhase.Native,
            Percent = 0,
            StartedAt = DateTimeOffset.UtcNow,
            Result = null,
        };
    }

    public void CancelScriptedBuildProbe()
    {
        _buildCancellation?.Cancel();
    }

    // 轮询预检/构建 Task 完成态并更新 UI 视图与控制台
    private void RefreshTasks()
    {
        if (_preflightTask is { IsCompleted: true } preflightTask)
        {
            _preflightTask = null;
            BuildPreflight preflight = preflightTask.Status == TaskStatus.RanToCompletion
                ? preflightTask.Result
                : new BuildPreflight
                {
                    Ok = false,
                    Diagnostic = preflightTask.Exception?.GetBaseException().Message ?? "构建工具预检失败。",
                };
            _view = _view with { Preflight = preflight };
            BuildProgressEvent preflightEvent = new(
                BuildEventKind.Log,
                BuildPhase.Unknown,
                _view.Percent,
                preflight.Ok ? BuildLogLevel.Info : BuildLogLevel.Error,
                preflight.Diagnostic,
                DateTimeOffset.UtcNow);
            _log.Add(preflightEvent);
            _console?.AddBuildPreflight(preflight);
        }

        if (_buildTask is { IsCompleted: true } buildTask)
        {
            _buildTask = null;
            BuildResult result = buildTask.Status == TaskStatus.RanToCompletion
                ? buildTask.Result
                : new BuildResult
                {
                    Ok = false,
                    Error = buildTask.Exception?.GetBaseException().Message ?? "构建任务失败。",
                    ExitCode = -3,
                };
            _view = _view with
            {
                IsRunning = false,
                Phase = result.Ok ? BuildPhase.Done : _view.Phase,
                Percent = result.Ok ? 1 : _view.Percent,
                Result = result,
            };
            _console?.AddBuildResult(result);
            if (result.Ok && _settings.RunAfterBuild)
            {
                LaunchBuildResult(result);
            }
        }
    }

    // 从并发队列取出 build-player JSON 事件，刷新进度条与日志
    private void DrainEvents()
    {
        while (_pendingEvents.TryDequeue(out BuildProgressEvent? item))
        {
            if (item is null)
            {
                continue;
            }

            _log.Add(item);
            _console?.AddBuildEvent(item);
            if (item.Kind is BuildEventKind.Progress or BuildEventKind.Result)
            {
                BuildPhase nextPhase = item.Phase == BuildPhase.Unknown && item.Level == BuildLogLevel.Error
                    ? _view.Phase
                    : item.Phase;
                _view = _view with
                {
                    Phase = nextPhase,
                    Percent = item.Percent,
                };
            }
        }
    }

    private void LaunchBuildResult(BuildResult result)
    {
        if (string.IsNullOrWhiteSpace(result.LauncherExe))
        {
            BuildProgressEvent missingLauncher = new(
                BuildEventKind.Log,
                BuildPhase.Done,
                1,
                BuildLogLevel.Warning,
                "构建结果未提供 LauncherExe，无法 Build And Run。",
                DateTimeOffset.UtcNow);
            _log.Add(missingLauncher);
            _console?.AddBuildEvent(missingLauncher, "build-run");
            return;
        }

        string workingDirectory = string.IsNullOrWhiteSpace(result.PlayerDir)
            ? Path.GetDirectoryName(result.LauncherExe) ?? Environment.CurrentDirectory
            : result.PlayerDir;
        ProcessStartInfo startInfo = new()
        {
            FileName = result.LauncherExe,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            _ = Process.Start(startInfo);
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            BuildProgressEvent launchFailure = new(
                BuildEventKind.Log,
                BuildPhase.Done,
                1,
                BuildLogLevel.Error,
                $"启动玩家包失败：{ex.Message}",
                DateTimeOffset.UtcNow);
            _log.Add(launchFailure);
            _console?.AddBuildEvent(launchFailure, "build-run");
        }
    }

    private void SetStartupScene(int index)
    {
        for (int i = 0; i < _settings.Scenes.Count; i++)
        {
            _settings.Scenes[i].IsStartup = i == index;
            if (i == index)
            {
                _settings.Scenes[i].Included = true;
            }
        }
    }

    private void Save()
    {
        Validate();
        _store.Save(_settings);
    }

    private void Validate()
    {
        _ = _settings.TryNormalize(out _validationMessage);
    }

    private string BuildLogText()
    {
        using StringWriter writer = new();
        for (int i = 0; i < _log.Count; i++)
        {
            BuildProgressEvent item = _log.GetFromOldest(i);
            writer.WriteLine($"{item.Timestamp:O} [{item.Level}] [{item.Phase}] {item.Message}");
        }

        return writer.ToString();
    }

    private string ResolveOutputDirectory()
    {
        string output = _settings.OutputDirectory;
        BuildToolLocatorResult? tools = _view.Preflight?.Tools;
        string root = string.IsNullOrWhiteSpace(tools?.RepositoryRoot) ? Environment.CurrentDirectory : tools.RepositoryRoot;
        return Path.IsPathRooted(output)
            ? Path.GetFullPath(output)
            : Path.GetFullPath(Path.Combine(root, output));
    }

    private void OpenPath(string path)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = path,
                UseShellExecute = true,
            };
            _ = Process.Start(startInfo);
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            _console?.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Build,
                EditorConsoleSeverity.Error,
                "build-path-opener",
                $"打开路径失败：{path}。{ex.Message}"));
        }
    }

    private static string PhaseLabel(BuildPhase phase)
    {
        return phase == BuildPhase.Native ? "native" :
            phase == BuildPhase.Publish ? "publish" :
            phase == BuildPhase.Verify ? "verify" :
            phase == BuildPhase.Package ? "package" :
            phase == BuildPhase.Audit ? "audit" :
            phase == BuildPhase.Done ? "done" :
            "idle";
    }

    private static int IndexOf(string[] values, string value)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static bool InputText(string label, string value, Action<string> assign, uint maxLength)
    {
        string editable = value;
        bool changed = ImGui.InputText(label, ref editable, maxLength);
        if (changed)
        {
            assign(editable);
        }

        return changed;
    }
}
