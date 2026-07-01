using System.Numerics;
using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// 定容开放寻址冷却表，用于抑制同一材质同一事件类型的重复播放。
/// </summary>
public sealed class CooldownTracker
{
    private readonly Entry[] _entries;
    private readonly int _mask;
    private int _nextReplacement;

    /// <summary>
    /// 创建冷却表。
    /// </summary>
    /// <param name="capacityPow2">容量，必须是正的 2 的幂。</param>
    public CooldownTracker(int capacityPow2)
    {
        if (capacityPow2 <= 0 || !BitOperations.IsPow2(capacityPow2))
        {
            throw new ArgumentOutOfRangeException(nameof(capacityPow2), "容量必须是正的 2 的幂。");
        }

        _entries = new Entry[capacityPow2];
        _mask = capacityPow2 - 1;
    }

    /// <summary>
    /// 判断当前事件是否可播放，并在允许播放时更新冷却时间。
    /// </summary>
    /// <param name="materialId">材质 runtime id。</param>
    /// <param name="type">事件类型。</param>
    /// <param name="tick">当前模拟 tick。</param>
    /// <param name="cooldownTicks">冷却 tick 数；小于等于 0 时仅更新不抑制。</param>
    /// <returns>若不在冷却期内则为 <see langword="true"/>。</returns>
    public bool ShouldPlay(ushort materialId, AudioEventType type, long tick, int cooldownTicks)
    {
        ulong key = MakeKey(materialId, type);
        int slot = FindSlot(key, out bool found);
        if (found)
        {
            long delta = tick - _entries[slot].LastTick;
            if (cooldownTicks > 0 && delta >= 0 && delta < cooldownTicks)
            {
                return false;
            }

            _entries[slot].LastTick = tick;
            return true;
        }

        _entries[slot] = new Entry
        {
            Occupied = true,
            Key = key,
            LastTick = tick,
        };
        return true;
    }

    private int FindSlot(ulong key, out bool found)
    {
        int slot = (int)Hash(key) & _mask;
        for (int probes = 0; probes < _entries.Length; probes++)
        {
            Entry entry = _entries[slot];
            if (!entry.Occupied)
            {
                found = false;
                return slot;
            }

            if (entry.Key == key)
            {
                found = true;
                return slot;
            }

            slot = (slot + 1) & _mask;
        }

        found = false;
        int replacement = _nextReplacement++ & _mask;
        return replacement;
    }

    private static ulong MakeKey(ushort materialId, AudioEventType type)
    {
        return ((ulong)materialId << 8) | (byte)type;
    }

    private static uint Hash(ulong key)
    {
        unchecked
        {
            key ^= key >> 33;
            key *= 0xff51afd7ed558ccdUL;
            key ^= key >> 33;
            key *= 0xc4ceb9fe1a85ec53UL;
            key ^= key >> 33;
            return (uint)key;
        }
    }

    private struct Entry
    {
        public bool Occupied;
        public ulong Key;
        public long LastTick;
    }
}
