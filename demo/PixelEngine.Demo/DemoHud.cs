using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 游戏内 HUD，经脚本公开 GUI API 绘制玩家状态、笔刷与目标进度。
/// </summary>
public sealed class DemoHud : Behaviour
{
    private PlayerHealth? _health;
    private MaterialBrush? _brush;
    private ExplosiveTool? _explosive;
    private GoalTrigger? _goal;

    /// <summary>
    /// HUD 左上角 X 坐标，单位像素。
    /// </summary>
    public float X { get; set; } = 16f;

    /// <summary>
    /// HUD 左上角 Y 坐标，单位像素。
    /// </summary>
    public float Y { get; set; } = 16f;

    /// <summary>
    /// HUD 宽度，单位像素。
    /// </summary>
    public float Width { get; set; } = 320f;

    /// <summary>
    /// HUD 高度，单位像素。
    /// </summary>
    public float Height { get; set; } = 134f;

    /// <summary>
    /// 最近一次阻塞原因；为空表示 HUD 已绑定到玩家组件。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveComponents();
    }

    /// <inheritdoc />
    protected override void OnGui(IGuiContext gui)
    {
        ResolveComponents();
        gui.SetNextWindow(X, Y, Width, Height, GuiCondition.FirstUseEver);
        GuiWindowFlags flags = GuiWindowFlags.NoResize |
            GuiWindowFlags.NoMove |
            GuiWindowFlags.NoSavedSettings |
            GuiWindowFlags.NoTitleBar;
        if (!gui.BeginWindow("demo-hud", "Demo HUD", flags))
        {
            gui.EndWindow();
            return;
        }

        DrawHealth(gui);
        DrawBrush(gui);
        DrawGoalAndExplosion(gui);
        DrawTiming(gui);
        if (!string.IsNullOrEmpty(BlockedReason))
        {
            gui.Separator();
            gui.TextColored(BlockedReason, 0xFF_40_80_FF);
        }

        gui.EndWindow();
    }

    private void ResolveComponents()
    {
        _health = Entity.TryGetComponent<PlayerHealth>(out PlayerHealth health) ? health : null;
        _brush = Entity.TryGetComponent<MaterialBrush>(out MaterialBrush brush) ? brush : null;
        _explosive = Entity.TryGetComponent<ExplosiveTool>(out ExplosiveTool explosive) ? explosive : null;
        _goal = Entity.TryGetComponent<GoalTrigger>(out GoalTrigger goal) ? goal : null;
        BlockedReason = _health is null || _brush is null || _explosive is null || _goal is null
            ? "HUD 等待玩家脚本组件。"
            : string.Empty;
    }

    private void DrawHealth(IGuiContext gui)
    {
        if (_health is null)
        {
            gui.Text("生命: --");
            return;
        }

        float max = MathF.Max(1f, _health.MaxHealth);
        float value = Math.Clamp(_health.Health / max, 0f, 1f);
        gui.Text($"生命 {_health.Health:0}/{max:0}");
        gui.ProgressBar(value, $"{value:P0}");
    }

    private void DrawBrush(IGuiContext gui)
    {
        if (_brush is null)
        {
            gui.Text("材质: --");
            return;
        }

        uint color = ColorForMaterial(_brush.SelectedMaterialName);
        gui.ColorSwatch("selected-material", color, 14f);
        gui.SameLine();
        gui.Text($"材质 {_brush.SelectedMaterialName}    半径 {_brush.Radius}");
    }

    private void DrawGoalAndExplosion(IGuiContext gui)
    {
        string goal = _goal?.Reached == true ? "目标: 已抵达" : "目标: 未抵达";
        uint goalColor = _goal?.Reached == true ? 0xFF_80_F0_80 : 0xFF_E0_E0_E0;
        gui.TextColored(goal, goalColor);
        gui.SameLine();
        gui.Text($"爆破 {_explosive?.ExplosionCount ?? 0}");
    }

    private void DrawTiming(IGuiContext gui)
    {
        float fps = Context.Time.DeltaTime > 0f ? 1f / Context.Time.DeltaTime : 0f;
        float simHz = Context.Time.FixedStep > 0f ? 1f / Context.Time.FixedStep : 0f;
        gui.Separator();
        gui.Text($"FPS {fps:0}   Sim {simHz:0}Hz   Frame {Context.Time.FrameCount}");
    }

    private static uint ColorForMaterial(string name)
    {
        return name switch
        {
            "sand" => 0xFF_5F_C8_D8,
            "water" => 0xFF_D8_72_38,
            "oil" => 0xFF_38_32_26,
            "lava" => 0xFF_20_5C_FF,
            "fire" => 0xFF_20_B8_FF,
            "stone" => 0xFF_80_80_80,
            "wood" => 0xFF_48_78_A8,
            "acid" => 0xFF_30_D8_70,
            "ice" => 0xFF_F8_D8_A8,
            "metal" => 0xFF_B8_B8_C0,
            _ => 0xFF_F0_F0_F0,
        };
    }
}
