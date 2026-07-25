using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>由 loot marker 生成的持久交互物，支持生命、金币、法术与商店购买。</summary>
internal sealed class NoitaMarkerLoot : Behaviour
{
    private const float InteractionRadius = 18f;
    private PlayerController? _player;
    private PlayerHealth? _health;
    private PlayerInventory? _inventory;
    private WandController? _wand;
    private readonly GuiTextBuffer _promptText = new(64);
    private string _promptWindowId = "noita-loot-prompt";
    private float _elapsed;
    private bool _nearby;

    public string Function { get; private set; } = string.Empty;

    public NoitaMarkerLootKind Kind { get; private set; }

    public long WorldX { get; private set; }

    public long WorldY { get; private set; }

    public int GoldValue { get; private set; }

    public int Price { get; private set; }

    public int SpellIndex { get; private set; } = -1;

    public bool IsConsumed { get; private set; }

    public static bool Supports(string function)
    {
        return function == "spawn_hp" ||
            function.Contains("shopitem", StringComparison.Ordinal) ||
            function == "spawn_specialshop" ||
            function is "spawn_chest" or "spawn_treasure" or "spawn_prize" or "spawn_book" or
                "spawn_bottle" or "spawn_brimstone" or "spawn_egg" or "spawn_fruit" or "spawn_trapwand" or
                "spawn_reward_wands";
    }

    public void Bind(in NoitaWangMarkerAnchor anchor, ulong worldSeed)
    {
        Function = anchor.Function;
        WorldX = anchor.WorldX;
        WorldY = anchor.WorldY;
        Kind = ResolveKind(anchor.Function);
        ulong hash = StableHash(anchor.WorldX, anchor.WorldY, worldSeed);
        GoldValue = Kind == NoitaMarkerLootKind.Treasure ? 20 + (int)(hash % 31UL) : 0;
        Price = Kind == NoitaMarkerLootKind.ShopSpell
            ? anchor.Function.Contains("cheap", StringComparison.Ordinal) ? 20 :
                anchor.Function.Contains("special", StringComparison.Ordinal) ? 100 : 50
            : 0;
        SpellIndex = Kind is NoitaMarkerLootKind.Spell or NoitaMarkerLootKind.ShopSpell
            ? (int)((hash >> 16) % 491UL)
            : -1;
        IsConsumed = false;
        _promptWindowId = $"noita-loot-prompt-{anchor.WorldX}-{anchor.WorldY}-{anchor.Function}";
        Enabled = true;
    }

    public bool TryInteract(PlayerInventory inventory, PlayerHealth health, int availableSpellCount)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(health);
        if (IsConsumed)
        {
            return false;
        }

        bool success = Kind switch
        {
            NoitaMarkerLootKind.Health => health.Heal(50f) > 0f,
            NoitaMarkerLootKind.Treasure => GrantTreasure(inventory),
            NoitaMarkerLootKind.Spell => GrantSpell(inventory, availableSpellCount),
            NoitaMarkerLootKind.ShopSpell => PurchaseSpell(inventory, availableSpellCount),
            _ => false,
        };
        if (success)
        {
            IsConsumed = true;
            Enabled = false;
        }

