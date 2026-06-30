using System.Runtime.InteropServices;

namespace PixelEngine.Core.Memory;

/// <summary>
/// 封装通过 <see cref="NativeMemory"/> 分配的非托管连续缓冲。
/// </summary>
/// <typeparam name="T">非托管元素类型。</typeparam>
public sealed unsafe class NativeBuffer<T> : IDisposable
    where T : unmanaged
{
    private T* _pointer;
    private bool _disposed;

    /// <summary>
    /// 创建指定长度的非托管缓冲。
    /// </summary>
    /// <param name="length">元素数量。</param>
    /// <param name="zeroed">是否使用零初始化分配。</param>
    public NativeBuffer(int length, bool zeroed = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        _pointer = Allocate(length, zeroed);
    }

    /// <summary>
    /// 终结器兜底释放非托管内存。
    /// </summary>
    ~NativeBuffer()
    {
        Free();
    }

    /// <summary>
    /// 获取元素数量。
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// 获取非托管内存指针。
    /// </summary>
    public T* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pointer;
        }
    }

    /// <summary>
    /// 获取缓冲的 <see cref="Span{T}"/> 视图。
    /// </summary>
    public Span<T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new Span<T>(_pointer, Length);
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
            return ref Span[index];
        }
    }

    /// <summary>
    /// 获取缓冲首元素引用，供上层热循环进行引用漫游。
    /// </summary>
    /// <returns>首元素引用。</returns>
    public ref T GetReference()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Length == 0)
        {
            throw new InvalidOperationException("空缓冲没有可引用的首元素。");
        }

        return ref *_pointer;
    }

    /// <summary>
    /// 将缓冲内容清零。
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Span.Clear();
    }

    /// <summary>
    /// 释放非托管内存。
    /// </summary>
    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    private static T* Allocate(int length, bool zeroed)
    {
        if (length == 0)
        {
            return null;
        }

        nuint elementCount = checked((nuint)length);
        nuint elementSize = checked((nuint)sizeof(T));
        return zeroed
            ? (T*)NativeMemory.AllocZeroed(elementCount, elementSize)
            : (T*)NativeMemory.Alloc(elementCount, elementSize);
    }

    private void Free()
    {
        if (_disposed)
        {
            return;
        }

        NativeMemory.Free(_pointer);
        _pointer = null;
        Length = 0;
        _disposed = true;
    }
}
