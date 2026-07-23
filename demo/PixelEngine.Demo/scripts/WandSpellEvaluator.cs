namespace PixelEngine.Demo;

/// <summary>
/// 单次 Wand cast 的固定容量输出。数组在装备 Wand 时创建，稳态求值只写入已有槽位。
/// </summary>
internal sealed class WandCastBuffer
{
    internal WandCastBuffer(WandEvaluationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        Projectiles = new SpellProjectilePlan[limits.MaxProjectilesPerCast];
        ProjectileWriteLimit = Projectiles.Length;
    }

    internal SpellProjectilePlan[] Projectiles { get; }

    internal int ProjectileCount { get; private set; }

    internal int CardsDrawn { get; private set; }

    internal int CardsSkippedForMana { get; private set; }

    internal int CardsSkippedForUses { get; private set; }

    internal int ProjectileWriteLimit { get; private set; }

    internal void Reset(int projectileWriteLimit = int.MaxValue)
    {
        ProjectileWriteLimit = Math.Clamp(projectileWriteLimit, 0, Projectiles.Length);
        ProjectileCount = 0;
        CardsDrawn = 0;
        CardsSkippedForMana = 0;
        CardsSkippedForUses = 0;
    }

    internal bool TryAdd(in SpellProjectilePlan plan, out int index)
    {
        if ((uint)ProjectileCount >= (uint)ProjectileWriteLimit)
        {
            index = -1;
            return false;
        }

        index = ProjectileCount;
        Projectiles[ProjectileCount++] = plan;
        return true;
    }

    internal void RecordDraw()
    {
        CardsDrawn++;
    }

    internal void RecordManaSkip()
    {
        CardsSkippedForMana++;
    }

    internal void RecordUsesSkip()
    {
        CardsSkippedForUses++;
    }
}

/// <summary>
/// 已解析的 projectile/world-effect 计划。ParentIndex 为 -1 表示立即发射，否则是 trigger payload。
/// </summary>
internal readonly record struct SpellProjectilePlan(
    int SpellIndex,
    int ParentIndex,
    WandProjectileKind Projectile,
    WandTriggerKind Trigger,
    float TriggerDelaySeconds,
    float Damage,
    float TerrainDamage,
    float Speed,
    float LifetimeSeconds,
    float Gravity,
    int Bounces,
    int ExplosionRadius,
    float AngleOffsetDegrees,
    string Material,
    int MaterialRadius,
    float LightRadius,
    float LightIntensity);

internal enum WandCastStatus : byte
{
    Success,
    NotReady,
    NoUsableSpell,
    BoundsExceeded,
    OutputCapacityExceeded,
}

internal readonly record struct WandCastResult(
    WandCastStatus Status,
    float ManaBefore,
    float ManaAfter,
    float CastDelaySeconds,
    float RechargeSeconds,
    int ProjectileCount,
    int CardsDrawn,
    int CardsSkippedForMana,
    int CardsSkippedForUses)
{
    internal bool Succeeded => Status == WandCastStatus.Success;
}

