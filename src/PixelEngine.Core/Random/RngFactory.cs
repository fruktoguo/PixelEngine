namespace PixelEngine.Core.Random;

/// <summary>
/// 提供引擎随机数源的集中创建入口。
/// </summary>
public static class RngFactory
{
    /// <summary>
    /// 为指定 chunk 与帧创建有状态随机数源。
    /// </summary>
    /// <param name="worldSeed">世界种子。</param>
    /// <param name="chunkX">chunk X 坐标。</param>
    /// <param name="chunkY">chunk Y 坐标。</param>
    /// <param name="frame">帧编号。</param>
    /// <returns>chunk 专属随机数源。</returns>
    public static Pcg32 ForChunk(ulong worldSeed, int chunkX, int chunkY, uint frame)
    {
        ulong seed = Mix64(worldSeed ^ ((ulong)frame << 32));
        ulong stream = Mix64(((ulong)(uint)chunkX << 32) | (uint)chunkY);
        return new Pcg32(seed, stream);
    }

    /// <summary>
    /// 创建默认高性能随机数源。
    /// </summary>
    /// <param name="seed">基础种子。</param>
    /// <returns>随机数源。</returns>
    public static IRandomSource CreateDefault(ulong seed)
    {
        return new Pcg32(seed, Mix64(seed ^ 0xD1B54A32D192ED03UL));
    }

    /// <summary>
    /// 创建确定性模式使用的随机数源。
    /// </summary>
    /// <param name="seed">基础种子。</param>
    /// <returns>随机数源。</returns>
    public static IRandomSource CreateDeterministic(ulong seed)
    {
        return new Pcg32(Mix64(seed), Mix64(seed ^ 0xA0761D6478BD642FUL));
    }

    private static ulong Mix64(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return value;
    }
}
