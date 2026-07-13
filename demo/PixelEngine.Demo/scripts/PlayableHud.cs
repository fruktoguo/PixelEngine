using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩 Demo 的精简 HUD。
/// </summary>
public sealed class PlayableHud : Behaviour
{
    private const int GraphSampleCapacity = 48;
    private static readonly string[] LegendMaterialNames =
    [
        "sand",
        "water",
        "oil",
        "acid",
        "lava",
        "stone",
        "wood",
        "ice",
        "metal",
        "gravel",
        "crystal",
    ];

    private readonly float[] _frameGraphSamples = new float[GraphSampleCapacity];
    private readonly GuiTextBuffer _text = new(512);
    private PlayerHealth? _health;
    private PlayableProjectileTool? _projectile;
    private WeaponController? _weapons;
    private MissionDirector? _mission;
    private GoalTrigger? _goal;
    private int _frameGraphIndex;
    private int _frameGraphCount;

    /// <summary>
    /// HUD 左上角 X 坐标，单位像素。
    /// </summary>
    public float X { get; set; } = 340f;

    /// <summary>
    /// HUD 左上角 Y 坐标，单位像素。
    /// </summary>
    public float Y { get; set; } = 12f;

    /// <summary>
    /// HUD 宽度，默认收敛在 640×360 Game View 的右侧，不遮挡左侧出生区。
    /// </summary>
    public float Width { get; set; } = 288f;

    /// <summary>
    /// 是否展开性能、坍塌扫描与材质图例等诊断信息；默认只显示游玩必需信息。
    /// </summary>
    public bool ShowDiagnostics { get; set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveComponents();
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        if (!LegacyGuiFallback.IsRequired(Context.GameUi))
        {
            return;
        }

        ResolveComponents();
        float height = ShowDiagnostics ? 304f : 176f;
        gui.SetNextWindow(X, Y, Width, height, GuiCondition.FirstUseEver);
        GuiWindowFlags flags = GuiWindowFlags.NoResize |
            GuiWindowFlags.NoMove |
            GuiWindowFlags.NoSavedSettings |
            GuiWindowFlags.NoTitleBar |
            GuiWindowFlags.NoScrollbar |
            GuiWindowFlags.NoInputs;
        if (!gui.BeginWindow("playable-hud", "Playable HUD", flags))
        {
            gui.EndWindow();
            return;
        }

        DrawHealth(gui);
        DrawGoal(gui);
        DrawWeapon(gui);
        _ = _text.Clear()
            .Append("射击 ")
            .Append(_weapons?.PrimaryFireCount ?? _projectile?.ShotsFired ?? 0);
        gui.Text(_text.WrittenSpan);
        if (!ShowDiagnostics)
        {
            gui.EndWindow();
            return;
        }

        DrawMissionDiagnostics(gui);
        EngineDiagnosticsSnapshot diagnostics = Context.Diagnostics.Capture();
        _ = _text.Clear()
            .Append("FX ")
            .Append(TransientParticleBurst.ActiveCount(Context.Scene))
            .Append("   Lights ")
            .Append(diagnostics.PointLights)
            .Append("   World particles ")
            .Append(diagnostics.FreeParticles);
        gui.Text(_text.WrittenSpan);
        PushFrameGraphSample(diagnostics);
        gui.Separator();
        _ = _text.Clear()
            .Append("FPS ")
            .Append(diagnostics.FramesPerSecond, "0.0")
            .Append("   ")
            .Append(diagnostics.FrameMilliseconds, "0.0")
            .Append(" ms   p99 ")
            .Append(diagnostics.FrameP99Milliseconds, "0.0");
        gui.Text(_text.WrittenSpan);
        _ = _text.Clear()
            .Append("1% ")
            .Append(diagnostics.FrameLow1PercentFps, "0.0")
            .Append("   jitter ")
            .Append(diagnostics.FrameJitterMilliseconds, "0.0")
            .Append("   ");
        AppendFrameGraphText();
        gui.Text(_text.WrittenSpan);
        _ = _text.Clear()
            .Append("Sim ")
            .Append(diagnostics.SimHz, "0")
            .Append("Hz   Frame ")
            .Append(diagnostics.FrameCount)
            .Append("   Bodies ")
            .Append(diagnostics.RigidBodies);
        gui.Text(_text.WrittenSpan);
        if (_projectile is not null)
        {
            _ = _text.Clear()
                .Append("Collapse ")
                .Append(_projectile.CollapsedFloatingIslands)
                .Append("   ")
                .Append(_projectile.CollapseStatus)
                .Append("   scan ")
                .Append(_projectile.LastCollapseSolidCandidates);
            gui.Text(_text.WrittenSpan);
        }

        DrawMaterialLegend(gui);
        gui.EndWindow();
    }

    private void PushFrameGraphSample(EngineDiagnosticsSnapshot diagnostics)
    {
        float frameMs = diagnostics.FrameLastMilliseconds > 0.001f
            ? diagnostics.FrameLastMilliseconds
            : diagnostics.FrameMilliseconds;
        if (!float.IsFinite(frameMs) || frameMs <= 0)
        {
            return;
        }

        _frameGraphSamples[_frameGraphIndex] = MathF.Min(frameMs, 99f);
        _frameGraphIndex = (_frameGraphIndex + 1) % GraphSampleCapacity;
        if (_frameGraphCount < GraphSampleCapacity)
        {
            _frameGraphCount++;
        }
    }

