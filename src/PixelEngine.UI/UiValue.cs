using System.Runtime.InteropServices;

namespace PixelEngine.UI;

#pragma warning disable IDE0022, IDE0024, IDE0032

/// <summary>
/// UI 数据桥使用的 blittable 联合值。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct UiValue : IEquatable<UiValue>
{
    [FieldOffset(0)]
    private readonly UiValueKind _kind;

    [FieldOffset(8)]
    private readonly long _integer;

    [FieldOffset(8)]
    private readonly double _number;

    /// <summary>
    /// 创建整数值。
    /// </summary>
    /// <param name="value">整数。</param>
    public UiValue(long value)
    {
        _number = default;
        _kind = UiValueKind.Int64;
        _integer = value;
    }

    /// <summary>
    /// 创建浮点值。
    /// </summary>
    /// <param name="value">浮点数。</param>
    public UiValue(double value)
    {
        _integer = default;
        _kind = UiValueKind.Double;
        _number = value;
    }

    private UiValue(UiValueKind kind, long integer)
    {
        _number = default;
        _kind = kind;
        _integer = integer;
    }

    /// <summary>
    /// 值类型。
    /// </summary>
    public UiValueKind Kind => _kind;

    /// <summary>
    /// 创建布尔值。
    /// </summary>
    /// <param name="value">布尔值。</param>
    /// <returns>UI 值。</returns>
    public static UiValue FromBoolean(bool value) => new(UiValueKind.Boolean, value ? 1 : 0);

    /// <summary>
    /// 创建字符串句柄值。
    /// </summary>
    /// <param name="handle">字符串池句柄。</param>
    /// <returns>UI 值。</returns>
    public static UiValue FromStringHandle(UiStringHandle handle) => new(UiValueKind.StringHandle, handle.Value);

    /// <summary>
    /// 读取整数。
    /// </summary>
    /// <returns>整数。</returns>
    public long AsInt64()
    {
        EnsureKind(UiValueKind.Int64);
        return _integer;
    }

    /// <summary>
    /// 读取浮点数。
    /// </summary>
    /// <returns>浮点数。</returns>
    public double AsDouble()
    {
        EnsureKind(UiValueKind.Double);
        return _number;
    }

    /// <summary>
    /// 读取布尔值。
    /// </summary>
    /// <returns>布尔值。</returns>
    public bool AsBoolean()
    {
        EnsureKind(UiValueKind.Boolean);
        return _integer != 0;
    }

    /// <summary>
    /// 读取字符串句柄。
    /// </summary>
    /// <returns>字符串池句柄。</returns>
    public UiStringHandle AsStringHandle()
    {
        EnsureKind(UiValueKind.StringHandle);
        return new UiStringHandle(checked((int)_integer));
    }

    /// <summary>
    /// 比较两个 UI 值是否类型与载荷完全一致。
    /// </summary>
    /// <param name="other">另一个 UI 值。</param>
    /// <returns>相等则返回 true。</returns>
    public bool Equals(UiValue other) => _kind == other._kind && _integer == other._integer;

    /// <summary>
    /// 比较对象是否为相同载荷的 UI 值。
    /// </summary>
    /// <param name="obj">待比较对象。</param>
    /// <returns>相等则返回 true。</returns>
    public override bool Equals(object? obj) => obj is UiValue other && Equals(other);

    /// <summary>
    /// 返回 UI 值的哈希码。
    /// </summary>
    /// <returns>哈希码。</returns>
    public override int GetHashCode() => HashCode.Combine(_kind, _integer);

    /// <summary>
    /// 相等运算。
    /// </summary>
    public static bool operator ==(UiValue left, UiValue right) => left.Equals(right);

    /// <summary>
    /// 不等运算。
    /// </summary>
    public static bool operator !=(UiValue left, UiValue right) => !left.Equals(right);

    private void EnsureKind(UiValueKind expected)
    {
        if (_kind != expected)
        {
            throw new InvalidOperationException($"UI 值类型为 {_kind}，不能按 {expected} 读取。");
        }
    }
}
#pragma warning restore IDE0022, IDE0024, IDE0032
