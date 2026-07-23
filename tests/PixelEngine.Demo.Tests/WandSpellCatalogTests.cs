using System.Text.Json.Nodes;
using PixelEngine.Hosting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita-like Wand / Spell 目录、确定性 deck 与有界零分配求值契约。
/// </summary>
public sealed class WandSpellCatalogTests
{
    private const ulong RunSeed = 0x5049_5845_4C57_414EUL;

    /// <summary>
    /// 验证目录锁定 Noita Build 17130612 的 gun/action 来源身份，并完整覆盖九类原创 gameplay spell。
    /// </summary>
    [Fact]
    public void CatalogLocksReferenceInventoryAndCompilesFourWands()
    {
        WandSpellCatalog catalog = LoadCatalog();

        Assert.Equal(WandSpellCatalog.CurrentSchemaVersion, catalog.SchemaVersion);
        Assert.Equal("17130612", catalog.Reference.BuildId);
        Assert.Equal("9dbd52ced019a643169a2db02f46c77f8766c6e5", catalog.Reference.VersionHash);
        Assert.Equal(5, catalog.Reference.SourceFiles.Length);
        Assert.Equal(
            [
                "data/scripts/gun/gun.lua",
                "data/scripts/gun/gun_actions.lua",
                "data/scripts/gun/gun_enums.lua",
                "data/scripts/gun/gunaction_generated.lua",
                "data/scripts/gun/gun_extra_modifiers.lua",
            ],
            catalog.Reference.SourceFiles.Select(static source => source.Path));
        Assert.All(catalog.Reference.SourceFiles, static source => Assert.Equal(64, source.Sha256.Length));

        WandReferenceActionInventory inventory = catalog.Reference.ActionInventory;
        Assert.Equal(491, inventory.Total);
        Assert.Equal(144, inventory.Projectile);
        Assert.Equal(46, inventory.StaticProjectile);
        Assert.Equal(179, inventory.Modifier);
        Assert.Equal(14, inventory.DrawMany);
        Assert.Equal(35, inventory.Material);
        Assert.Equal(43, inventory.Other);
        Assert.Equal(25, inventory.Utility);
        Assert.Equal(5, inventory.Passive);

        Assert.Equal(25, catalog.Spells.Length);
        Assert.Equal(4, catalog.Wands.Length);
        Assert.Equal(
            ["apprentice-wand", "trigger-wand", "chaos-wand", "geomancer-wand"],
            catalog.Wands.Select(static wand => wand.Id));
        foreach (WandSpellCategory category in Enum.GetValues<WandSpellCategory>())
        {
            Assert.Contains(catalog.Spells, spell => spell.Category == category);
        }

        Assert.Equal(
            Enumerable.Range(0, catalog.Spells.Length),
            catalog.Spells.Select(static spell => spell.Index));
        Assert.Equal(
            Enumerable.Range(0, catalog.Wands.Length),
            catalog.Wands.Select(static wand => wand.Index));
    }