        return success;
    }

    protected override void OnUpdate(float dt)
    {
        if (IsConsumed || !float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        ResolvePlayerComponents();
        _elapsed += dt;
        _nearby = _player is not null &&
            DistanceSquared(_player.CenterX, _player.CenterY, WorldX, WorldY) <= InteractionRadius * InteractionRadius;
        if (_nearby && Context.Input.WasPressed(Key.E) && _inventory is not null && _health is not null)
        {
            int spellCount = _wand?.Catalog?.Spells.Length ?? 0;
            if (TryInteract(_inventory, _health, spellCount))
            {
                Context.Audio.PlayAt("pickup.wav", WorldX, WorldY, 0.75f);
                TransientParticleBurst.Emit(Context, WorldX, WorldY, 12, 34f, 72, 0xFF_50_E8_FFu, 0xCC_80_70_FFu, 0.65f);
                return;
            }
        }

        Draw();
    }

    protected override void OnGui(IGuiContext gui)
    {
        if (!_nearby || IsConsumed)
        {
            return;
        }

        Point2F center = Context.Camera.WorldToScreen(WorldX, WorldY);
        float width = Kind == NoitaMarkerLootKind.ShopSpell ? 190f : 170f;
        gui.SetNextWindow(
            Math.Clamp(center.X - (width * 0.5f), 8f, MathF.Max(8f, gui.Width - width - 8f)),
            Math.Clamp(center.Y - 52f, 8f, MathF.Max(8f, gui.Height - 38f)),
            width,
            34f);
        GuiWindowFlags flags = GuiWindowFlags.NoTitleBar |
            GuiWindowFlags.NoResize |
            GuiWindowFlags.NoMove |
            GuiWindowFlags.NoSavedSettings |
            GuiWindowFlags.NoScrollbar |
            GuiWindowFlags.NoInputs;
        if (!gui.BeginWindow(_promptWindowId, "交互", flags))
        {
            gui.EndWindow();
            return;
        }

        BuildInteractionText();
        gui.TextColored(_promptText.WrittenSpan, 0xFF_FF_FF_FF);
        gui.EndWindow();
    }

    private void ResolvePlayerComponents()
    {
        _ = _player is null && Context.Scene.TryGetFirstComponent(out _player);
        _ = _health is null && Context.Scene.TryGetFirstComponent(out _health);
        _ = _inventory is null && Context.Scene.TryGetFirstComponent(out _inventory);
        _ = _wand is null && Context.Scene.TryGetFirstComponent(out _wand);
    }

    private void Draw()
    {
        Point2F center = Context.Camera.WorldToScreen(WorldX, WorldY);
        float size = MathF.Max(8f, Context.Camera.Zoom * 7f);
        float pulse = 0.8f + (MathF.Sin(_elapsed * 3f) * 0.15f);
        uint color = Kind == NoitaMarkerLootKind.Health ? 0xFF_50_E0_60u : 0xFF_30_D8_FFu;
        Context.Overlay.SolidRectangle(center.X - (size * 0.5f), center.Y - (size * 0.5f), size, size, ScaleAlpha(color, pulse));
        Context.Overlay.OutlineRectangle(center.X - (size * 0.5f), center.Y - (size * 0.5f), size, size, 1.5f, 0xFF_F0_F0_F0);
        Context.Lighting.AddPointLight(WorldX, WorldY, 28f, color, 0.42f);
    }

    private void BuildInteractionText()
    {
        _ = _promptText.Clear().Append("[E] ");
        if (Kind == NoitaMarkerLootKind.Health)
        {
            _ = _promptText.Append("恢复生命");
            return;
        }

        if (Kind == NoitaMarkerLootKind.Treasure)
        {
            _ = _promptText.Append("打开宝箱  +").Append(GoldValue).Append(" 金币");
            return;
        }

        if (Kind == NoitaMarkerLootKind.Spell)
        {
            _ = _promptText.Append("拾取法术");
            return;
        }

        if (Kind != NoitaMarkerLootKind.ShopSpell)
        {
            throw new InvalidOperationException($"未知 loot kind：{Kind}。");
        }

        _ = _promptText.Append("购买法术  ").Append(Price).Append(" 金币");
    }

    private bool GrantTreasure(PlayerInventory inventory)
    {
        inventory.GrantGold(GoldValue);
        return true;
    }

    private bool GrantSpell(PlayerInventory inventory, int availableSpellCount)
    {
        return availableSpellCount > 0 && inventory.TryAddSpell(SpellIndex % availableSpellCount);
    }

    private bool PurchaseSpell(PlayerInventory inventory, int availableSpellCount)
    {
        if (availableSpellCount <= 0 || inventory.SpellCount >= PlayerInventory.SpellCapacity || !inventory.TrySpendGold(Price))
        {
            return false;
        }

        if (inventory.TryAddSpell(SpellIndex % availableSpellCount))
        {
            return true;
        }

        inventory.GrantGold(Price);
        return false;
    }

    private static NoitaMarkerLootKind ResolveKind(string function)
    {
        return function == "spawn_hp"
            ? NoitaMarkerLootKind.Health
            : function.Contains("shopitem", StringComparison.Ordinal) || function == "spawn_specialshop"
                ? NoitaMarkerLootKind.ShopSpell
                : function is "spawn_chest" or "spawn_treasure" or "spawn_prize"
                    ? NoitaMarkerLootKind.Treasure
                    : NoitaMarkerLootKind.Spell;
    }

    private static float DistanceSquared(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        return (dx * dx) + (dy * dy);
    }

    private static ulong StableHash(long x, long y, ulong seed)
    {
        ulong value = unchecked((ulong)x) * 0x9E37_79B1_85EB_CA87UL;
        value ^= unchecked((ulong)y) * 0xC2B2_AE3D_27D4_EB4FUL;
        value ^= seed;
        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        return value ^ (value >> 31);
    }

    private static uint ScaleAlpha(uint bgra, float multiplier)
    {
        byte alpha = (byte)(bgra >> 24);
        byte scaled = (byte)Math.Clamp((int)MathF.Round(alpha * multiplier), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)scaled << 24);
    }
}

internal enum NoitaMarkerLootKind : byte
{
    Health,
    Treasure,
    Spell,
    ShopSpell,
}
