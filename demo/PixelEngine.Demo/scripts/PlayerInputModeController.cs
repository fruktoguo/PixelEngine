using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 玩家玩法输入的唯一所有者，在战斗与 Sandbox 材质笔刷之间原子切换全部相关组件。
/// </summary>
public sealed class PlayerInputModeController : Behaviour
{
    private MaterialBrush? _brush;
    private WeaponController? _weapons;
    private PlayableProjectileTool? _projectile;
    private ExplosiveTool? _explosive;
    private CampaignRunDirector? _runDirector;
    private PlayerInputMode _appliedMode = (PlayerInputMode)byte.MaxValue;
    private bool _appliedBrushAvailability;

    /// <summary>当前权威玩法输入模式。</summary>
    public PlayerInputMode Mode { get; private set; } = PlayerInputMode.Combat;

    /// <summary>是否允许进入材质笔刷模式。</summary>
    public bool AllowMaterialBrush { get; set; } = true;

    /// <summary>是否只在 InfiniteSandbox 模式允许材质笔刷。</summary>
    public bool RestrictBrushToSandbox { get; set; }

    /// <summary>战斗/笔刷模式切换键。</summary>
    public Key ToggleKey { get; set; } = Key.B;

    /// <summary>实际应用输入所有权的次数，供诊断和测试使用。</summary>
    public int ApplyCount { get; private set; }

    /// <summary>当前是否可以进入材质笔刷模式。</summary>
    public bool IsMaterialBrushAvailable => ResolveBrushAvailability();

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveComponents();
        ApplyOwnership(force: true);
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        ResolveComponents();
        bool brushAvailable = ResolveBrushAvailability();
        if (!brushAvailable && Mode == PlayerInputMode.MaterialBrush)
        {
            Mode = PlayerInputMode.Combat;
        }
        else if (brushAvailable && Context.Input.WasPressed(ToggleKey))
        {
            Mode = Mode == PlayerInputMode.Combat
                ? PlayerInputMode.MaterialBrush
                : PlayerInputMode.Combat;
        }

        ApplyOwnership(force: false);
    }

    /// <summary>
    /// 尝试由 UI 或测试选择玩法输入模式。
    /// </summary>
    /// <param name="mode">目标模式。</param>
    /// <returns>模式合法且可用时返回 true。</returns>
    public bool TrySelectMode(PlayerInputMode mode)
    {
        if (!Enum.IsDefined(mode) ||
            (mode == PlayerInputMode.MaterialBrush && !ResolveBrushAvailability()))
        {
            return false;
        }

        Mode = mode;
        ApplyOwnership(force: true);
        return true;
    }

    private void ResolveComponents()
    {
        _brush ??= Entity.TryGetComponent(out MaterialBrush brush) ? brush : null;
        _weapons ??= Entity.TryGetComponent(out WeaponController weapons) ? weapons : null;
        _projectile ??= Entity.TryGetComponent(out PlayableProjectileTool projectile) ? projectile : null;
        _explosive ??= Entity.TryGetComponent(out ExplosiveTool explosive) ? explosive : null;
        _runDirector ??= Entity.TryGetComponent(out CampaignRunDirector runDirector) ? runDirector : null;
    }

    private bool ResolveBrushAvailability()
    {
        return AllowMaterialBrush &&
            _brush is not null &&
            (!RestrictBrushToSandbox || _runDirector?.Mode == DemoGameMode.InfiniteSandbox);
    }

    private void ApplyOwnership(bool force)
    {
        bool brushAvailable = ResolveBrushAvailability();
        if (!force && _appliedMode == Mode && _appliedBrushAvailability == brushAvailable)
        {
            return;
        }

        bool brushOwnsInput = brushAvailable && Mode == PlayerInputMode.MaterialBrush;
        if (_brush is { } brush)
        {
            brush.InputEnabled = brushOwnsInput;
        }

        if (_weapons is { } weapons)
        {
            weapons.InputEnabled = !brushOwnsInput;
        }

        if (_projectile is { } projectile)
        {
            projectile.InputEnabled = !brushOwnsInput && _weapons is null;
        }

        if (_explosive is { } explosive)
        {
            explosive.InputEnabled = !brushOwnsInput;
        }

        _appliedMode = Mode;
        _appliedBrushAvailability = brushAvailable;
        ApplyCount++;
    }
}

/// <summary>玩家玩法输入所有权模式。</summary>
public enum PlayerInputMode : byte
{
    /// <summary>Wand/武器、物品和爆破工具持有玩法输入。</summary>
    Combat,

    /// <summary>仅 InfiniteSandbox 材质笔刷持有数字键、滚轮和左右键。</summary>
    MaterialBrush,
}