    /// <summary>
    /// 验证未知字段、伪造来源 hash、重复 spell 和不存在的 deck 引用全部 fail-closed。
    /// </summary>
    [Fact]
    public void CatalogRejectsUnknownFieldsAndInvalidStableReferences()
    {
        string source = File.ReadAllText(Path.Combine(ContentRoot(), "wand-spells.json"));

        JsonObject unknown = ParseObject(source);
        unknown["unmappedField"] = true;
        InvalidDataException unknownError = Assert.Throws<InvalidDataException>(
            () => WandSpellCatalog.Parse(unknown.ToJsonString()));
        Assert.Contains("未知字段 unmappedField", unknownError.Message, StringComparison.Ordinal);

        JsonObject badCommit = ParseObject(source);
        Reference(badCommit)["versionHash"] = new string('0', 64);
        InvalidDataException commitError = Assert.Throws<InvalidDataException>(
            () => WandSpellCatalog.Parse(badCommit.ToJsonString()));
        Assert.Contains("40 位 lowercase commit hash", commitError.Message, StringComparison.Ordinal);

        JsonObject badSourceHash = ParseObject(source);
        JsonArray sourceFiles = Assert.IsType<JsonArray>(Reference(badSourceHash)["sourceFiles"]);
        JsonObject firstSource = Assert.IsType<JsonObject>(sourceFiles[0]);
        firstSource["sha256"] = new string('A', 64);
        InvalidDataException sourceHashError = Assert.Throws<InvalidDataException>(
            () => WandSpellCatalog.Parse(badSourceHash.ToJsonString()));
        Assert.Contains("sha256 无效", sourceHashError.Message, StringComparison.Ordinal);

        JsonObject duplicateSpell = ParseObject(source);
        JsonArray spells = Assert.IsType<JsonArray>(duplicateSpell["spells"]);
        Assert.IsType<JsonObject>(spells[1])["id"] = "ember-bolt";
        InvalidDataException duplicateError = Assert.Throws<InvalidDataException>(
            () => WandSpellCatalog.Parse(duplicateSpell.ToJsonString()));
        Assert.Contains("spell id 重复", duplicateError.Message, StringComparison.Ordinal);

        JsonObject missingReference = ParseObject(source);
        JsonArray wands = Assert.IsType<JsonArray>(missingReference["wands"]);
        JsonArray deck = Assert.IsType<JsonArray>(Assert.IsType<JsonObject>(wands[0])["deck"]);
        deck[0] = "missing-spell";
        InvalidDataException referenceError = Assert.Throws<InvalidDataException>(
            () => WandSpellCatalog.Parse(missingReference.ToJsonString()));
        Assert.Contains("未知 spell id", referenceError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 non-shuffle 保持 card slot 顺序，shuffle 对相同 seed 完全确定且会随 run seed 改变。
    /// </summary>
    [Fact]
    public void DeckOrderingIsDeterministicAndSeedSensitive()
    {
        WandSpellCatalog catalog = LoadCatalog();
        WandDefinition apprentice = catalog.Wands[catalog.FindWandIndex("apprentice-wand")];
        WandRuntimeState ordered = new(catalog, apprentice, RunSeed);
        Assert.Equal(Enumerable.Range(0, apprentice.Deck.Length), ordered.DeckOrder);

        WandDefinition chaos = catalog.Wands[catalog.FindWandIndex("chaos-wand")];
        WandRuntimeState first = new(catalog, chaos, RunSeed);
        WandRuntimeState repeated = new(catalog, chaos, RunSeed);
        WandRuntimeState other = new(catalog, chaos, RunSeed ^ 0x9E37_79B9_7F4A_7C15UL);

        Assert.Equal(first.DeckOrder, repeated.DeckOrder);
        Assert.False(first.DeckOrder.AsSpan().SequenceEqual(other.DeckOrder));
    }

    /// <summary>
    /// 验证 modifier 自动 draw、multi-draw、special repeat、被动光照与 mana/cooldown 合并语义。
    /// </summary>
    [Fact]
    public void ApprenticeWandEvaluatesModifierDrawRepeatAndPassiveLight()
    {
        WandSpellCatalog catalog = LoadCatalog();
        WandDefinition wand = catalog.Wands[catalog.FindWandIndex("apprentice-wand")];
        WandRuntimeState state = new(catalog, wand, RunSeed);
        WandCastBuffer buffer = new(catalog.Limits);
        WandPassiveState passives = WandPassiveState.Compute(catalog, wand);

        WandCastResult passiveOnly = WandSpellEvaluator.Evaluate(catalog, wand, state, 1, buffer);
        Assert.Equal(WandCastStatus.NoUsableSpell, passiveOnly.Status);
        Assert.Equal(1, passiveOnly.CardsDrawn);

        state.Advance(wand, in passives, 10f);
        WandCastResult modified = WandSpellEvaluator.Evaluate(catalog, wand, state, 2, buffer);

        Assert.Equal(WandCastStatus.Success, modified.Status);
        Assert.Equal(1, modified.ProjectileCount);
        Assert.Equal(2, modified.CardsDrawn);
        Assert.Equal(207f, modified.ManaAfter);
        Assert.Equal(0.26f, modified.CastDelaySeconds, 3);
        Assert.Equal(0.85f, modified.RechargeSeconds, 3);
        SpellProjectilePlan ember = buffer.Projectiles[0];
        Assert.Equal(catalog.FindSpellIndex("ember-bolt"), ember.SpellIndex);
        Assert.Equal(97f, ember.Damage);
        Assert.Equal(108f, ember.TerrainDamage);
        Assert.Equal(42f, ember.LightRadius);
        Assert.Equal(0.75f, ember.LightIntensity);

        state.Advance(wand, in passives, 10f);
        WandCastResult woven = WandSpellEvaluator.Evaluate(catalog, wand, state, 3, buffer);

        Assert.Equal(WandCastStatus.Success, woven.Status);
        Assert.Equal(2, woven.ProjectileCount);
        Assert.Equal(4, woven.CardsDrawn);
        int stoneIndex = catalog.FindSpellIndex("stone-orb");
        Assert.Equal(stoneIndex, buffer.Projectiles[0].SpellIndex);
        Assert.Equal(stoneIndex, buffer.Projectiles[1].SpellIndex);
        Assert.Equal(
            3.5f,
            buffer.Projectiles[1].AngleOffsetDegrees - buffer.Projectiles[0].AngleOffsetDegrees,
            3);
    }

    /// <summary>
    /// 验证 hit/timer trigger 以 parent index 构成延迟 payload 树，并保留 material spell 计划。
    /// </summary>
    [Fact]
    public void TriggerWandBuildsNestedPayloadTree()
    {
        WandSpellCatalog catalog = LoadCatalog();
        WandDefinition wand = catalog.Wands[catalog.FindWandIndex("trigger-wand")];
        WandRuntimeState state = new(catalog, wand, RunSeed);
        WandCastBuffer buffer = new(catalog.Limits);

        WandCastResult result = WandSpellEvaluator.Evaluate(catalog, wand, state, 11, buffer);

        Assert.Equal(WandCastStatus.Success, result.Status);
        Assert.Equal(3, result.ProjectileCount);
        Assert.Equal(3, result.CardsDrawn);
        SpellProjectilePlan impact = buffer.Projectiles[0];
        SpellProjectilePlan timer = buffer.Projectiles[1];
        SpellProjectilePlan acid = buffer.Projectiles[2];
        Assert.Equal(catalog.FindSpellIndex("impact-trigger"), impact.SpellIndex);
        Assert.Equal(-1, impact.ParentIndex);
        Assert.Equal(WandTriggerKind.Hit, impact.Trigger);
        Assert.Equal(catalog.FindSpellIndex("timed-trigger"), timer.SpellIndex);
        Assert.Equal(0, timer.ParentIndex);
        Assert.Equal(WandTriggerKind.Timer, timer.Trigger);
        Assert.Equal(0.32f, timer.TriggerDelaySeconds);
        Assert.Equal(catalog.FindSpellIndex("acid-orb"), acid.SpellIndex);
        Assert.Equal(1, acid.ParentIndex);
        Assert.Equal(WandProjectileKind.Material, acid.Projectile);
        Assert.Equal("acid", acid.Material);
        Assert.Equal(4, acid.MaterialRadius);
    }

    /// <summary>
    /// 验证 utility 计划与 limited-use 次数按 card slot 消耗，耗尽后跳到下一张卡，Reset 正确恢复次数。
    /// </summary>
    [Fact]
    public void UtilityAndLimitedUseCardsProduceEffectsAndTrackPerSlotUses()
    {
        WandSpellCatalog catalog = LoadCatalog();
        WandCastBuffer buffer = new(catalog.Limits);

        WandDefinition geomancer = catalog.Wands[catalog.FindWandIndex("geomancer-wand")];
        WandRuntimeState utilityState = new(catalog, geomancer, RunSeed)
        {
            DeckCursor = 3,
        };
        WandCastResult utility = WandSpellEvaluator.Evaluate(catalog, geomancer, utilityState, 20, buffer);
        Assert.Equal(WandCastStatus.Success, utility.Status);
        Assert.Equal(WandProjectileKind.Dig, buffer.Projectiles[0].Projectile);
        Assert.Equal(420f, buffer.Projectiles[0].TerrainDamage);

        WandDefinition trigger = catalog.Wands[catalog.FindWandIndex("trigger-wand")];
        WandRuntimeState limitedState = new(catalog, trigger, RunSeed);
        WandPassiveState passives = WandPassiveState.Compute(catalog, trigger);
        const int DemolitionSlot = 5;
        for (int cast = 0; cast < 3; cast++)
        {
            limitedState.DeckCursor = DemolitionSlot;
            WandCastResult demolition = WandSpellEvaluator.Evaluate(
                catalog,
                trigger,
                limitedState,
                (ulong)(30 + cast),
                buffer);
            Assert.Equal(WandCastStatus.Success, demolition.Status);
            Assert.Equal(WandProjectileKind.Grenade, buffer.Projectiles[0].Projectile);
            Assert.Equal(2 - cast, limitedState.UsesRemaining[DemolitionSlot]);
            limitedState.Advance(trigger, in passives, 10f);
        }

        limitedState.DeckCursor = DemolitionSlot;
        WandCastResult exhausted = WandSpellEvaluator.Evaluate(catalog, trigger, limitedState, 40, buffer);
        Assert.Equal(WandCastStatus.Success, exhausted.Status);
        Assert.Equal(1, exhausted.CardsSkippedForUses);
        Assert.Equal(WandProjectileKind.Material, buffer.Projectiles[0].Projectile);
        Assert.Equal("water", buffer.Projectiles[0].Material);

        limitedState.Reset(catalog, trigger);
        Assert.Equal(3, limitedState.UsesRemaining[DemolitionSlot]);
    }

    /// <summary>
    /// 验证容量契约 fail-closed 且不改 state，并以真实 utility cast 测量稳态求值零托管堆分配。
    /// </summary>
    [Fact]
    public void EvaluatorBoundsAreAtomicAndSteadyStateIsAllocationFree()
    {
        WandSpellCatalog catalog = LoadCatalog();
        WandDefinition wand = catalog.Wands[catalog.FindWandIndex("geomancer-wand")];
        WandRuntimeState state = new(catalog, wand, RunSeed);
        WandCastBuffer undersized = new(new WandEvaluationLimits { MaxProjectilesPerCast = 8 });
        int[] deckBefore = [.. state.DeckOrder];
        int[] usesBefore = [.. state.UsesRemaining];
        float manaBefore = state.Mana;
        int cursorBefore = state.DeckCursor;

        WandCastResult rejected = WandSpellEvaluator.Evaluate(catalog, wand, state, 50, undersized);

        Assert.Equal(WandCastStatus.BoundsExceeded, rejected.Status);
        Assert.Equal(deckBefore, state.DeckOrder);
        Assert.Equal(usesBefore, state.UsesRemaining);
        Assert.Equal(manaBefore, state.Mana);
        Assert.Equal(cursorBefore, state.DeckCursor);
        Assert.Equal(0, undersized.ProjectileCount);

        WandCastBuffer buffer = new(catalog.Limits);
        WandCastResult poolRejected = WandSpellEvaluator.Evaluate(
            catalog,
            wand,
            state,
            51,
            buffer,
            maxProjectiles: 0);
        Assert.Equal(WandCastStatus.OutputCapacityExceeded, poolRejected.Status);
        Assert.Equal(deckBefore, state.DeckOrder);
        Assert.Equal(usesBefore, state.UsesRemaining);
        Assert.Equal(manaBefore, state.Mana);
        Assert.Equal(cursorBefore, state.DeckCursor);
        Assert.Equal(0, buffer.ProjectileCount);

        int checksum = 0;
        for (int i = 0; i < 512; i++)
        {
            state.Reset(catalog, wand);
            state.DeckCursor = 3;
            WandCastResult warmup = WandSpellEvaluator.Evaluate(catalog, wand, state, (ulong)i, buffer);
            checksum += warmup.ProjectileCount;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 4_096; i++)
        {
            state.Reset(catalog, wand);
            state.DeckCursor = 3;
            WandCastResult result = WandSpellEvaluator.Evaluate(catalog, wand, state, (ulong)i, buffer);
            checksum += result.ProjectileCount + buffer.Projectiles[0].SpellIndex;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        Assert.InRange(allocated, 0, 1_024);
    }

    private static WandSpellCatalog LoadCatalog()
    {
        return WandSpellCatalog.Load(new EngineScriptConfigApi(ContentRoot()));
    }

    private static JsonObject Reference(JsonObject document)
    {
        return Assert.IsType<JsonObject>(document["reference"]);
    }

    private static JsonObject ParseObject(string json)
    {
        return Assert.IsType<JsonObject>(JsonNode.Parse(json));
    }

    private static string ContentRoot()
    {
        return Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
