using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell.Build;

internal sealed class BuildSettingsPanel : IEditorPanel
{
    public const string PanelTitle = "构建与发布";
    private static readonly string[] RidOptions = ["win-x64", "win-arm64"];
    private static readonly string[] ChannelOptions = ["R2R", "NativeAOT"];
    private static readonly string[] ConfigurationOptions = ["Release", "Debug"];
    private readonly BuildSettingsStore _store;
    private readonly BuildLog _log = new();
    private readonly BuildTargetSettings _settings;
    private string _validationMessage = string.Empty;

    public BuildSettingsPanel(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _store = new BuildSettingsStore(project);
        _settings = _store.Load();
        Validate();
    }

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    public void Draw(in EditorContext context)
    {
        _ = context;
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawSettings();
        ImGui.SeparatorText("场景");
        DrawScenes();
        ImGui.SeparatorText("操作");
        DrawActions();
        ImGui.SeparatorText("日志");
        DrawLog();
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
        ImGui.TextUnformatted(valid ? "设置有效；构建执行将在 PlayerBuildService 节点启用。" : _validationMessage);

        ImGui.BeginDisabled();
        _ = ImGui.Button("Build");
        ImGui.SameLine();
        _ = ImGui.Button("Build And Run");
        ImGui.SameLine();
        _ = ImGui.Button("取消");
        ImGui.EndDisabled();
    }

    private void DrawLog()
    {
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
                ImGui.TextUnformatted($"[{item.Level}] {item.Message}");
            }
        }

        ImGui.EndChild();
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
