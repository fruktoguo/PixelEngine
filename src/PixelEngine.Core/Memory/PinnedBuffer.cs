using System.Runtime.InteropServices;

namespace PixelEngine.Core.Memory;

/// <summary>
/// 封装 POH pinned 托管数组，避免每次互操作调用重复 pin。
/// </summary>
/// <typeparam name="T">非托管元素类型。</typeparam>
public sealed unsafe class PinnedBuffer<T> : IDisposable
    where T : unmanaged
{
    private T[] _items;
    private bool _disposed;

    /// <summary>
    /// 创建指定长度的 pinned 托管缓冲。
    /// </summary>
    /// <param name="length">元素数量。</param>
    public PinnedBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _items = GC.AllocateArray<T>(length, pinned: true);
    }

    /// <summary>
    /// 获取元素数量。
    /// </summary>
    public int Length => _items.Length;

    /// <summary>
    /// 获取缓冲的 <see cref="Span{T}"/> 视图。
    /// </summary>
    public Span<T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _items;
        }
    }

    /// <summary>
    /// 获取缓冲的 <see cref="Memory{T}"/> 视图。
    /// </summary>
    public Memory<T> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _items;
        }
    }

    /// <summary>
    /// 获取 pinned 数组的稳定原生指针。
    /// </summary>
    public T* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_items.Length == 0)
            {
                return null;
            }

            fixed (T* pointer = _items)
            {
                return pointer;
            }
        }
    }

    /// <summary>
    /// 获取指定索引处元素的引用。
    /// </summary>
    /// <param name="index">元素索引。</param>
    /// <returns>元素引用。</returns>
    public ref T this[int index]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ref _items[index];
        }
    }

    /// <summary>
    /// 获取缓冲首元素引用，供上层热循环进行引用漫游。
    /// </summary>
    /// <returns>首元素引用。</returns>
    public ref T GetReference()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_items.Length == 0)
        {
            throw new InvalidOperationException("空缓冲没有可引用的首元素。");
        }

        return ref MemoryMarshal.GetArrayDataReference(_items);
    }

    /// <summary>
    /// 将缓冲内容清零。
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(_items);
    }

    /// <summary>
    /// 释放托管引用，使 pinned 数组可由 GC 回收。
    /// </summary>
    public void Dispose()
    {
        _items = [];
        _disposed = true;
    }
}
