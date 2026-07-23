using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 的 Noita-like Wand 运行时控制器。
/// 目录解析与施法求值由 <see cref="WandSpellCatalog" /> / <see cref="WandSpellEvaluator" /> 完成，
/// 本组件只负责输入、状态推进和把固定容量 projectile plan 交给场景安全相位。
/// </summary>
public sealed class WandController : Behaviour
{
    private const int MaxPendingProjectiles = 128;
    private const int ProjectilePoolCapacity = 256;
    private const ulong FallbackRunSeed = 0x5049_5845_4C57_414EUL;

    private PlayerController? _player;
    private CampaignRunDirector? _runDirector;
    private WandRuntimeState[] _states = [];
    private WandPassiveState[] _passives = [];
    private WandCastBuffer? _buffer;
    private bool _pendingCast;
    private int _pendingProjectileCount;
    private float _pendingOriginX;
    private float _pendingOriginY;
    private float _pendingDirectionX;
    private float _pendingDirectionY;
    private readonly SpellProjectilePlan[] _pendingPlans = new SpellProjectilePlan[MaxPendingProjectiles];
    private readonly WandProjectile?[] _spawnedProjectiles = new WandProjectile?[MaxPendingProjectiles];
    private readonly WandProjectile?[] _projectilePool = new WandProjectile?[ProjectilePoolCapacity];
    private readonly WandProjectile?[] _freeProjectiles = new WandProjectile?[ProjectilePoolCapacity];
    private int _poolCreatedCount;
    private int _freeProjectileCount;
    private bool _spawnSystemRegistered;

    /// <summary>相对于 Demo content 根目录的 Wand 目录路径。</summary>
    public string CatalogPath { get; set; } = "wand-spells.json";

    /// <summary>是否响应 Wand 输入；材质笔刷模式会由输入所有权控制器关闭它。</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>当前 Wand 索引，范围由目录决定。</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>当前 Wand 的稳定 id。</summary>
    public string SelectedWandId => CurrentWand?.Id ?? string.Empty;

    /// <summary>当前 Wand 的显示名。</summary>
    public string SelectedWandDisplayName => CurrentWand?.DisplayName ?? string.Empty;

    /// <summary>当前 Wand 的 mana。</summary>
    public float CurrentMana => CurrentState?.Mana ?? 0f;

    /// <summary>当前 Wand 的最大 mana。</summary>
    public float CurrentManaMax => CurrentWand?.ManaMax ?? 0f;

    /// <summary>当前 Wand 的 mana 比例。</summary>
    public float ManaFraction => CurrentManaMax <= 0f
        ? 0f
        : Math.Clamp(CurrentMana / CurrentManaMax, 0f, 1f);

    /// <summary>当前 Wand 的施法冷却剩余秒数。</summary>
    public float CastDelayRemaining => CurrentState?.CastDelayRemaining ?? 0f;

    /// <summary>当前 Wand 的 recharge 剩余秒数。</summary>
    public float RechargeRemaining => CurrentState?.RechargeRemaining ?? 0f;

    /// <summary>当前 Wand 的 deck 容量。</summary>
    public int CurrentCapacity => CurrentWand?.Capacity ?? 0;

    /// <summary>当前 Wand 的 deck 卡片数。</summary>
    public int CurrentDeckCount => CurrentWand?.DeckSpellIndices.Length ?? 0;

    /// <summary>当前 Wand 的每次 cast draw 数。</summary>
    public int CurrentSpellsPerCast => CurrentWand?.SpellsPerCast ?? 0;

    /// <summary>已成功求值的 cast 次数。</summary>
    public int CastCount { get; private set; }

    /// <summary>最近一次 cast 的 projectile 数量。</summary>
    public int LastProjectileCount { get; private set; }

    /// <summary>最近一次 cast 的状态文本。</summary>
    public string LastCastStatusText { get; private set; } = "idle";

    /// <summary>最近一次 cast 消耗的 mana。</summary>
    public float LastManaCost { get; private set; }

    /// <summary>最近一次求值中被跳过的 mana 卡数量。</summary>
    public int LastManaSkippedCards { get; private set; }

    /// <summary>最近一次求值中被跳过的有限次数卡数量。</summary>
    public int LastUsesSkippedCards { get; private set; }

    /// <summary>最近一次求值产生的 spell 摘要。</summary>
    public string LastSpellSummary { get; private set; } = string.Empty;

