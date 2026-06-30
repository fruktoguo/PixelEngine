namespace PixelEngine.Core.Memory;

/// <summary>
/// 多列等长 SoA 缓冲的生命周期与容量管理基类。
/// </summary>
public abstract class SoaBuffer : IDisposable
{
    private readonly List<ISoaColumn> _columns = [];
    private bool _disposed;

    /// <summary>
    /// 获取当前有效元素数量。
    /// </summary>
    public int Count { get; protected set; }

    /// <summary>
    /// 获取当前列容量。
    /// </summary>
    public int Capacity { get; private set; }

    /// <summary>
    /// 定义一个新的 SoA 强类型列。派生类型应在构造阶段调用。
    /// </summary>
    /// <typeparam name="T">非托管元素类型。</typeparam>
    /// <param name="memoryKind">底层内存后端。</param>
    /// <returns>新建列。</returns>
    protected SoaColumn<T> DefineColumn<T>(MemoryKind memoryKind)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SoaColumn<T> column = new(Capacity, memoryKind);
        _columns.Add(new ColumnAdapter<T>(column));
        return column;
    }

    /// <summary>
    /// 确保所有列至少具有指定容量，并保留原有数据。
    /// </summary>
    /// <param name="capacity">所需最小容量。</param>
    public void EnsureCapacity(int capacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (capacity <= Capacity)
        {
            return;
        }

        int newCapacity = Capacity == 0 ? 1 : Capacity;
        while (newCapacity < capacity)
        {
            newCapacity = checked(newCapacity * 2);
        }

        foreach (ISoaColumn column in _columns)
        {
            column.EnsureCapacity(newCapacity);
        }

        Capacity = newCapacity;
    }

    /// <summary>
    /// 清零所有列并将有效元素数量置为 0。
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (ISoaColumn column in _columns)
        {
            column.Clear();
        }

        Count = 0;
    }

    /// <summary>
    /// 释放所有列的底层缓冲。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (ISoaColumn column in _columns)
        {
            column.Dispose();
        }

        _columns.Clear();
        Count = 0;
        Capacity = 0;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class ColumnAdapter<T>(SoaColumn<T> column) : ISoaColumn
        where T : unmanaged
    {
        public int Capacity => column.Capacity;

        public void EnsureCapacity(int capacity)
        {
            column.EnsureCapacity(capacity);
        }

        public void Clear()
        {
            column.Clear();
        }

        public void Dispose()
        {
            column.Dispose();
        }
    }
}
