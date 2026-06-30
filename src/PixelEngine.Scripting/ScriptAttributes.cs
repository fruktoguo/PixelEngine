namespace PixelEngine.Scripting;

/// <summary>
/// 标记私有字段可被 Inspector 序列化与编辑。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeFieldAttribute : Attribute
{
}

/// <summary>
/// 标记字段在脚本热重载时应尝试保留状态。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PersistAttribute : Attribute
{
}

/// <summary>
/// 标记字段不在 Inspector 中显示。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute
{
}

/// <summary>
/// 标记类型是可被脚本运行时发现和挂载的脚本组件。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ScriptComponentAttribute : Attribute
{
}
