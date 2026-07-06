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
    private readonly char[] _frameGraphText = new char[GraphSampleCapacity];
    private PlayerHealth? _health;
    private PlayableProjectileTool? _projectile;
    private WeaponController? _weapons;
    private MissionDirector? _mission;
    private int _frameGraphIndex;
    private int _frameGraphCount;

    /// <summary>
    /// HUD 左上角 X 坐标，单位像素。
    /// </summary>
    public float X { get; set; } = 14f;

    /// <summary>
    /// HUD 左上角 Y 坐标，单位像素。
    /// </summary>
    public float Y { get; set; } = 14f;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveComponents();
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        ResolveComponents();
        gui.SetNextWindow(X, Y, 380f, 248f, GuiCondition.FirstUseEver);
        GuiWindowFlags flags = GuiWindowFlags.NoResize |
            GuiWindowFlags.NoMove |
            GuiWindowFlags.NoSavedSettings |
            GuiWindowFlags.NoTitleBar |
            GuiWindowFlags.NoScrollbar;
        if (!gui.BeginWindow("playable-hud", "Playable HUD", flags))
        {
            gui.EndWindow();
            return;
        }

        DrawHealth(gui);
        DrawMission(gui);
        DrawWeapon(gui);
        gui.Text($"射击 {_projectile?.ShotsFired ?? 0}");
        EngineDiagnosticsSnapshot diagnostics = Context.Diagnostics.Capture();
        gui.Text(
            $"FX {TransientParticleBurst.ActiveCount(Context.Scene)}   " +
            $"Lights {diagnostics.PointLights}   World particles {diagnostics.FreeParticles}");
        PushFrameGraphSample(diagnostics);
        gui.Separator();
        gui.Text($"FPS {diagnostics.FramesPerSecond:0.0}   {diagnostics.FrameMilliseconds:0.0} ms   p99 {diagnostics.FrameP99Milliseconds:0.0}");
        gui.Text($"1% {diagnostics.FrameLow1PercentFps:0.0}   jitter {diagnostics.FrameJitterMilliseconds:0.0}   {BuildFrameGraphText()}");
        gui.Text($"Sim {diagnostics.SimHz:0}Hz   Frame {diagnostics.FrameCount}   Bodies {diagnostics.RigidBodies}");
        if (_projectile is not null)
        {
            gui.Text(
                $"Collapse {_projectile.CollapsedFloatingIslands}   {_projectile.CollapseStatus}   " +
                $"scan {_projectile.LastCollapseSolidCandidates}");
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

    private string BuildFrameGraphText()
    {
        if (_frameGraphCount == 0)
        {
            return "";
        }

        for (int i = 0; i < GraphSampleCapacity; i++)
        {
            _frameGraphText[i] = ' ';
        }

        int start = (_frameGraphIndex - _frameGraphCount + GraphSampleCapacity) % GraphSampleCapacity;
        for (int i = 0; i < _frameGraphCount; i++)
        {
            float ms = _frameGraphSamples[(start + i) % GraphSampleCapacity];
            _frameGraphText[i] = ms switch
            {
                < 12f => '.',
                < 17f => '-',
                < 24f => '=',
                < 34f => '+',
                _ => '#',
            };
        }

        return new string(_frameGraphText, 0, _frameGraphCount);
    }

    private void ResolveComponents()
    {
        _health = Entity.TryGetComponent(out PlayerHealth health) ? health : null;
        _projectile = Entity.TryGetComponent(out PlayableProjectileTool projectile) ? projectile : null;
        _weapons = Entity.TryGetComponent(out WeaponController weapons) ? weapons : null;
        if (Entity.TryGetComponent(out MissionDirector mission))
        {
            _mission = mission;
            return;
        }

        if (_mission is not null)
        {
            return;
        }

        ScriptEntityInspection[] entities = Context.Scene.CaptureInspectionSnapshot();
        for (int i = 0; i < entities.Length; i++)
        {
            ScriptComponentInspection[] components = entities[i].Components;
            for (int j = 0; j < components.Length; j++)
            {
                if (components[j].Behaviour is MissionDirector sceneMission)
                {
                    _mission = sceneMission;
                    return;
                }
            }
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
        gui.Text($"生命 {_health.Health:0}/{max:0}");
        gui.ProgressBar(value, $"{value:P0}");
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
        gui.TextColored($"{weapon.DisplayName}  {_weapons.CurrentAmmo}/{Math.Max(0, weapon.AmmoMax)}", color);
        float cooldown = weapon.CooldownSeconds <= 0f ? 0f : Math.Clamp(_weapons.CooldownRemaining / weapon.CooldownSeconds, 0f, 1f);
        gui.ProgressBar(1f - cooldown, "冷却");
        gui.ProgressBar(Math.Clamp(_weapons.Heat / 100f, 0f, 1f), _weapons.IsOverheated ? "过热" : "热量");
    }

    private void DrawMission(IGuiContext gui)
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
        gui.TextColored(
            $"目标 水晶 {_mission.CrystalsCollected}/{Math.Max(1, _mission.RequiredCrystals)}  " +
            $"时间 {_mission.RemainingSeconds:0}s  水位 {_mission.LavaSurfaceY:0}  分数 {_mission.Score}",
            stateColor);
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

            gui.ColorSwatch("legend-" + info.Name, info.BaseColorBgra, 12f);
            gui.SameLine();
            string name = string.IsNullOrWhiteSpace(info.DisplayName) ? info.Name : info.DisplayName;
            gui.Text($"{name} / {info.LegendCategory}");
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
