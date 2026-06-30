namespace PixelEngine.Core.Memory;

/// <summary>
/// 表示由前台缓冲与后台缓冲组成的双缓冲容器。
/// </summary>
/// <typeparam name="TBuffer">缓冲对象类型。</typeparam>
public sealed class DoubleBuffer<TBuffer>
    where TBuffer : class
{
    /// <summary>
    /// 创建双缓冲容器。
    /// </summary>
    /// <param name="factory">用于创建前台与后台缓冲的工厂函数。</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="factory"/> 返回 <see langword="null"/>，或两次返回了同一个对象实例。</exception>
    public DoubleBuffer(Func<TBuffer> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Front = Create(factory);
        Back = Create(factory);

        if (ReferenceEquals(Front, Back))
        {
            throw new InvalidOperationException("双缓冲工厂函数必须返回两个不同的对象实例。");
        }
    }

    /// <summary>
    /// 获取当前供消费者读取的前台缓冲。
    /// </summary>
    public TBuffer Front { get; private set; }

    /// <summary>
    /// 获取当前供生产者写入的后台缓冲。
    /// </summary>
    public TBuffer Back { get; private set; }

    /// <summary>
    /// 交换前台与后台缓冲引用；不会复制缓冲内容。
    /// </summary>
    public void Swap()
    {
        (Front, Back) = (Back, Front);
    }

    private static TBuffer Create(Func<TBuffer> factory)
    {
        TBuffer buffer = factory();
        return buffer ?? throw new InvalidOperationException("双缓冲工厂函数返回了 null。");
    }
}
