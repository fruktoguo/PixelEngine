namespace PixelEngine.Core.Random;

/// <summary>
/// 定义引擎内部使用的随机数源接口。
/// </summary>
public interface IRandomSource
{
    /// <summary>
    /// 返回下一个 32 位无符号随机数。
    /// </summary>
    /// <returns>随机位。</returns>
    uint NextUInt();

    /// <summary>
    /// 返回位于 <c>[0, maxExclusive)</c> 的随机整数。
    /// </summary>
    /// <param name="maxExclusive">排他的上界，必须大于 0。</param>
    /// <returns>随机整数。</returns>
    int NextInt(int maxExclusive);

    /// <summary>
    /// 返回位于 <c>[0, 1)</c> 的随机浮点数。
    /// </summary>
    /// <returns>随机浮点数。</returns>
    float NextFloat();
}
