namespace PixelEngine.Core.Memory;

/// <summary>
/// 指定 SoA 列的底层内存后端。
/// </summary>
public enum MemoryKind
{
    /// <summary>
    /// 使用 POH pinned 托管数组。
    /// </summary>
    Poh,

    /// <summary>
    /// 使用 <see cref="System.Runtime.InteropServices.NativeMemory"/> 非托管内存。
    /// </summary>
    Native,
}

internal interface ISoaColumn : IDisposable
{
    int Capacity { get; }

    void EnsureCapacity(int capacity);

    void Clear();
}

/// <summary>
/// 表示 SoA 布局中的一个强类型等长列。
/// </summary>
/// <typeparam name="T">非托管元素类型。</typeparam>
public sealed unsafe class SoaColumn<T> : IDisposable
    where T : unmanaged
{
    private readonly Storage _storage;

    internal SoaColumn(int capacity, MemoryKind memoryKind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _storage = new Storage(capacity, memoryKind);
    }

    /// <summary>
    /// 获取当前列容量。
    /// </summary>
    public int Capacity => _storage.Capacity;

    /// <summary>
    /// 获取列的 <see cref="Span{T}"/> 视图。
    /// </summary>
    public Span<T> Span => _storage.Span;

    /// <summary>
    /// 获取列的稳定指针。
    /// </summary>
    public T* Pointer => _storage.Pointer;

    /// <summary>
    /// 获取列首元素引用，供上层热循环进行引用漫游。
    /// </summary>
    /// <returns>首元素引用。</returns>
    public ref T GetReference()
    {
        return ref _storage.GetReference();
    }

    /// <summary>
    /// 释放列占用的底层缓冲。
    /// </summary>
    public void Dispose()
    {
        _storage.Dispose();
    }

    internal void EnsureCapacity(int capacity)
    {
        _storage.EnsureCapacity(capacity);
    }

    internal void Clear()
    {
        _storage.Clear();
    }

    private sealed class Storage : ISoaColumn
    {
        private readonly MemoryKind _memoryKind;
        private PinnedBuffer<T>? _poh;
        private NativeBuffer<T>? _native;
        private bool _disposed;

        public Storage(int capacity, MemoryKind memoryKind)
        {
            _memoryKind = memoryKind;
            Allocate(capacity);
        }

        public int Capacity => _poh?.Length ?? _native?.Length ?? 0;

        public Span<T> Span
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _poh is not null ? _poh.Span : _native!.Span;
            }
        }

        public T* Pointer
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _poh is not null ? _poh.Pointer : _native!.Pointer;
            }
        }

        public ref T GetReference()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ref (_poh is not null ? ref _poh.GetReference() : ref _native!.GetReference());
        }

        public void EnsureCapacity(int capacity)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);
            if (capacity <= Capacity)
            {
                return;
            }

            PinnedBuffer<T>? oldPoh = _poh;
            NativeBuffer<T>? oldNative = _native;
            Span<T> oldSpan = Span;

            _poh = null;
            _native = null;
            Allocate(capacity);
            oldSpan.CopyTo(Span);

            oldPoh?.Dispose();
            oldNative?.Dispose();
        }

        public void Clear()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _poh?.Dispose();
            _native?.Dispose();
            _poh = null;
            _native = null;
            _disposed = true;
        }

        private void Allocate(int capacity)
        {
            switch (_memoryKind)
            {
                case MemoryKind.Poh:
                    _poh = new PinnedBuffer<T>(capacity);
                    break;
                case MemoryKind.Native:
                    _native = new NativeBuffer<T>(capacity);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_memoryKind), _memoryKind, "未知内存后端。");
            }
        }
    }
}
