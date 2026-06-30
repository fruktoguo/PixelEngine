using System.Buffers;

namespace PixelEngine.Serialization;

/// <summary>
/// 基于 ArrayPool 的可复用 byte IBufferWriter，适合 chunk 编解码临时缓冲。
/// </summary>
public sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int DefaultInitialCapacity = 16 * 1024;

    private byte[] _buffer;
    private bool _disposed;

    /// <summary>
    /// 创建 pooled byte writer。
    /// </summary>
    public PooledByteBufferWriter(int initialCapacity = DefaultInitialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    /// <summary>
    /// 已写入字节数。
    /// </summary>
    public int WrittenCount { get; private set; }

    /// <summary>
    /// 已写入字节视图。
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    /// <summary>
    /// 清空已写入长度，保留租用缓冲。
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        WrittenCount = 0;
    }

    /// <summary>
    /// 通知 writer 调用方已经写入指定字节数。
    /// </summary>
    public void Advance(int count)
    {
        ThrowIfDisposed();
        if (count < 0 || WrittenCount > _buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        WrittenCount += count;
    }

    /// <summary>
    /// 获取至少包含 <paramref name="sizeHint" /> 字节可写空间的内存。
    /// </summary>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    /// <summary>
    /// 获取至少包含 <paramref name="sizeHint" /> 字节可写空间的 Span。
    /// </summary>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    /// <summary>
    /// 复制已写入内容到新数组。
    /// </summary>
    public byte[] ToArray()
    {
        ThrowIfDisposed();
        return WrittenSpan.ToArray();
    }

    /// <summary>
    /// 归还租用缓冲，释放 writer 的后续使用权。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
        WrittenCount = 0;
        _disposed = true;
    }

    private void Ensure(int sizeHint)
    {
        ThrowIfDisposed();
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint <= _buffer.Length - WrittenCount)
        {
            return;
        }

        Grow(sizeHint);
    }

    private void Grow(int sizeHint)
    {
        int required = checked(WrittenCount + sizeHint);
        int next = Math.Max(required, _buffer.Length * 2);
        byte[] replacement = ArrayPool<byte>.Shared.Rent(next);
        _buffer.AsSpan(0, WrittenCount).CopyTo(replacement);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = replacement;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
