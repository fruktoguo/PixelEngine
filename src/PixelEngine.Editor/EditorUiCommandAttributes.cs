namespace PixelEngine.Editor;

/// <summary>
/// 声明一个 production Editor UI 类型拥有的稳定人工操作 surface。
/// automation 能力矩阵使用该身份，不依赖类型名、窗口标题或本地化文本。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorUiSurfaceAttribute : Attribute
{
    /// <summary>创建稳定 UI surface 声明。</summary>
    /// <param name="id">1..128 字符的 ASCII semantic ID。</param>
    public EditorUiSurfaceAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    /// <summary>稳定 UI surface ID。</summary>
    public string Id { get; }
}

/// <summary>
/// 把 production Editor UI 方法实际提供的人工操作登记为稳定 command IDs。
/// 每个 ID 必须由能力矩阵验证器双向映射到至少一个真实 semantic capability。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class EditorUiCommandsAttribute : Attribute
{
    /// <summary>声明方法实际提供的一组 UI commands。</summary>
    /// <param name="commandIds">稳定 UI command IDs。</param>
    public EditorUiCommandsAttribute(params string[] commandIds)
    {
        ArgumentNullException.ThrowIfNull(commandIds);
        CommandIds = [.. commandIds];
    }

    /// <summary>该 production method 实际提供的 UI command IDs。</summary>
    public string[] CommandIds { get; }
}

/// <summary>
/// 标记只封装 ImGui 输入或把输入转发到已登记语义、但不拥有独立 command ID 的低级 helper。
/// 实际人工动作必须由调用方或被调用 semantic method 的 <see cref="EditorUiCommandsAttribute"/> 声明。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EditorUiControlPrimitiveAttribute : Attribute;
