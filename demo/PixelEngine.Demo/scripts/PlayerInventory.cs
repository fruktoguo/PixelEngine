using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>Noita 风格玩家持久库存：金币与未装入法杖的法术卡。</summary>
public sealed class PlayerInventory : Behaviour
{
    /// <summary>玩家可携带的未装配法术上限。</summary>
    public const int SpellCapacity = 16;
    private readonly int[] _spellIndices = new int[SpellCapacity];

    /// <summary>当前可消费金币。</summary>
    public int Gold { get; private set; }

    /// <summary>当前携带的未装配法术数量。</summary>
    public int SpellCount { get; private set; }

    /// <summary>本轮累计获得的金币，不因购买而减少。</summary>
    public int TotalGoldCollected { get; private set; }

    /// <summary>按拾取顺序返回当前携带的法术目录索引。</summary>
    public ReadOnlySpan<int> SpellIndices => _spellIndices.AsSpan(0, SpellCount);

    /// <summary>增加金币；非正数输入不产生效果。</summary>
    /// <param name="amount">增加量。</param>
    public void GrantGold(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Gold = checked(Gold + amount);
        TotalGoldCollected = checked(TotalGoldCollected + amount);
    }

    /// <summary>尝试消费指定金币。</summary>
    /// <param name="amount">消费量。</param>
    /// <returns>余额充足且输入非负时返回 <see langword="true"/>。</returns>
    public bool TrySpendGold(int amount)
    {
        if (amount < 0 || Gold < amount)
        {
            return false;
        }

        Gold -= amount;
        return true;
    }

    /// <summary>尝试把法术目录索引放入未装配库存。</summary>
    /// <param name="spellIndex">法术目录索引。</param>
    /// <returns>索引有效且库存未满时返回 <see langword="true"/>。</returns>
    public bool TryAddSpell(int spellIndex)
    {
        if (spellIndex < 0 || SpellCount >= _spellIndices.Length)
        {
            return false;
        }

        _spellIndices[SpellCount++] = spellIndex;
        return true;
    }
}
