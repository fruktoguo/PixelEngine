using System.Buffers;

namespace PixelEngine.Core.Memory;

/// <summary>
/// 表示从 <see cref="ArrayPool{T}.Shared"/> 租出的数组切片，并在释放时自动归还。
/// </summary>
/// <typeparam name="T">数组元素类型。</typeparam>
public readonly struct RentedArray<T> : IDisposable
{
    private readonly bool _clear;

    private RentedArray(T[] array, int length, bool clear)
    {
        Array = array;
        Length = length;
        _clear = clear;
    }

    /// <summary>
    /// 获取实际租出的数组；其长度可能大于 <see cref="Length"/>。
    /// </summary>
    public T[] Array { get; }

    /// <summary>
    /// 获取调用方请求使用的有效长度。
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// 获取有效长度范围内的 <see cref="Span{T}"/>。
    /// </summary>
    public Span<T> Span => Array is null ? [] : Array.AsSpan(0, Length);

    /// <summary>
    /// 从共享数组池租出长度至少为指定值的数组。
    /// </summary>
    /// <param name="minLength">所需的最小有效长度。</param>
    /// <param name="clear">是否在租出和归还时清理有效范围内的元素。</param>
    /// <returns>租出的数组包装。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minLength"/> 小于 0。</exception>
    public static RentedArray<T> Rent(int minLength, bool clear = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minLength);

        T[] array = ArrayPool<T>.Shared.Rent(minLength);
        RentedArray<T> rented = new(array, minLength, clear);

        if (clear)
        {
            rented.Span.Clear();
        }

        return rented;
    }

    /// <summary>
    /// 将数组归还到共享数组池。
    /// </summary>
    public void Dispose()
    {
        if (Array is null)
        {
            return;
        }

        ArrayPool<T>.Shared.Return(Array, clearArray: _clear);
    }
}
