using System.Collections.Concurrent;
using System.Diagnostics;
using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell.Build;

internal sealed class BuildSettingsPanel : IEditorPanel
{
    public const string PanelTitle = EditorDockSpace.BuildSettingsWindowTitle;
    private static readonly string[] RidOptions = ["win-x64", "win-arm64"];
    private static readonly string[] ChannelOptions = ["R2R", "NativeAOT"];
    private static readonly string[] ConfigurationOptions = ["Release", "Debug"];
    private readonly EditorProject _project;
    private readonly BuildSettingsStore _store;
    private readonly IPlayerBuildService _buildService;
    private readonly ConcurrentQueue<BuildProgressEvent> _pendingEvents = new();
    private readonly BuildLog _log = new();
    private readonly BuildTargetSettings _settings;
    private BuildRunView _view = new();
    private CancellationTokenSource? _buildCancellation;
    private Task<BuildPreflight>? _preflightTask;
    private Task<BuildResult>? _buildTask;
    private string _validationMessage = string.Empty;
    private bool _autoScroll = true;

    public BuildSettingsPanel(EditorProject project, IPlayerBuildService? buildService = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _project = project;
        _store = new BuildSettingsStore(project);
        _buildService = buildService ?? new PlayerBuildService();
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
        _settings.Channel = BuildChannel.R2R;
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
        _settings.Channel = BuildChannel.R2R;
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
        SceneBuildEntry? startup = _settings.Scenes.FirstOrDefault(static scene => scene.IsStartup);
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
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        ImGui.BeginDisabled(_view.IsRunning);
        DrawSettings();
        ImGui.SeparatorText("场景");
        DrawScenes();
        ImGui.EndDisabled();
        ImGui.SeparatorText("操作");
        DrawActions();
        ImGui.SeparatorText("进度");
        DrawProgress();
        ImGui.SeparatorText("日志");
        DrawLog();
        ImGui.SeparatorText("结果");
        DrawResult();
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

        int channel = _settings.Channel == BuildChannel.Aot ? 1 : 0;
        if (ImGui.Combo("通道", ref channel, ChannelOptions, ChannelOptions.Length))
        {
            _settings.Channel = channel == 1 ? BuildChannel.Aot : BuildChannel.R2R;
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

        bool runAfterBuild = _settings.RunAfterBuild;
        if (ImGui.Checkbox("Build And Run", ref runAfterBuild))
        {
            _settings.RunAfterBuild = runAfterBuild;
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
                SceneBuildEntry scene = _settings.Scenes[i];
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
        bool valid = _settings.TryNormalize(out _validationMessage);
        BuildPreflight? preflight = _view.Preflight;
        bool aotRidSupported = _settings.Channel != BuildChannel.Aot ||
            string.Equals(_settings.Rid, BuildHostRid.Current, StringComparison.OrdinalIgnoreCase);
        if (!valid)
        {
            ImGui.TextUnformatted(_validationMessage);
        }
        else if (!aotRidSupported)
        {
            ImGui.TextUnformatted($"NativeAOT 仅支持当前宿主 RID：{BuildHostRid.Current}。");
        }
        else if (_preflightTask is { IsCompleted: false })
        {
            ImGui.TextUnformatted("正在预检构建工具...");
        }
        else
        {
            ImGui.TextUnformatted(preflight?.Diagnostic ?? "构建工具尚未完成预检。");
        }

        bool canBuild = valid && aotRidSupported && preflight?.Ok == true && !_view.IsRunning;
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

    private void StartBuild(bool runAfterBuild)
    {
        if (_view.IsRunning || !_settings.TryNormalize(out _validationMessage))
        {
            return;
        }

        _settings.RunAfterBuild = runAfterBuild || _settings.RunAfterBuild;
        Save();
        _buildCancellation?.Dispose();
        _buildCancellation = new CancellationTokenSource();
        IProgress<BuildProgressEvent> progress = new Progress<BuildProgressEvent>(_pendingEvents.Enqueue);
        _buildTask = _buildService.RunAsync(_settings.ToRequest(), progress, _buildCancellation.Token);
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
            _log.Add(new BuildProgressEvent(
                BuildEventKind.Log,
                BuildPhase.Unknown,
                _view.Percent,
                preflight.Ok ? BuildLogLevel.Info : BuildLogLevel.Error,
                preflight.Diagnostic,
                DateTimeOffset.UtcNow));
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
            if (result.Ok && _settings.RunAfterBuild)
            {
                LaunchBuildResult(result);
            }
        }
    }

    private void DrainEvents()
    {
        while (_pendingEvents.TryDequeue(out BuildProgressEvent? item))
        {
            if (item is null)
            {
                continue;
            }

            _log.Add(item);
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
            _log.Add(new BuildProgressEvent(
                BuildEventKind.Log,
                BuildPhase.Done,
                1,
                BuildLogLevel.Warning,
                "构建结果未提供 LauncherExe，无法 Build And Run。",
                DateTimeOffset.UtcNow));
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
        };
        try
        {
            _ = Process.Start(startInfo);
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            _log.Add(new BuildProgressEvent(
                BuildEventKind.Log,
                BuildPhase.Done,
                1,
                BuildLogLevel.Error,
                $"启动玩家包失败：{ex.Message}",
                DateTimeOffset.UtcNow));
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

    private static void OpenPath(string path)
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
        catch (Exception) when (!OperatingSystem.IsBrowser())
        {
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
