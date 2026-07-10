using System.Globalization;

namespace PixelEngine.Scripting;

/// <summary>
/// 为即时模式 GUI 复用的可增长字符缓冲区，用于在稳态帧循环中无分配地构造动态文本。
/// </summary>
/// <remarks>
/// 实例不是线程安全的；调用方应为每个 GUI 绘制线程或 Behaviour 独占实例。容量不足时会增长，
/// 因此应在初始化阶段提供足以覆盖常见文本的初始容量，让稳态帧只复用既有数组。
/// </remarks>
public sealed class GuiTextBuffer
{
    private const int DefaultCapacity = 256;
    private char[] _buffer;

    /// <summary>
    /// 创建 GUI 文本缓冲区。
    /// </summary>
    /// <param name="initialCapacity">初始字符容量；应覆盖稳态下最长的一行文本。</param>
    public GuiTextBuffer(int initialCapacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _buffer = GC.AllocateUninitializedArray<char>(initialCapacity);
    }

    /// <summary>
    /// 当前已写入的字符数。
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// 当前字符容量。
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// 当前已写入内容的只读视图；视图仅在下一次修改缓冲区前有效。
    /// </summary>
    public ReadOnlySpan<char> WrittenSpan => _buffer.AsSpan(0, Length);

    /// <summary>
    /// 清空已写入内容并保留底层数组供后续帧复用。
    /// </summary>
    /// <returns>当前缓冲区，便于链式追加。</returns>
    public GuiTextBuffer Clear()
    {
        Length = 0;
        return this;
    }

    /// <summary>
    /// 追加一段文本。
    /// </summary>
    /// <param name="value">待追加文本。</param>
    /// <returns>当前缓冲区，便于链式追加。</returns>
    public GuiTextBuffer Append(ReadOnlySpan<char> value)
    {
        EnsureAdditionalCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(Length));
        Length += value.Length;
        return this;
    }

    /// <summary>
    /// 追加一个字符。
    /// </summary>
    /// <param name="value">待追加字符。</param>
    /// <returns>当前缓冲区，便于链式追加。</returns>
    public GuiTextBuffer Append(char value)
    {
        EnsureAdditionalCapacity(1);
        _buffer[Length++] = value;
        return this;
    }

    /// <summary>
    /// 使用 invariant culture 无分配地追加支持 <see cref="ISpanFormattable" /> 的值。
    /// </summary>
    /// <typeparam name="T">支持 span 格式化的值类型。</typeparam>
    /// <param name="value">待格式化值。</param>
    /// <param name="format">标准或自定义格式字符串；为空时使用类型默认格式。</param>
    /// <returns>当前缓冲区，便于链式追加。</returns>
    public GuiTextBuffer Append<T>(T value, ReadOnlySpan<char> format = default)
        where T : ISpanFormattable
    {
        int written;
        while (!value.TryFormat(
            _buffer.AsSpan(Length),
            out written,
            format,
            CultureInfo.InvariantCulture))
        {
            EnsureCapacity(checked(_buffer.Length + 1));
        }

        Length += written;
        return this;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return new string(WrittenSpan);
    }

    private void EnsureAdditionalCapacity(int additionalCapacity)
    {
        EnsureCapacity(checked(Length + additionalCapacity));
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _buffer.Length)
        {
            return;
        }

        int doubledCapacity = _buffer.Length <= Array.MaxLength / 2
            ? _buffer.Length * 2
            : Array.MaxLength;
        int newCapacity = Math.Max(requiredCapacity, doubledCapacity);
        Array.Resize(ref _buffer, newCapacity);
    }
}