    private void AppendFrameGraphText()
    {
        if (_frameGraphCount == 0)
        {
            return;
        }

        int start = (_frameGraphIndex - _frameGraphCount + GraphSampleCapacity) % GraphSampleCapacity;
        for (int i = 0; i < _frameGraphCount; i++)
        {
            float ms = _frameGraphSamples[(start + i) % GraphSampleCapacity];
            _ = _text.Append(ms switch
            {
                < 12f => '.',
                < 17f => '-',
                < 24f => '=',
                < 34f => '+',
                _ => '#',
            });
        }
    }

    private void ResolveComponents()
    {
        _health = Entity.TryGetComponent(out PlayerHealth health) ? health : null;
        _projectile = Entity.TryGetComponent(out PlayableProjectileTool projectile) ? projectile : null;
        _weapons = Entity.TryGetComponent(out WeaponController weapons) ? weapons : null;
        _goal = Entity.TryGetComponent(out GoalTrigger localGoal) ? localGoal : _goal;
        if (_mission is null && Entity.TryGetComponent(out MissionDirector mission))
        {
            _mission = mission;
        }

        if (_mission is null && Context.Scene.TryGetFirstComponent(out MissionDirector? sceneMission))
        {
            _mission = sceneMission;
        }

        if (_goal is null && Context.Scene.TryGetFirstComponent(out GoalTrigger? sceneGoal))
        {
            _goal = sceneGoal;
        }
    }

    private void DrawHealth(IGuiContext gui)
    {
        if (_health is null)
        {
            gui.Text("生命 --");
            return;
        }

        float max = MathF.Max(1f, _health.MaxHealth);
        float value = Math.Clamp(_health.Health / max, 0f, 1f);
        _ = _text.Clear()
            .Append("生命 ")
            .Append(_health.Health, "0")
            .Append('/')
            .Append(max, "0");
        gui.Text(_text.WrittenSpan);
        _ = _text.Clear().Append(value, "P0");
        gui.ProgressBar(value, _text.WrittenSpan);
    }

    private void DrawWeapon(IGuiContext gui)
    {
        if (_weapons?.Catalog is not { Weapons.Length: > 0 } catalog)
        {
            gui.Text("武器 --");
            return;
        }

        WeaponDefinition weapon = catalog.Weapons[_weapons.SelectedIndex];
        uint color = ParseBgraColor(weapon.HudColor, fallback: 0xFF_E8_D0_6A);
        gui.ColorSwatch("weapon-current", color, 14f);
        gui.SameLine();
        _ = _text.Clear()
            .Append(weapon.DisplayName)
            .Append("  ")
            .Append(_weapons.CurrentAmmo)
            .Append('/')
            .Append(Math.Max(0, weapon.AmmoMax));
        gui.TextColored(_text.WrittenSpan, color);
        float cooldown = weapon.CooldownSeconds <= 0f ? 0f : Math.Clamp(_weapons.CooldownRemaining / weapon.CooldownSeconds, 0f, 1f);
        gui.ProgressBar(1f - cooldown, "冷却");
        gui.ProgressBar(Math.Clamp(_weapons.Heat / 100f, 0f, 1f), _weapons.IsOverheated ? "过热" : "热量");
    }

    private void DrawMissionDiagnostics(IGuiContext gui)
    {
        if (_mission is null)
        {
            return;
        }

        uint stateColor = _mission.State switch
        {
            MissionState.Won => 0xFF_80_F0_80,
            MissionState.Lost => 0xFF_60_60_F0,
            MissionState.Playing => 0xFF_E8_D0_6A,
            _ => 0xFF_E8_D0_6A,
        };
        _ = _text.Clear()
            .Append("旧任务诊断 采集 ")
            .Append(_mission.CrystalsCollected)
            .Append('/')
            .Append(Math.Max(1, _mission.RequiredCrystals))
            .Append("  时间 ")
            .Append(_mission.RemainingSeconds, "0")
            .Append("s  危险线 ")
            .Append(_mission.LavaSurfaceY, "0")
            .Append("  分数 ")
            .Append(_mission.Score);
        gui.TextColored(_text.WrittenSpan, stateColor);
    }

    private void DrawGoal(IGuiContext gui)
    {
        uint color = _goal?.Reached == true ? 0xFF_80_F0_80 : 0xFF_E8_D0_6A;
        gui.TextColored(
            _goal?.Reached == true
                ? "目标 已抵达右侧出口"
                : "目标 → 右侧出口",
            color);
    }

    private void DrawMaterialLegend(IGuiContext gui)
    {
        gui.Separator();
        gui.Text("材质");
        int shown = 0;
        for (int i = 0; i < LegendMaterialNames.Length; i++)
        {
            MaterialId id = Context.Materials.Resolve(LegendMaterialNames[i]);
            if (id == MaterialId.Invalid)
            {
                continue;
            }

            MaterialInfo info = Context.Materials.GetInfo(id);
            if (!info.LegendVisible)
            {
                continue;
            }

            _ = _text.Clear().Append("legend-").Append(info.Name);
            gui.ColorSwatch(_text.WrittenSpan, info.BaseColorBgra, 12f);
            gui.SameLine();
            string name = string.IsNullOrWhiteSpace(info.DisplayName) ? info.Name : info.DisplayName;
            _ = _text.Clear()
                .Append(name)
                .Append(" / ")
                .Append(info.LegendCategory);
            gui.Text(_text.WrittenSpan);
            shown++;
            if (shown >= 4)
            {
                return;
            }
        }
    }

    private static uint ParseBgraColor(string value, uint fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        return span.Length == 9 &&
            span[0] == '#' &&
            uint.TryParse(span[1..], System.Globalization.NumberStyles.HexNumber, null, out uint parsed)
            ? parsed
            : fallback;
    }
}
