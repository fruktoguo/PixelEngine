using System.Runtime.InteropServices;

namespace PixelEngine.Scripting;

/// <summary>
/// 脚本可见的游戏大 UI 服务契约；实现由 Hosting 桥接到 PixelEngine.UI。
/// </summary>
public interface IGameUiService
{
    /// <summary>
    /// UI 事件通知；事件由宿主在相位 1 派发。
    /// </summary>
    event Action<UiEvent>? UiEventRaised;

    /// <summary>
    /// 显示一个普通 UI 屏幕。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id 或内容 id。</param>
    /// <returns>可见屏幕实例句柄。</returns>
    UiScreenHandle ShowScreen(string screenId);

    /// <summary>
    /// 隐藏一个可见 UI 屏幕。
    /// </summary>
    /// <param name="screen">屏幕实例句柄。</param>
    void HideScreen(UiScreenHandle screen);

    /// <summary>
    /// 压入一个模态 UI 屏幕。
    /// </summary>
    /// <param name="screenId">稳定屏幕 id 或内容 id。</param>
    /// <returns>可见屏幕实例句柄。</returns>
    UiScreenHandle PushModal(string screenId);

    /// <summary>
    /// 把脚本模型绑定到指定屏幕。
    /// </summary>
    /// <param name="screen">屏幕实例句柄。</param>
    /// <param name="modelName">稳定模型名句柄。</param>
    /// <param name="model">模型读取接口。</param>
    void BindModel(UiScreenHandle screen, UiModelName modelName, IUiModel model);

    /// <summary>
    /// 把托管字符串登记到 UI 字符串池并返回可写入模型的稳定句柄。
    /// </summary>
    /// <param name="value">要显示的文本。</param>
    /// <returns>字符串池句柄。</returns>
    UiStringHandle InternString(string value);

    /// <summary>
    /// 向 UI 写入一个模型值。
    /// </summary>
    /// <param name="screen">屏幕实例句柄。</param>
    /// <param name="path">稳定模型路径句柄。</param>
    /// <param name="value">写入值。</param>
    void SetValue(UiScreenHandle screen, UiPathId path, in UiValue value);

    /// <summary>
    /// 从 UI 读取一个模型值。
    /// </summary>
    /// <param name="screen">屏幕实例句柄。</param>
    /// <param name="path">稳定模型路径句柄。</param>
    /// <param name="value">读出值。</param>
    /// <returns>读取成功则返回 true。</returns>
    bool TryGetValue(UiScreenHandle screen, UiPathId path, out UiValue value);

    /// <summary>
    /// 调用 UI 屏幕上的动作。
    /// </summary>
    /// <param name="screen">屏幕实例句柄。</param>
    /// <param name="action">稳定动作 id。</param>
    /// <param name="payload">动作载荷。</param>
    void Invoke(UiScreenHandle screen, UiActionId action, in UiValue payload);
}

/// <summary>
/// 禁用游戏大 UI 时注入脚本上下文的空服务；所有写入静默丢弃，所有读取返回失败。
/// </summary>
public sealed class NoopGameUiService : IGameUiService
{
    /// <summary>
    /// 全局空服务实例。
    /// </summary>
    public static NoopGameUiService Instance { get; } = new();

    private NoopGameUiService()
    {
    }

    /// <inheritdoc />
    public event Action<UiEvent>? UiEventRaised
    {
        add => _ = value;
        remove => _ = value;
    }

    /// <inheritdoc />
    public UiScreenHandle ShowScreen(string screenId)
    {
        _ = screenId;
        return default;
    }

    /// <inheritdoc />
    public void HideScreen(UiScreenHandle screen)
    {
        _ = screen;
    }

    /// <inheritdoc />
    public UiScreenHandle PushModal(string screenId)
    {
        _ = screenId;
        return default;
    }

    /// <inheritdoc />
    public void BindModel(UiScreenHandle screen, UiModelName modelName, IUiModel model)
    {
        _ = screen;
        _ = modelName;
        _ = model;
    }

    /// <inheritdoc />
    public UiStringHandle InternString(string value)
    {
        _ = value;
        return default;
    }

    /// <inheritdoc />
    public void SetValue(UiScreenHandle screen, UiPathId path, in UiValue value)
    {
        _ = screen;
        _ = path;
        _ = value;
    }

    /// <inheritdoc />
    public bool TryGetValue(UiScreenHandle screen, UiPathId path, out UiValue value)
    {
        _ = screen;
        _ = path;
        value = default;
        return false;
    }

    /// <inheritdoc />
    public void Invoke(UiScreenHandle screen, UiActionId action, in UiValue payload)
    {
        _ = screen;
        _ = action;
        _ = payload;
    }
}

/// <summary>
/// 脚本可绑定给 UI 的只读模型。
/// </summary>
public interface IUiModel
{
    /// <summary>
    /// 尝试读取模型路径对应的值。
    /// </summary>
    /// <param name="path">稳定模型路径句柄。</param>
    /// <param name="value">读出值。</param>
    /// <returns>读取成功则返回 true。</returns>
    bool TryGetValue(UiPathId path, out UiValue value);
}

/// <summary>
/// 脚本可见的 UI 到游戏事件。
/// </summary>
/// <param name="Screen">来源屏幕实例。</param>
/// <param name="Element">来源元素 id。</param>
/// <param name="Action">动作 id。</param>
/// <param name="Payload">事件载荷。</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct UiEvent(
    UiScreenHandle Screen,
    UiElementId Element,
    UiActionId Action,
    UiValue Payload);

/// <summary>
/// 可见 UI 屏幕实例句柄。
/// </summary>
/// <param name="Value">正整数句柄值。</param>
public readonly record struct UiScreenHandle(int Value);

/// <summary>
/// UI 元素 id。
/// </summary>
/// <param name="Value">稳定 hash 或句柄。</param>
public readonly record struct UiElementId(int Value);

/// <summary>
/// UI 动作 id。
/// </summary>
/// <param name="Value">稳定 hash 或句柄。</param>
public readonly record struct UiActionId(int Value);

/// <summary>
/// UI 模型路径 id。
/// </summary>
/// <param name="Value">稳定 hash 或句柄。</param>
public readonly record struct UiPathId(int Value);

/// <summary>
/// UI 模型名 id。
/// </summary>
/// <param name="Value">稳定 hash 或句柄。</param>
public readonly record struct UiModelName(int Value);

/// <summary>
/// UI 字符串池句柄。
/// </summary>
/// <param name="Value">字符串池索引。</param>
public readonly record struct UiStringHandle(int Value);

/// <summary>
/// 脚本 UI 数据桥使用的 blittable 联合值。
/// </summary>
#pragma warning disable IDE0022, IDE0024, IDE0032
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

    /// <inheritdoc />
    public bool Equals(UiValue other) => _kind == other._kind && _integer == other._integer;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is UiValue other && Equals(other);

    /// <inheritdoc />
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

/// <summary>
/// 脚本 UI 值类型。
/// </summary>
public enum UiValueKind : byte
{
    /// <summary>
    /// 空值。
    /// </summary>
    Empty = 0,

    /// <summary>
    /// 布尔值。
    /// </summary>
    Boolean = 1,

    /// <summary>
    /// 64 位整数。
    /// </summary>
    Int64 = 2,

    /// <summary>
    /// 双精度浮点数。
    /// </summary>
    Double = 3,

    /// <summary>
    /// 字符串池句柄。
    /// </summary>
    StringHandle = 4,
}