/// <summary>
/// 可保存的 Wand runtime 状态。Deck order 和 uses 数组均按 Wand capacity 固定，支持原子 evaluator 回滚。
/// </summary>
internal sealed class WandRuntimeState
{
    internal WandRuntimeState(WandSpellCatalog catalog, WandDefinition wand, ulong runSeed)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(wand);
        RunSeed = runSeed;
        DeckOrder = new int[wand.DeckSpellIndices.Length];
        UsesRemaining = new int[wand.DeckSpellIndices.Length];
        AlwaysUsesRemaining = new int[wand.AlwaysCastSpellIndices.Length];
        Reset(catalog, wand);
    }

    internal ulong RunSeed { get; }

    internal float Mana { get; set; }

    internal float CastDelayRemaining { get; set; }

    internal float RechargeRemaining { get; set; }

    internal int DeckCursor { get; set; }

    internal int DeckCycle { get; set; }

    internal int CastSequence { get; set; }

    internal bool DeckWasRefilled { get; set; }

    internal int[] DeckOrder { get; }

    internal int[] UsesRemaining { get; }

    internal int[] AlwaysUsesRemaining { get; }

    internal void Reset(WandSpellCatalog catalog, WandDefinition wand)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(wand);
        if (DeckOrder.Length != wand.DeckSpellIndices.Length ||
            UsesRemaining.Length != wand.DeckSpellIndices.Length ||
            AlwaysUsesRemaining.Length != wand.AlwaysCastSpellIndices.Length)
        {
            throw new ArgumentException("Wand runtime state 与传入 Wand 的容量不匹配。", nameof(wand));
        }

        Mana = wand.ManaMax;
        CastDelayRemaining = 0f;
        RechargeRemaining = 0f;
        DeckCursor = 0;
        DeckCycle = 0;
        CastSequence = 0;
        DeckWasRefilled = false;
        RefillDeck(wand, firstCycle: true);
        for (int i = 0; i < wand.DeckSpellIndices.Length; i++)
        {
            // maxUses 属于 card slot；同一 spell 若出现在不同 Wand，次数互不共享。
            UsesRemaining[i] = catalog.Spells[wand.DeckSpellIndices[i]].MaxUses;
        }

        for (int i = 0; i < wand.AlwaysCastSpellIndices.Length; i++)
        {
            AlwaysUsesRemaining[i] = catalog.Spells[wand.AlwaysCastSpellIndices[i]].MaxUses;
        }
    }

    internal void Advance(WandDefinition wand, in WandPassiveState passives, float dt)
    {
        float safeDt = float.IsFinite(dt) && dt > 0f ? dt : 0f;
        CastDelayRemaining = MathF.Max(0f, CastDelayRemaining - safeDt);
        RechargeRemaining = MathF.Max(0f, RechargeRemaining - safeDt);
        float multiplier = MathF.Max(0.01f, passives.ManaChargeMultiplier);
        Mana = MathF.Min(wand.ManaMax, Mana + (wand.ManaChargePerSecond * multiplier * safeDt));
    }

    internal void RefillDeck(WandDefinition wand, bool firstCycle = false)
    {
        for (int i = 0; i < DeckOrder.Length; i++)
        {
            DeckOrder[i] = i;
        }

        if (wand.Shuffle)
        {
            ulong random = WandSpellEvaluator.Hash(RunSeed, (ulong)wand.Index, (ulong)DeckCycle, 0x53485546464C45UL);
            for (int i = DeckOrder.Length - 1; i > 0; i--)
            {
                random = WandSpellEvaluator.NextRandom(random);
                int swapIndex = (int)(random % (uint)(i + 1));
                (DeckOrder[i], DeckOrder[swapIndex]) = (DeckOrder[swapIndex], DeckOrder[i]);
            }
        }

        DeckCursor = 0;
        if (!firstCycle)
        {
            DeckCycle++;
        }

        DeckWasRefilled = true;
    }
}

internal readonly record struct WandPassiveState(
    float ManaChargeMultiplier,
    float LightRadius,
    float LightIntensity)
{
    internal static WandPassiveState Compute(WandSpellCatalog catalog, WandDefinition wand)
    {
        float manaMultiplier = 1f;
        float lightRadius = 0f;
        float lightIntensity = 0f;
        for (int i = 0; i < wand.AlwaysCastSpellIndices.Length; i++)
        {
            Add(catalog.Spells[wand.AlwaysCastSpellIndices[i]], ref manaMultiplier, ref lightRadius, ref lightIntensity);
        }

        for (int i = 0; i < wand.DeckSpellIndices.Length; i++)
        {
            Add(catalog.Spells[wand.DeckSpellIndices[i]], ref manaMultiplier, ref lightRadius, ref lightIntensity);
        }

        return new WandPassiveState(manaMultiplier, lightRadius, MathF.Min(4f, lightIntensity));
    }

    private static void Add(
        WandSpellDefinition spell,
        ref float manaMultiplier,
        ref float lightRadius,
        ref float lightIntensity)
    {
        if (spell.Category != WandSpellCategory.Passive)
        {
            return;
        }

        WandSpellEffectDefinition effect = spell.Effect;
        manaMultiplier *= effect.ManaChargeMultiplier;
        lightRadius = MathF.Max(lightRadius, effect.LightRadius);
        lightIntensity += effect.LightIntensity;
    }
}

