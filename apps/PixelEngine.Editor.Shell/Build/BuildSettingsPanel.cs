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
    private const string ActionsOverflowPopupName = "build-settings-actions-overflow";
    private static readonly string[] RidOptions = ["win-x64", "win-arm64"];
    private static readonly string[] ChannelOptions = ["R2R", "NativeAOT"];
    private static readonly string[] ConfigurationOptions = ["Release", "Debug"];
    private readonly EditorProject _project;
    private readonly BuildSettingsStore _store;
    private readonly IPlayerBuildService _buildService;
    private readonly IEditorConsoleSink? _console;
    private readonly Func<BuildScenePreparationResult> _prepareScene;
    private readonly ConcurrentQueue<BuildProgressEvent> _pendingEvents = new();
    private readonly BuildLog _log = new();
    private readonly BuildProfileDto _settings;
    private BuildRunView _view = new();
    private CancellationTokenSource? _buildCancellation;
    private Task<BuildPreflight>? _preflightTask;
    private Task<BuildResult>? _buildTask;
    private string _validationMessage = string.Empty;
    private string _persistentSettingsDiagnostic = string.Empty;
    private bool _autoScroll = true;
    private bool _scriptedOpenActionsOverflow;
    private ScriptedBuildSettingsFooterProbeSnapshot _lastFooterProbe = new();

    public BuildSettingsPanel(
        EditorProject project,
        IPlayerBuildService? buildService = null,
        IEditorConsoleSink? console = null,
        Func<BuildScenePreparationResult>? prepareScene = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _project = project;
        _store = new BuildSettingsStore(project);
        _buildService = buildService ?? new PlayerBuildService();
        _console = console;
        _prepareScene = prepareScene ?? (static () => new BuildScenePreparationResult(true, string.Empty));
        _settings = _store.LoadRecoverable(out _persistentSettingsDiagnostic);
        RequiresRepair = !string.IsNullOrWhiteSpace(_persistentSettingsDiagnostic);
        if (RequiresRepair)
        {
            _console?.AddProjectError("build-settings", _persistentSettingsDiagnostic);
        }

        Validate();
        StartPreflight();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    internal bool RequiresRepair { get; private set; }

    internal string SettingsDiagnostic => _persistentSettingsDiagnostic;

    internal bool TryRepairSettings(out string diagnostic)
    {
        bool saved = Save();
        diagnostic = _validationMessage;
        return saved;
    }

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

        if (!StartBuild(runAfterBuild))
        {
            diagnostic = _validationMessage;
            return false;
        }

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

        if (!StartBuild(runAfterBuild))
        {
            diagnostic = _validationMessage;
            return false;
        }

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

        _ = _settings.Normalize();
        return Save()
            ? CaptureScriptedBuildSettingsProbe()
            : throw new InvalidOperationException(_validationMessage);
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

    /// <summary>
    /// 读取最近一帧实际绘制的 Build Settings footer 响应式布局。
    /// </summary>
    public ScriptedBuildSettingsFooterProbeSnapshot CaptureScriptedBuildSettingsFooterProbe()
    {
        return _lastFooterProbe;
    }

    /// <summary>
    /// 让下一次绘制打开窄 footer 的动作菜单，供 commit-bound framebuffer 证明菜单内容可达。
    /// </summary>
    public bool RequestScriptedBuildSettingsActionsOverflow()
    {
        if (!_lastFooterProbe.OverflowVisible || !_lastFooterProbe.ActionsAccessible)
        {
            return false;
        }

        _scriptedOpenActionsOverflow = true;
        return true;
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
        if (RequiresRepair)
        {
            ImGui.SeparatorText("设置恢复");
            ImGui.TextWrapped(_persistentSettingsDiagnostic);
            if (ImGui.Button("保存当前回退设置并修复"))
            {
                _ = TryRepairSettings(out _);
            }
        }

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
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!ImGui.BeginTable(
            "build_settings_fields",
            2,
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TableSetupColumn(
            "Property",
            ImGuiTableColumnFlags.WidthFixed,
            ResolveSettingsLabelWidth(availableWidth));
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        bool changed = false;
        int rid = IndexOf(RidOptions, _settings.Rid);
        NextSetting("目标平台");
        if (ImGui.Combo("##build-target-rid", ref rid, RidOptions, RidOptions.Length) && rid >= 0)
        {
            _settings.Rid = RidOptions[rid];
            changed = true;
        }

        int channel = _settings.Channel == BuildProfileChannel.Aot ? 1 : 0;
        NextSetting("通道");
        if (ImGui.Combo("##build-channel", ref channel, ChannelOptions, ChannelOptions.Length))
        {
            _settings.Channel = channel == 1 ? BuildProfileChannel.Aot : BuildProfileChannel.R2R;
            changed = true;
        }

        int configuration = IndexOf(ConfigurationOptions, _settings.Configuration);
        NextSetting("配置");
        if (ImGui.Combo("##build-configuration", ref configuration, ConfigurationOptions, ConfigurationOptions.Length) && configuration >= 0)
        {
            _settings.Configuration = ConfigurationOptions[configuration];
            changed = true;
        }

        NextSetting("输出目录");
        changed |= InputTextValue("##build-output-directory", _settings.OutputDirectory, value => _settings.OutputDirectory = value, 512);
        NextSetting("产物名");
        changed |= InputTextValue("##build-product-name", _settings.ProductName, value => _settings.ProductName = value, 128);
        NextSetting("版本");
        changed |= InputTextValue("##build-version", _settings.Version, value => _settings.Version = value, 64);
        NextSetting("信息版本");
        changed |= InputTextValue("##build-informational-version", _settings.InformationalVersion, value => _settings.InformationalVersion = value, 128);
        string icon = _settings.IconPath ?? string.Empty;
        NextSetting("图标 .ico");
        if (InputTextValue("##build-icon", icon, value => _settings.IconPath = string.IsNullOrWhiteSpace(value) ? null : value, 512))
        {
            changed = true;
        }

        bool includeSymbols = _settings.IncludeSymbols;
        NextSetting("调试符号");
        if (ImGui.Checkbox("##build-include-symbols", ref includeSymbols))
        {
            _settings.IncludeSymbols = includeSymbols;
            changed = true;
        }

        bool wholeContent = _settings.PackageWholeContent;
        NextSetting("完整 content");
        if (ImGui.Checkbox("##build-package-whole-content", ref wholeContent))
        {
            _settings.PackageWholeContent = wholeContent;
            changed = true;
        }

        ImGui.EndTable();
        if (changed)
        {
            _ = Save();
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
            _ = Save();
        }
    }

    private void DrawActions()
    {
        bool canBuild = CanStartBuild(out _validationMessage);
        ImGui.TextUnformatted(_validationMessage);
        float availableWidth = ImGui.GetContentRegionAvail().X;
        ImGuiStylePtr style = ImGui.GetStyle();
        float horizontalPadding = style.FramePadding.X * 2f;
        float buildWidth = ImGui.CalcTextSize("Build").X + horizontalPadding;
        float buildAndRunWidth = ImGui.CalcTextSize("Build And Run").X + horizontalPadding;
        float cancelWidth = ImGui.CalcTextSize("取消").X + horizontalPadding;
        float preflightWidth = ImGui.CalcTextSize("重新预检").X + horizontalPadding;
        float overflowWidth = MathF.Max(
            ImGui.GetFrameHeight(),
            ImGui.CalcTextSize("...").X + horizontalPadding);
        BuildSettingsFooterLayout layout = ResolveFooterLayout(
            availableWidth,
            style.ItemSpacing.X,
            buildWidth,
            buildAndRunWidth,
            cancelWidth,
            preflightWidth,
            overflowWidth);

        bool primaryActionsVisible = layout.Density != BuildSettingsFooterDensity.AllOverflow;
        if (primaryActionsVisible)
        {
            DrawPrimaryActions(canBuild);
        }

        bool overflowPopupOpen = false;
        if (layout.Density == BuildSettingsFooterDensity.Inline)
        {
            ImGui.SameLine();
            DrawInlineSecondaryActions();
        }
        else
        {
            if (primaryActionsVisible)
            {
                ImGui.SameLine();
            }

            bool popupRequested = DrawActionsOverflowButton(
                overflowWidth,
                out System.Numerics.Vector2 popupAnchor);
            if (_scriptedOpenActionsOverflow)
            {
                ImGui.OpenPopup(ActionsOverflowPopupName);
                _scriptedOpenActionsOverflow = false;
                popupRequested = true;
            }

            if (popupRequested)
            {
                // footer 位于面板底部；菜单向上展开，避免落到状态栏外或依赖鼠标位置。
                ImGui.SetNextWindowPos(
                    popupAnchor,
                    ImGuiCond.Appearing,
                    new System.Numerics.Vector2(0f, 1f));
            }

            overflowPopupOpen = DrawActionsOverflowPopup(
                includePrimaryActions: layout.Density == BuildSettingsFooterDensity.AllOverflow,
                canBuild);
        }

        _lastFooterProbe = new ScriptedBuildSettingsFooterProbeSnapshot
        {
            Density = layout.Density,
            AvailableWidth = layout.AvailableWidth,
            RequiredInlineWidth = layout.RequiredInlineWidth,
            RequiredResponsiveWidth = layout.RequiredResponsiveWidth,
            RequiredOverflowWidth = layout.RequiredOverflowWidth,
            PrimaryActionsFit = layout.PrimaryActionsFit,
            ActionsAccessible = layout.ActionsAccessible,
            BuildVisible = primaryActionsVisible,
            BuildAndRunVisible = primaryActionsVisible,
            OverflowVisible = layout.Density != BuildSettingsFooterDensity.Inline,
            OverflowPopupOpen = overflowPopupOpen,
            SecondaryActionsAccessible = layout.ActionsAccessible,
        };
    }

    private void DrawPrimaryActions(bool canBuild)
    {
        ImGui.BeginDisabled(!canBuild);
        if (ImGui.Button("Build"))
        {
            _ = StartBuild(runAfterBuild: false);
        }

        ImGui.SameLine();
        if (ImGui.Button("Build And Run"))
        {
            _ = StartBuild(runAfterBuild: true);
        }

        ImGui.EndDisabled();
    }

    private void DrawInlineSecondaryActions()
    {
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

    private static bool DrawActionsOverflowButton(
        float overflowWidth,
        out System.Numerics.Vector2 popupAnchor)
    {
        bool requested = ImGui.Button(
            "...##build-settings-actions-overflow",
            new System.Numerics.Vector2(overflowWidth, 0f));
        System.Numerics.Vector2 buttonMin = ImGui.GetItemRectMin();
        popupAnchor = new System.Numerics.Vector2(buttonMin.X, buttonMin.Y);
        if (requested)
        {
            ImGui.OpenPopup(ActionsOverflowPopupName);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("更多构建操作");
        }

        return requested;
    }

    private bool DrawActionsOverflowPopup(bool includePrimaryActions, bool canBuild)
    {
        if (!ImGui.BeginPopup(ActionsOverflowPopupName))
        {
            return false;
        }

        if (includePrimaryActions)
        {
            if (ImGui.MenuItem("Build", string.Empty, selected: false, enabled: canBuild))
            {
                _ = StartBuild(runAfterBuild: false);
            }

            if (ImGui.MenuItem("Build And Run", string.Empty, selected: false, enabled: canBuild))
            {
                _ = StartBuild(runAfterBuild: true);
            }

            ImGui.Separator();
        }

        bool cancelRequested = ImGui.MenuItem(
            "取消构建",
            string.Empty,
            selected: false,
            enabled: _view.IsRunning);
        if (cancelRequested)
        {
            _buildCancellation?.Cancel();
        }

        if (ImGui.MenuItem("重新预检"))
        {
            StartPreflight();
        }

        ImGui.EndPopup();
        return true;
    }

    /// <summary>
    /// 按字体实测按钮宽度决定 footer 是否折叠次要动作；边界值采用 inline，避免临界宽度抖动。
    /// </summary>
    internal static BuildSettingsFooterLayout ResolveFooterLayout(
        float availableWidth,
        float itemSpacing,
        float buildWidth,
        float buildAndRunWidth,
        float cancelWidth,
        float preflightWidth,
        float overflowWidth)
    {
        float available = NormalizeLayoutWidth(availableWidth);
        float spacing = NormalizeLayoutWidth(itemSpacing);
        float build = NormalizeLayoutWidth(buildWidth);
        float buildAndRun = NormalizeLayoutWidth(buildAndRunWidth);
        float cancel = NormalizeLayoutWidth(cancelWidth);
        float preflight = NormalizeLayoutWidth(preflightWidth);
        float overflow = NormalizeLayoutWidth(overflowWidth);
        float requiredInline = build + buildAndRun + cancel + preflight + (spacing * 3f);
        float requiredResponsive = build + buildAndRun + overflow + (spacing * 2f);
        BuildSettingsFooterDensity density = available >= requiredInline
            ? BuildSettingsFooterDensity.Inline
            : available >= requiredResponsive
                ? BuildSettingsFooterDensity.Overflow
                : BuildSettingsFooterDensity.AllOverflow;
        return new BuildSettingsFooterLayout(
            density,
            available,
            requiredInline,
            requiredResponsive,
            overflow);
    }

    private static float NormalizeLayoutWidth(float width)
    {
        return float.IsFinite(width) ? MathF.Max(0f, width) : 0f;
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
    private bool StartBuild(bool runAfterBuild)
    {
        if (_view.IsRunning || !_settings.TryNormalize(out _validationMessage))
        {
            return false;
        }

        BuildScenePreparationResult preparation = _prepareScene();
        if (!preparation.Succeeded)
        {
            _validationMessage = string.IsNullOrWhiteSpace(preparation.Diagnostic)
                ? "当前场景尚未准备好，构建未启动。"
                : preparation.Diagnostic;
            return false;
        }

        // Build 与 Build And Run 是逐次命令；不能把上一次的启动选择粘滞到下一次 Build。
        _settings.RunAfterBuild = runAfterBuild;
        if (!Save())
        {
            return false;
        }

        _buildCancellation?.Dispose();
        _buildCancellation = new CancellationTokenSource();
        IProgress<BuildProgressEvent> progress = new Progress<BuildProgressEvent>(_pendingEvents.Enqueue);
        PlayerSettingsDto playerSettings = new PlayerSettingsStore(_project).LoadRecoverable(
            out string playerSettingsDiagnostic);
        if (!string.IsNullOrWhiteSpace(playerSettingsDiagnostic))
        {
            _console?.AddProjectError("player-settings", playerSettingsDiagnostic);
        }

        BuildRequest request = PlayerSettingsEditorAdapter.ApplyToBuildRequest(
            _settings.ToRequest() with
            {
                ContentRoot = _project.ContentRootPath,
            },
            playerSettings);
        _buildTask = _buildService.RunAsync(request, progress, _buildCancellation.Token);
        _view = _view with
        {
            IsRunning = true,
            Phase = BuildPhase.Native,
            Percent = 0,
            StartedAt = DateTimeOffset.UtcNow,
            Result = null,
        };
        return true;
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

    private bool Save()
    {
        Validate();
        if (!_settings.TryNormalize(out _validationMessage))
        {
            return false;
        }

        try
        {
            _store.Save(_settings);
            _persistentSettingsDiagnostic = string.Empty;
            RequiresRepair = false;
            _validationMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            string diagnostic = $"保存 Build Settings 失败：{exception.Message}";
            bool shouldReport = !string.Equals(_persistentSettingsDiagnostic, diagnostic, StringComparison.Ordinal);
            _persistentSettingsDiagnostic = diagnostic;
            RequiresRepair = true;
            _validationMessage = diagnostic;
            if (shouldReport)
            {
                _console?.AddProjectError("build-settings", diagnostic);
            }

            return false;
        }
    }

    private void Validate()
    {
        bool valid = _settings.TryNormalize(out string diagnostic);
        _validationMessage = valid ? _persistentSettingsDiagnostic : diagnostic;
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

    internal static float ResolveSettingsLabelWidth(float availableWidth)
    {
        float width = float.IsFinite(availableWidth) ? MathF.Max(1f, availableWidth) : 1f;
        return Math.Clamp(width * 0.44f, 72f, 144f);
    }

    private static void NextSetting(string label)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1f);
    }

    private static bool InputTextValue(string id, string value, Action<string> assign, uint maxLength)
    {
        string editable = value;
        bool changed = ImGui.InputText(id, ref editable, maxLength);
        if (changed)
        {
            assign(editable);
        }

        return changed;
    }
}

/// <summary>Build 提交前当前 authoring scene 的校验/持久化结果。</summary>
internal readonly record struct BuildScenePreparationResult(bool Succeeded, string Diagnostic);
