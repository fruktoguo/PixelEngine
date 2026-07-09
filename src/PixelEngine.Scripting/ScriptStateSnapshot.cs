using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PixelEngine.Scripting;

/// <summary>
/// Behaviour 可持久化字段/属性的快照；热重载时用于状态迁移。
/// </summary>
internal sealed class ScriptStateSnapshot
{
    private readonly Dictionary<string, object?> _values = [];

    /// <summary>
    /// 快照中已捕获的成员数量。
    /// </summary>
    public int Count => _values.Count;

    /// <summary>
    /// 从 Behaviour 实例反射捕获应持久化的字段与属性值。
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Hot reload state capture reflects over live runtime script Behaviour fields/properties, outside trimmed engine hot paths.")]
    public static ScriptStateSnapshot Capture(Behaviour behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        ScriptStateSnapshot snapshot = new();
        Type type = behaviour.GetType();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!ShouldPersist(field) || !IsSupportedValueType(field.FieldType))
            {
                continue;
            }

            snapshot._values[Key(field.Name, field.FieldType)] = field.GetValue(behaviour);
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            // 捕获阶段要求可读可写，避免只读计算属性进入快照。
            if (!ShouldPersist(property) || !IsSupportedValueType(property.PropertyType) || property.GetMethod is null || property.SetMethod is null)
            {
                continue;
            }

            snapshot._values[Key(property.Name, property.PropertyType)] = property.GetValue(behaviour);
        }

        return snapshot;
    }

    /// <summary>
    /// 将快照值写回新 Behaviour 实例的同名字段/属性。
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Hot reload state restore reflects over live runtime script Behaviour fields/properties, outside trimmed engine hot paths.")]
    public void Restore(Behaviour behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        Type type = behaviour.GetType();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!ShouldPersist(field) || !IsSupportedValueType(field.FieldType))
            {
                continue;
            }

            if (_values.TryGetValue(Key(field.Name, field.FieldType), out object? value))
            {
                field.SetValue(behaviour, value);
            }
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!ShouldPersist(property) || !IsSupportedValueType(property.PropertyType) || property.SetMethod is null)
            {
                continue;
            }

            if (_values.TryGetValue(Key(property.Name, property.PropertyType), out object? value))
            {
                property.SetValue(behaviour, value);
            }
        }
    }

    /// <summary>
    /// 判断成员是否应参与热重载状态迁移。
    /// </summary>
    private static bool ShouldPersist(MemberInfo member)
    {
        return member.GetCustomAttribute<HideInInspectorAttribute>() is null
            && (member.GetCustomAttribute<PersistAttribute>() is not null || IsPublic(member));
    }

    private static bool IsPublic(MemberInfo member)
    {
        return member switch
        {
            FieldInfo field => field.IsPublic,
            PropertyInfo property => property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true,
            _ => false,
        };
    }

    /// <summary>
    /// 仅支持基元、枚举、字符串、decimal 与 ScriptAssetReference 等可安全装箱的类型。
    /// </summary>
    private static bool IsSupportedValueType(Type type)
    {
        return type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(ScriptAssetReference);
    }

    /// <summary>
    /// 以「名称:类型全名」作为键，区分同名不同类型的成员。
    /// </summary>
    private static string Key(string name, Type type)
    {
        return $"{name}:{type.FullName}";
    }
}