/// <summary>
/// Noita-like bounded deck evaluator。它只操作预分配 state/buffer，任何递归或输出超限均原子回滚。
/// </summary>
internal static class WandSpellEvaluator
{
    private const ulong HashSalt = 0x57414E445F434153UL;

    internal static WandCastResult Evaluate(
        WandSpellCatalog catalog,
        WandDefinition wand,
        WandRuntimeState state,
        ulong castSeed,
        WandCastBuffer buffer,
        int maxProjectiles = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(wand);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Reset(maxProjectiles);
        if (state.CastDelayRemaining > 0f || state.RechargeRemaining > 0f)
        {
            return new WandCastResult(
                WandCastStatus.NotReady,
                state.Mana,
                state.Mana,
                state.CastDelayRemaining,
                state.RechargeRemaining,
                0,
                0,
                0,
                0);
        }

        if (state.DeckOrder.Length > catalog.Limits.MaxWandCapacity ||
            wand.AlwaysCastSpellIndices.Length > catalog.Limits.MaxAlwaysCast ||
            buffer.Projectiles.Length < catalog.Limits.MaxProjectilesPerCast)
        {
            return new WandCastResult(WandCastStatus.BoundsExceeded, state.Mana, state.Mana, 0f, 0f, 0, 0, 0, 0);
        }

        Span<int> deckSnapshot = stackalloc int[state.DeckOrder.Length];
        Span<int> usesSnapshot = stackalloc int[state.UsesRemaining.Length];
        Span<int> alwaysUsesSnapshot = stackalloc int[state.AlwaysUsesRemaining.Length];
        state.DeckOrder.AsSpan().CopyTo(deckSnapshot);
        state.UsesRemaining.AsSpan().CopyTo(usesSnapshot);
        state.AlwaysUsesRemaining.AsSpan().CopyTo(alwaysUsesSnapshot);
        float manaBefore = state.Mana;
        float castDelayBefore = state.CastDelayRemaining;
        float rechargeBefore = state.RechargeRemaining;
        int cursorBefore = state.DeckCursor;
        int cycleBefore = state.DeckCycle;
        int sequenceBefore = state.CastSequence;
        bool refilledBefore = state.DeckWasRefilled;

        WandPassiveState passives = WandPassiveState.Compute(catalog, wand);
        EvaluationContext context = new(catalog, wand, state, buffer, castSeed, in passives);
        SpellModifierAccumulator modifiers = SpellModifierAccumulator.Default;
        for (int alwaysIndex = 0; alwaysIndex < wand.AlwaysCastSpellIndices.Length; alwaysIndex++)
        {
            int spellIndex = wand.AlwaysCastSpellIndices[alwaysIndex];
            WandSpellDefinition spell = catalog.Spells[spellIndex];
            if (spell.Category == WandSpellCategory.Passive ||
                !TryPayAlwaysCast(spell, state, alwaysIndex, buffer))
            {
                continue;
            }

            EvaluateSpell(ref context, spellIndex, parentIndex: -1, depth: 0, permanent: true, ref modifiers);
            if (context.BoundsExceeded)
            {
                break;
            }
        }

        if (!context.BoundsExceeded)
        {
            DrawMany(ref context, wand.SpellsPerCast, parentIndex: -1, depth: 0, instantReloadIfEmpty: false, ref modifiers);
        }

        if (context.BoundsExceeded)
        {
            bool outputCapacityExceeded = buffer.ProjectileWriteLimit < buffer.Projectiles.Length;
            deckSnapshot.CopyTo(state.DeckOrder);
            usesSnapshot.CopyTo(state.UsesRemaining);
            alwaysUsesSnapshot.CopyTo(state.AlwaysUsesRemaining);
            state.Mana = manaBefore;
            state.CastDelayRemaining = castDelayBefore;
            state.RechargeRemaining = rechargeBefore;
            state.DeckCursor = cursorBefore;
            state.DeckCycle = cycleBefore;
            state.CastSequence = sequenceBefore;
            state.DeckWasRefilled = refilledBefore;
            buffer.Reset();
            WandCastStatus rollbackStatus = outputCapacityExceeded
                ? WandCastStatus.OutputCapacityExceeded
                : WandCastStatus.BoundsExceeded;
            return new WandCastResult(rollbackStatus, manaBefore, manaBefore, 0f, 0f, 0, 0, 0, 0);
        }

        float castDelay = MathF.Max(0.01f, context.CastDelaySeconds);
        float recharge = MathF.Max(0.01f, context.RechargeSeconds);
        if (state.DeckCursor >= state.DeckOrder.Length)
        {
            state.RefillDeck(wand);
            recharge = MathF.Max(recharge, wand.RechargeSeconds);
        }

        state.CastDelayRemaining = castDelay;
        state.RechargeRemaining = recharge;
        state.CastSequence++;
        WandCastStatus status = buffer.ProjectileCount > 0
            ? WandCastStatus.Success
            : WandCastStatus.NoUsableSpell;
        return new WandCastResult(
            status,
            manaBefore,
            state.Mana,
            castDelay,
            recharge,
            buffer.ProjectileCount,
            buffer.CardsDrawn,
            buffer.CardsSkippedForMana,
            buffer.CardsSkippedForUses);
    }

