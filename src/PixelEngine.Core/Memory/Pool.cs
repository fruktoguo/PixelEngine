namespace PixelEngine.Core.Memory;

/// <summary>
/// 提供面向单线程热路径的对象池。
/// </summary>
/// <typeparam name="T">池化对象类型。</typeparam>
/// <remarks>
/// 本类型不加锁；并发场景应由调用方按线程或 worker 隔离实例。
/// </remarks>
public sealed class Pool<T>
    where T : class
{
    private readonly Func<T> _factory;
    private readonly Action<T>? _onRent;
    private readonly Action<T>? _onReturn;
    private T[] _items;

    /// <summary>
    /// 创建对象池。
    /// </summary>
    /// <param name="factory">用于创建新对象的工厂函数。</param>
    /// <param name="onRent">对象被租出时调用的回调。</param>
    /// <param name="onReturn">对象归还前调用的回调。</param>
    /// <param name="preallocate">预创建并保留在池中的对象数量。</param>
    /// <param name="maxRetained">最多保留的空闲对象数量，<c>-1</c> 表示无限保留。</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preallocate"/> 小于 0，或 <paramref name="maxRetained"/> 小于 -1。</exception>
    /// <exception cref="ArgumentException"><paramref name="preallocate"/> 大于有限的 <paramref name="maxRetained"/>。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="factory"/> 返回 <see langword="null"/>。</exception>
    public Pool(
        Func<T> factory,
        Action<T>? onRent = null,
        Action<T>? onReturn = null,
        int preallocate = 0,
        int maxRetained = -1)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfNegative(preallocate);

        if (maxRetained < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetained), maxRetained, "最大保留数量不能小于 -1。");
        }

        if (maxRetained >= 0 && preallocate > maxRetained)
        {
            throw new ArgumentException("预创建数量不能大于有限的最大保留数量。", nameof(preallocate));
        }

        _factory = factory;
        _onRent = onRent;
        _onReturn = onReturn;
        MaxRetained = maxRetained;
        _items = preallocate == 0 ? [] : new T[preallocate];

        for (int i = 0; i < preallocate; i++)
        {
            _items[i] = Create();
        }

        CountInactive = preallocate;
    }

    /// <summary>
    /// 获取当前池中空闲对象数量。
    /// </summary>
    public int CountInactive { get; private set; }

    private int MaxRetained { get; }

    /// <summary>
    /// 从池中租出一个对象；若池为空则通过工厂函数创建。
    /// </summary>
    /// <returns>租出的对象。</returns>
    /// <exception cref="InvalidOperationException">工厂函数返回 <see langword="null"/>。</exception>
    public T Rent()
    {
        T item;

        if (CountInactive == 0)
        {
            item = Create();
        }
        else
        {
            int index = --CountInactive;
            item = _items[index];
            _items[index] = null!;
        }

        _onRent?.Invoke(item);
        return item;
    }

    /// <summary>
    /// 将对象归还到池中。
    /// </summary>
    /// <param name="item">要归还的对象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> 为 <see langword="null"/>。</exception>
    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _onReturn?.Invoke(item);

        if (MaxRetained >= 0 && CountInactive >= MaxRetained)
        {
            return;
        }

        EnsureCapacityForOneMore();
        _items[CountInactive++] = item;
    }

    private T Create()
    {
        T item = _factory();
        return item ?? throw new InvalidOperationException("对象池工厂函数返回了 null。");
    }

    private void EnsureCapacityForOneMore()
    {
        if (CountInactive < _items.Length)
        {
            return;
        }

        int newCapacity = _items.Length == 0 ? 4 : _items.Length * 2;

        if (MaxRetained >= 0)
        {
            newCapacity = Math.Min(newCapacity, MaxRetained);
        }

        Array.Resize(ref _items, newCapacity);
    }
}