    /// <summary>当前 play session 创建过的 Wand projectile 数量。</summary>
    public int SpawnedProjectileCount { get; private set; }

    /// <summary>当前仍处于 active 状态的 Wand projectile 数量。</summary>
    public int ActiveProjectileCount { get; private set; }

    /// <summary>固定 projectile pool 中当前可立即租用的槽位数。</summary>
    public int AvailableProjectileCount => _poolCreatedCount == ProjectilePoolCapacity
        ? _freeProjectileCount
        : ProjectilePoolCapacity;

    /// <summary>因固定 projectile pool 容量不足而被原子拒绝的 cast 次数。</summary>
    public int PoolExhaustedCastCount { get; private set; }

    /// <summary>trigger payload 被激活的总次数。</summary>
    public int PayloadActivationCount { get; private set; }

    /// <summary>最近一次 cast plan 中声明 parent 的 payload 数量。</summary>
    public int LastPayloadPlanCount { get; private set; }

    /// <summary>当前 play session 成功建立的 parent/payload 链接数量。</summary>
    public int LinkedPayloadCount { get; private set; }

    /// <summary>当前是否处于允许编辑 deck 的安全状态。</summary>
    public bool CanEditWand => _runDirector is null ||
        _runDirector.Mode == DemoGameMode.InfiniteSandbox ||
        _runDirector.State is CampaignRunState.MainMenu or CampaignRunState.HolyMountain;

    /// <summary>当前加载的 Wand 目录；仅供 Demo UI/测试读取。</summary>
    internal WandSpellCatalog? Catalog { get; private set; }

    /// <summary>当前 Wand 定义；仅供同程序集运行时/UI 使用。</summary>
    internal WandDefinition? CurrentWand => Catalog is { } catalog &&
        (uint)SelectedIndex < (uint)catalog.Wands.Length
        ? catalog.Wands[SelectedIndex]
        : null;