    internal static ulong Hash(ulong seed, ulong a, ulong b, ulong c)
    {
        ulong value = seed ^ HashSalt;
        value ^= a + 0x9E3779B97F4A7C15UL + (value << 6) + (value >> 2);
        value ^= b + 0xBF58476D1CE4E5B9UL + (value << 7) + (value >> 3);
        value ^= c + 0x94D049BB133111EBUL + (value << 8) + (value >> 4);
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    internal static ulong NextRandom(ulong value)
    {
        value ^= value << 13;
        value ^= value >> 7;
        value ^= value << 17;
        return value;
    }

    private static bool TryPayAlwaysCast(
        WandSpellDefinition spell,
        WandRuntimeState state,
        int alwaysIndex,
        WandCastBuffer buffer)
    {
        if (state.AlwaysUsesRemaining[alwaysIndex] == 0)
        {
            buffer.RecordUsesSkip();
            return false;
        }

        if (spell.ManaCost > state.Mana)
        {
            buffer.RecordManaSkip();
            return false;
        }

        state.Mana -= spell.ManaCost;
        if (state.AlwaysUsesRemaining[alwaysIndex] > 0)
        {
            state.AlwaysUsesRemaining[alwaysIndex]--;
        }

        return true;
    }

    private static void DrawMany(
        ref EvaluationContext context,
        int count,
        int parentIndex,
        int depth,
        bool instantReloadIfEmpty,
        ref SpellModifierAccumulator modifiers)
    {
        if (count <= 0 || context.BoundsExceeded)
        {
            return;
        }

        if (depth > context.Catalog.Limits.MaxRecursionDepth)
        {
            context.BoundsExceeded = true;
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (++context.DrawOperations > context.Catalog.Limits.MaxDrawsPerCast)
            {
                context.BoundsExceeded = true;
                return;
            }

            if (!DrawOne(ref context, parentIndex, depth, instantReloadIfEmpty, ref modifiers))
            {
                return;
            }
        }
    }

    private static bool DrawOne(
        ref EvaluationContext context,
        int parentIndex,
        int depth,
        bool instantReloadIfEmpty,
        ref SpellModifierAccumulator modifiers)
    {
        int attempts = 0;
        int maxAttempts = Math.Max(1, context.State.DeckOrder.Length + 1);
        while (attempts++ < maxAttempts)
        {
            if (context.State.DeckCursor >= context.State.DeckOrder.Length)
            {
                if (!instantReloadIfEmpty)
                {
                    return false;
                }

                context.State.RefillDeck(context.Wand);
            }

            int slot = context.State.DeckOrder[context.State.DeckCursor++];
            int spellIndex = context.Wand.DeckSpellIndices[slot];
            WandSpellDefinition spell = context.Catalog.Spells[spellIndex];
            context.Buffer.RecordDraw();
            if (context.State.UsesRemaining[slot] == 0)
            {
                context.Buffer.RecordUsesSkip();
                continue;
            }

            if (spell.ManaCost > context.State.Mana)
            {
                context.Buffer.RecordManaSkip();
                continue;
            }

            context.State.Mana -= spell.ManaCost;
            if (context.State.UsesRemaining[slot] > 0)
            {
                context.State.UsesRemaining[slot]--;
            }

            EvaluateSpell(ref context, spellIndex, parentIndex, depth, permanent: false, ref modifiers);
            return !context.BoundsExceeded;
        }

        return false;
    }

    private static void EvaluateSpell(
        ref EvaluationContext context,
        int spellIndex,
        int parentIndex,
        int depth,
        bool permanent,
        ref SpellModifierAccumulator modifiers)
    {
        if (context.BoundsExceeded)
        {
            return;
        }

        WandSpellDefinition spell = context.Catalog.Spells[spellIndex];
        WandSpellEffectDefinition effect = spell.Effect;
        context.CastDelaySeconds += spell.CastDelaySeconds;
        context.RechargeSeconds += spell.RechargeSeconds;
        switch (spell.Category)
        {
            case WandSpellCategory.Projectile:
            case WandSpellCategory.Trigger:
            case WandSpellCategory.LimitedUse:
            case WandSpellCategory.Material:
            case WandSpellCategory.Utility:
                AppendProjectile(ref context, spellIndex, parentIndex, in modifiers);
                if (spell.Category == WandSpellCategory.Trigger && effect.Trigger != WandTriggerKind.None)
                {
                    SpellModifierAccumulator payloadModifiers = modifiers;
                    DrawMany(
                        ref context,
                        effect.TriggerDraw,
                        context.LastProjectileIndex,
                        depth + 1,
                        instantReloadIfEmpty: true,
                        ref payloadModifiers);
                }

                break;
            case WandSpellCategory.Modifier:
                modifiers = modifiers.Apply(effect);
                if (!permanent)
                {
                    DrawMany(ref context, 1, parentIndex, depth, instantReloadIfEmpty: true, ref modifiers);
                }

                break;
            case WandSpellCategory.Draw:
                SpellModifierAccumulator drawModifiers = modifiers with
                {
                    SpreadDegrees = modifiers.SpreadDegrees + effect.SpreadDegrees,
                };
                DrawMany(ref context, effect.DrawCount, parentIndex, depth + 1, instantReloadIfEmpty: true, ref drawModifiers);
                break;
            case WandSpellCategory.Passive:
                break;
            case WandSpellCategory.Special:
                RepeatLastProjectile(ref context, parentIndex, effect.RepeatCount);
                break;
            default:
                context.BoundsExceeded = true;
                break;
        }
    }

    private static void AppendProjectile(
        ref EvaluationContext context,
        int spellIndex,
        int parentIndex,
        in SpellModifierAccumulator modifiers)
    {
        WandSpellEffectDefinition effect = context.Catalog.Spells[spellIndex].Effect;
        float speed = effect.Speed * context.Wand.SpeedMultiplier * modifiers.SpeedMultiplier;
        float lifetime = effect.LifetimeSeconds * modifiers.LifetimeMultiplier;
        float damage = MathF.Max(0f, effect.Damage + modifiers.DamageAdd);
        float terrainDamage = MathF.Max(0f, effect.TerrainDamage + modifiers.TerrainDamageAdd);
        int bounces = Math.Clamp(effect.Bounces + modifiers.BouncesAdd, 0, 32);
        float spread = context.Wand.SpreadDegrees + effect.SpreadDegrees + modifiers.SpreadDegrees;
        int emissionIndex = context.Buffer.ProjectileCount;
        float normalized = (Hash(context.CastSeed, (ulong)emissionIndex, (ulong)spellIndex, 0x535052454144UL) / (float)ulong.MaxValue) - 0.5f;
        SpellProjectilePlan plan = new(
            spellIndex,
            parentIndex,
            effect.Projectile,
            effect.Trigger,
            effect.TriggerDelaySeconds,
            damage,
            terrainDamage,
            MathF.Max(1f, speed),
            MathF.Max(0.05f, lifetime),
            effect.Gravity + modifiers.GravityAdd,
            bounces,
            effect.ExplosionRadius,
            normalized * spread,
            effect.Material,
            effect.MaterialRadius,
            MathF.Max(effect.LightRadius, context.Passives.LightRadius),
            MathF.Min(4f, effect.LightIntensity + context.Passives.LightIntensity));
        if (!context.Buffer.TryAdd(in plan, out int index))
        {
            context.BoundsExceeded = true;
            return;
        }

        context.LastProjectileIndex = index;
    }

    private static void RepeatLastProjectile(ref EvaluationContext context, int parentIndex, int repeatCount)
    {
        if (context.LastProjectileIndex < 0)
        {
            return;
        }

        SpellProjectilePlan source = context.Buffer.Projectiles[context.LastProjectileIndex];
        for (int i = 0; i < repeatCount; i++)
        {
            SpellProjectilePlan copy = source with
            {
                ParentIndex = parentIndex,
                AngleOffsetDegrees = source.AngleOffsetDegrees + ((i + 1) * 3.5f),
            };
            if (!context.Buffer.TryAdd(in copy, out int index))
            {
                context.BoundsExceeded = true;
                return;
            }

            context.LastProjectileIndex = index;
        }
    }

    private struct EvaluationContext
    {
        internal EvaluationContext(
            WandSpellCatalog catalog,
            WandDefinition wand,
            WandRuntimeState state,
            WandCastBuffer buffer,
            ulong castSeed,
            in WandPassiveState passives)
        {
            Catalog = catalog;
            Wand = wand;
            State = state;
            Buffer = buffer;
            CastSeed = castSeed;
            Passives = passives;
            CastDelaySeconds = wand.CastDelaySeconds;
            RechargeSeconds = wand.RechargeSeconds;
            DrawOperations = 0;
            LastProjectileIndex = -1;
            BoundsExceeded = false;
        }

        internal readonly WandSpellCatalog Catalog;
        internal readonly WandDefinition Wand;
        internal readonly WandRuntimeState State;
        internal readonly WandCastBuffer Buffer;
        internal readonly ulong CastSeed;
        internal readonly WandPassiveState Passives;
        internal float CastDelaySeconds;
        internal float RechargeSeconds;
        internal int DrawOperations;
        internal int LastProjectileIndex;
        internal bool BoundsExceeded;
    }
}

internal readonly record struct SpellModifierAccumulator(
    float DamageAdd,
    float TerrainDamageAdd,
    float SpeedMultiplier,
    float LifetimeMultiplier,
    float GravityAdd,
    int BouncesAdd,
    float SpreadDegrees)
{
    internal static SpellModifierAccumulator Default => new(0f, 0f, 1f, 1f, 0f, 0, 0f);

    internal SpellModifierAccumulator Apply(WandSpellEffectDefinition effect)
    {
        return this with
        {
            DamageAdd = DamageAdd + effect.DamageAdd,
            TerrainDamageAdd = TerrainDamageAdd + effect.TerrainDamageAdd,
            SpeedMultiplier = SpeedMultiplier * effect.SpeedMultiplier,
            LifetimeMultiplier = LifetimeMultiplier * effect.LifetimeMultiplier,
            GravityAdd = GravityAdd + effect.GravityAdd,
            BouncesAdd = BouncesAdd + effect.BouncesAdd,
            SpreadDegrees = SpreadDegrees + effect.SpreadDegrees,
        };
    }
}
