using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>Loot marker、金币与未装配法术库存的快速行为测试。</summary>
public sealed class NoitaMarkerLootTests
{
    /// <summary>验证金币收支与法术容量。</summary>
    [Fact]
    public void PlayerInventoryTracksGoldSpendAndSpellCapacity()
    {
        PlayerInventory inventory = new();

        inventory.GrantGold(80);
        Assert.True(inventory.TrySpendGold(50));
        Assert.False(inventory.TrySpendGold(31));
        Assert.Equal(30, inventory.Gold);
        Assert.Equal(80, inventory.TotalGoldCollected);

        for (int i = 0; i < PlayerInventory.SpellCapacity; i++)
        {
            Assert.True(inventory.TryAddSpell(i));
        }

        Assert.False(inventory.TryAddSpell(PlayerInventory.SpellCapacity));
        Assert.Equal(PlayerInventory.SpellCapacity, inventory.SpellCount);
    }

    /// <summary>验证宝箱金币确定性和一次性消费。</summary>
    [Fact]
    public void TreasureInteractionGrantsDeterministicGoldAndPersistsConsumption()
    {
        NoitaMarkerLoot loot = CreateLoot("spawn_chest", worldX: 120, worldY: 340, seed: 42);
        PlayerInventory inventory = new();

        Assert.InRange(loot.GoldValue, 20, 50);
        Assert.True(loot.TryInteract(inventory, new PlayerHealth(), availableSpellCount: 491));
        Assert.Equal(loot.GoldValue, inventory.Gold);
        Assert.True(loot.IsConsumed);
        Assert.False(loot.TryInteract(inventory, new PlayerHealth(), availableSpellCount: 491));
    }

    /// <summary>验证商店金币门槛与法术目录约束。</summary>
    [Fact]
    public void ShopSpellRequiresGoldAndAddsCatalogBoundSpell()
    {
        NoitaMarkerLoot loot = CreateLoot("spawn_shopitem", worldX: 44, worldY: 88, seed: 7);
        PlayerInventory inventory = new();

        Assert.False(loot.TryInteract(inventory, new PlayerHealth(), availableSpellCount: 12));
        Assert.False(loot.IsConsumed);

        inventory.GrantGold(loot.Price);
        Assert.True(loot.TryInteract(inventory, new PlayerHealth(), availableSpellCount: 12));
        Assert.Equal(0, inventory.Gold);
        Assert.Equal(1, inventory.SpellCount);
        Assert.InRange(inventory.SpellIndices[0], 0, 11);
    }

    /// <summary>验证已实现 loot marker 进入真实交互 profile。</summary>
    [Theory]
    [InlineData("spawn_hp")]
    [InlineData("spawn_chest")]
    [InlineData("spawn_shopitem")]
    [InlineData("spawn_specialshop")]
    public void SupportedLootMarkersCreateGameplayProfiles(string function)
    {
        NoitaWangMarkerAnchor anchor = CreateAnchor(function, 1, 2);

        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(anchor, out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.Loot, profile.GameplayKind);
    }

    /// <summary>验证未实现的通用 loot marker 不会冒充法术拾取。</summary>
    [Fact]
    public void UnsupportedGenericLootMarkerDoesNotBecomeSpellPickup()
    {
        NoitaWangMarkerAnchor anchor = CreateAnchor("spawn_perk_reroll", 1, 2);

        Assert.False(NoitaWangMarkerVisualProfile.TryCreate(anchor, out _));
    }

    private static NoitaMarkerLoot CreateLoot(string function, long worldX, long worldY, ulong seed)
    {
        NoitaMarkerLoot loot = new();
        loot.Bind(CreateAnchor(function, worldX, worldY), seed);
        return loot;
    }

    private static NoitaWangMarkerAnchor CreateAnchor(string function, long worldX, long worldY)
    {
        return new NoitaWangMarkerAnchor(
            "coalmine",
            "coalmine",
            "#ffffffff",
            function,
            "test",
            1,
            worldX,
            worldY);
    }
}