    private WandRuntimeState? CurrentState => (uint)SelectedIndex < (uint)_states.Length
        ? _states[SelectedIndex]
        : null;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResetRuntimeState();
        ResolveComponents();
        LoadCatalog();
        RegisterSpawnSystem();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f)
        {
            return;
        }

        ResolveComponents();
        LoadCatalog();
        WandRuntimeState? state = CurrentState;
        WandDefinition? wand = CurrentWand;
        if (state is null || wand is null || Catalog is null)
        {
            return;
        }

        state.Advance(wand, in _passives[SelectedIndex], dt);
        if (!InputEnabled)
        {
            return;
        }

        HandleSelection();
        if (Context.Input.IsMouseDown(MouseButton.Left))
        {
            _ = TryCastFromCurrentInput();
        }
    }

    /// <summary>
    /// 由 UI/测试选择一个 Wand；成功时立即切换到该 Wand 的独立 deck 状态。
    /// </summary>
    /// <param name="index">目录中的 Wand 索引。</param>
    /// <returns>索引有效时返回 true。</returns>
    public bool SelectWand(int index)
    {
        if (Catalog is null || (uint)index >= (uint)Catalog.Wands.Length)
        {
            return false;
        }

        SelectedIndex = index;
        LastCastStatusText = "ready";
        LastSpellSummary = string.Empty;
        return true;
    }

    /// <summary>
    /// 把当前 Wand 的一个 deck slot 向后循环到下一个 spell；用于 inventory UI 的自由搭配。
    /// </summary>
    /// <param name="slot">deck slot 索引。</param>
    /// <returns>slot 有效且当前允许编辑时返回 true。</returns>
    public bool CycleCurrentSpellSlot(int slot)
    {
        return TryCycleSpellSlot(SelectedIndex, slot, 1);
    }

    /// <summary>
    /// 设置指定 Wand slot 的 spell；重复 spell 是合法的，次数仍按 card slot 独立计算。
    /// </summary>
    public bool TrySetSpellSlot(int wandIndex, int slot, int spellIndex)
    {
        if (!CanEditWand ||
            Catalog is null ||
            (uint)wandIndex >= (uint)Catalog.Wands.Length ||
            (uint)spellIndex >= (uint)Catalog.Spells.Length)
        {
            return false;
        }

        WandDefinition wand = Catalog.Wands[wandIndex];
        if ((uint)slot >= (uint)wand.DeckSpellIndices.Length)
        {
            return false;
        }

        wand.DeckSpellIndices[slot] = spellIndex;
        _states[wandIndex].Reset(Catalog, wand);
        return true;
    }

    /// <summary>按方向循环指定 Wand 的 slot。</summary>
    public bool TryCycleSpellSlot(int wandIndex, int slot, int direction)
    {
        if (Catalog is null ||
            (uint)wandIndex >= (uint)Catalog.Wands.Length)
        {
            return false;
        }

        WandDefinition wand = Catalog.Wands[wandIndex];
        if ((uint)slot >= (uint)wand.DeckSpellIndices.Length || Catalog.Spells.Length == 0)
        {
            return false;
        }

        int step = direction < 0 ? -1 : 1;
        int next = (wand.DeckSpellIndices[slot] + step + Catalog.Spells.Length) % Catalog.Spells.Length;
        return TrySetSpellSlot(wandIndex, slot, next);
    }

    /// <summary>读取指定 Wand slot 的显示名；无效 slot 返回空字符串。</summary>
    internal string SpellSlotDisplayName(int wandIndex, int slot)
    {
        if (Catalog is null || (uint)wandIndex >= (uint)Catalog.Wands.Length)
        {
            return string.Empty;
        }

        WandDefinition wand = Catalog.Wands[wandIndex];
        return (uint)slot < (uint)wand.DeckSpellIndices.Length
            ? Catalog.Spells[wand.DeckSpellIndices[slot]].DisplayName
            : string.Empty;
    }

    /// <summary>读取指定 Wand slot 的稳定 spell id。</summary>
    internal string SpellSlotId(int wandIndex, int slot)
    {
        if (Catalog is null || (uint)wandIndex >= (uint)Catalog.Wands.Length)
        {
            return string.Empty;
        }

        WandDefinition wand = Catalog.Wands[wandIndex];
        return (uint)slot < (uint)wand.DeckSpellIndices.Length
            ? Catalog.Spells[wand.DeckSpellIndices[slot]].Id
            : string.Empty;
    }

    /// <summary>
    /// 按当前鼠标方向执行一次 cast；供自动化输入与测试复用。
    /// </summary>
    /// <returns>求值成功并产生 projectile 时返回 true。</returns>
    public bool CastOnceFromCurrentInput()
    {
        return TryCastFromCurrentInput();
    }

    private bool TryCastFromCurrentInput()
    {
        ResolveComponents();
        WandSpellCatalog? catalog = Catalog;
        WandDefinition? wand = CurrentWand;
        WandRuntimeState? state = CurrentState;
        WandCastBuffer? buffer = _buffer;
        if (catalog is null || wand is null || state is null || buffer is null)
        {
            return false;
        }

        Point2F origin = ResolveMuzzle();
        Point2F target = Context.Camera.ScreenToWorld(Context.Input.MousePixel.X, Context.Input.MousePixel.Y);
        float dx = target.X - origin.X;
        float dy = target.Y - origin.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (!float.IsFinite(length) || length <= 0.001f)
        {
            return false;
        }

        dx /= length;
        dy /= length;
        ulong seed = ResolveRunSeed() ^ ((ulong)CastCount * 0x9E37_79B9_7F4A_7C15UL);
        WandCastResult result = WandSpellEvaluator.Evaluate(
            catalog,
            wand,
            state,
            seed,
            buffer,
            AvailableProjectileCount);
        LastProjectileCount = result.ProjectileCount;
        LastManaCost = MathF.Max(0f, result.ManaBefore - result.ManaAfter);
        LastManaSkippedCards = result.CardsSkippedForMana;
        LastUsesSkippedCards = result.CardsSkippedForUses;
        LastCastStatusText = StatusText(result.Status);
        if (result.Status == WandCastStatus.OutputCapacityExceeded)
        {
            PoolExhaustedCastCount++;
        }
        LastSpellSummary = buffer.ProjectileCount > 0
            ? catalog.Spells[buffer.Projectiles[0].SpellIndex].DisplayName
            : string.Empty;
        if (!result.Succeeded || result.ProjectileCount <= 0)
        {
            return result.Status == WandCastStatus.NoUsableSpell;
        }

        int count = Math.Min(result.ProjectileCount, _pendingPlans.Length);
        LastPayloadPlanCount = 0;
        for (int i = 0; i < count; i++)
        {
            _pendingPlans[i] = buffer.Projectiles[i];
            if (_pendingPlans[i].ParentIndex >= 0)
            {
                LastPayloadPlanCount++;
            }
        }

        _pendingProjectileCount = count;
        _pendingOriginX = origin.X;
        _pendingOriginY = origin.Y;
        _pendingDirectionX = dx;
        _pendingDirectionY = dy;
        _pendingCast = true;
        CastCount++;
        EmitCastFeedback(origin.X, origin.Y, dx, dy, in result);
        return true;
    }

    private void HandleSelection()
    {
        if (Catalog is null || Catalog.Wands.Length == 0)
        {
            return;
        }

        int count = Math.Min(9, Catalog.Wands.Length);
        for (int i = 0; i < count; i++)
        {
            if (Context.Input.WasPressed((Key)((int)Key.Digit1 + i)))
            {
                _ = SelectWand(i);
                return;
            }
        }

        float wheel = Context.Input.MouseWheelY;
        if (wheel > 0f)
        {
            _ = SelectWand((SelectedIndex + Catalog.Wands.Length - 1) % Catalog.Wands.Length);
        }
        else if (wheel < 0f)
        {
            _ = SelectWand((SelectedIndex + 1) % Catalog.Wands.Length);
        }
    }

    private void LoadCatalog()
    {
        if (Catalog is not null)
        {
            return;
        }

        Catalog = WandSpellCatalog.Load(Context.Config, CatalogPath);
        _buffer = new WandCastBuffer(Catalog.Limits);
        _states = new WandRuntimeState[Catalog.Wands.Length];
        _passives = new WandPassiveState[Catalog.Wands.Length];
        ulong runSeed = ResolveRunSeed();
        for (int i = 0; i < Catalog.Wands.Length; i++)
        {
            WandDefinition wand = Catalog.Wands[i];
            _states[i] = new WandRuntimeState(Catalog, wand, runSeed ^ (uint)i);
            _passives[i] = WandPassiveState.Compute(Catalog, wand);
        }

        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, Catalog.Wands.Length - 1));
        LastCastStatusText = "ready";
    }

    private void RegisterSpawnSystem()
    {
        if (_spawnSystemRegistered)
        {
            return;
        }

        Context.Scene.RegisterSystem(new WandProjectileSpawnSystem(this));
        _spawnSystemRegistered = true;
    }

    private void FlushPendingCast(IScriptContext context)
    {
        EnsureProjectilePool(context);
        if (!_pendingCast || _pendingProjectileCount <= 0)
        {
            return;
        }

        int count = _pendingProjectileCount;
        _pendingCast = false;
        _pendingProjectileCount = 0;
        if (count > _freeProjectileCount)
        {
            LastCastStatusText = "projectile_pool_exhausted";
            PoolExhaustedCastCount++;
            return;
        }

        // 所有结构性实体只在首帧 system phase 预热；稳态 cast 只租用固定池槽位。
        for (int i = 0; i < count; i++)
        {
            int freeIndex = --_freeProjectileCount;
            WandProjectile projectile = _freeProjectiles[freeIndex]!;
            _freeProjectiles[freeIndex] = null;
            projectile.Initialize(
                _pendingPlans[i],
                _pendingOriginX,
                _pendingOriginY,
                _pendingDirectionX,
                _pendingDirectionY,
                this,
                _pendingPlans[i].ParentIndex >= 0);
            _spawnedProjectiles[i] = projectile;
            SpawnedProjectileCount++;
        }

        for (int i = 0; i < count; i++)
        {
            WandProjectile projectile = _spawnedProjectiles[i]!;
            int parentIndex = _pendingPlans[i].ParentIndex;
            if ((uint)parentIndex < (uint)count)
            {
                _spawnedProjectiles[parentIndex]!.AttachPayload(projectile);
                LinkedPayloadCount++;
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (_pendingPlans[i].ParentIndex < 0)
            {
                _spawnedProjectiles[i]!.ActivatePreparedRoot();
            }
        }

        Array.Clear(_spawnedProjectiles, 0, count);
    }

    private void EnsureProjectilePool(IScriptContext context)
    {
        if (_poolCreatedCount == ProjectilePoolCapacity)
        {
            return;
        }

        for (int i = _poolCreatedCount; i < ProjectilePoolCapacity; i++)
        {
            Entity entity = context.Scene.CreateEntity();
            Transform transform = entity.AddComponent<Transform>();
            WandProjectile projectile = entity.AddComponent<WandProjectile>();
            projectile.InitializePoolSlot(this, transform);
            _projectilePool[i] = projectile;
        }

        _poolCreatedCount = ProjectilePoolCapacity;
        RebuildFreeProjectileStack();
    }

    private void RebuildFreeProjectileStack()
    {
        Array.Clear(_freeProjectiles);
        _freeProjectileCount = 0;
        for (int i = _poolCreatedCount - 1; i >= 0; i--)
        {
            WandProjectile projectile = _projectilePool[i]!;
            projectile.ResetForSession(this);
            _freeProjectiles[_freeProjectileCount++] = projectile;
        }
    }

    private void EmitCastFeedback(float x, float y, float dx, float dy, in WandCastResult result)
    {
        MaterialId spark = Context.Materials.Resolve("fire");
        if (spark.IsValid)
        {
            Context.Particles.Emit(
                x,
                y,
                dx,
                dy,
                coneRadians: 0.24f,
                minSpeed: 28f,
                maxSpeed: 62f,
                count: Math.Clamp(2 + result.ProjectileCount, 3, 12),
                material: spark,
                lifeTicks: 20);
        }

        Context.Lighting.AddPointLight(x, y, 18f, 0xFF_70_D8_FF, 0.22f);
    }

    private Point2F ResolveMuzzle()
    {
        return _player is null
            ? Context.Camera.ScreenToWorld(Context.Input.MousePixel.X, Context.Input.MousePixel.Y)
            : new Point2F(_player.CenterX, _player.CenterY - 2f);
    }

    private void ResolveComponents()
    {
        _player ??= Entity.TryGetComponent(out PlayerController player) ? player : null;
        _runDirector ??= Entity.TryGetComponent(out CampaignRunDirector run) ? run : null;
    }

    private ulong ResolveRunSeed()
    {
        ResolveComponents();
        return _runDirector is { RunSeed: not 0 } run ? run.RunSeed : FallbackRunSeed;
    }

    private void ResetRuntimeState()
    {
        _player = null;
        _runDirector = null;
        _states = [];
        _passives = [];
        Catalog = null;
        _buffer = null;
        SelectedIndex = 0;
        _pendingCast = false;
        _pendingProjectileCount = 0;
        CastCount = 0;
        LastProjectileCount = 0;
        LastCastStatusText = "idle";
        LastManaCost = 0f;
        LastManaSkippedCards = 0;
        LastUsesSkippedCards = 0;
        LastSpellSummary = string.Empty;
        SpawnedProjectileCount = 0;
        ActiveProjectileCount = 0;
        PayloadActivationCount = 0;
        LastPayloadPlanCount = 0;
        LinkedPayloadCount = 0;
        PoolExhaustedCastCount = 0;
        if (_poolCreatedCount > 0)
        {
            RebuildFreeProjectileStack();
        }
    }

    internal void NotifyProjectileActivated(bool payload)
    {
        ActiveProjectileCount++;
        if (payload)
        {
            PayloadActivationCount++;
        }
    }

    internal void NotifyProjectileDestroyed()
    {
        ActiveProjectileCount = Math.Max(0, ActiveProjectileCount - 1);
    }

    internal void ReturnProjectile(WandProjectile projectile)
    {
        ArgumentNullException.ThrowIfNull(projectile);
        if (_freeProjectileCount >= _freeProjectiles.Length)
        {
            throw new InvalidOperationException("Wand projectile pool 发生重复归还或容量损坏。");
        }

        _freeProjectiles[_freeProjectileCount++] = projectile;
    }

    private static string StatusText(WandCastStatus status)
    {
        return status switch
        {
            WandCastStatus.Success => "success",
            WandCastStatus.NotReady => "not_ready",
            WandCastStatus.NoUsableSpell => "no_usable_spell",
            WandCastStatus.BoundsExceeded => "bounds_exceeded",
            WandCastStatus.OutputCapacityExceeded => "projectile_pool_exhausted",
            _ => "unknown",
        };
    }

    private sealed class WandProjectileSpawnSystem(WandController owner) : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
            _ = context;
        }

        public void OnFrame(IScriptContext context, float dt)
        {
            _ = dt;
            owner.FlushPendingCast(context);
        }
    }
}
