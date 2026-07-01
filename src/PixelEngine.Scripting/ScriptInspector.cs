using System.Reflection;

namespace PixelEngine.Scripting;

/// <summary>
/// 提供脚本组件的 Inspector 反射能力；仅用于编辑器/迭代路径，不进入帧热循环。
/// </summary>
public static class ScriptInspector
{
    /// <summary>
    /// 枚举指定脚本组件可在 Inspector 中显示和编辑的字段。
    /// </summary>
    /// <param name="behaviour">要检查的脚本组件实例。</param>
    /// <returns>字段描述数组，包含字段名、类型、当前值与可写性。</returns>
    public static ScriptFieldDescriptor[] InspectFields(Behaviour behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        Type type = behaviour.GetType();
        List<ScriptFieldDescriptor> descriptors = [];
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!ShouldShow(field))
            {
                continue;
            }

            descriptors.Add(new ScriptFieldDescriptor(
                field.Name,
                field.FieldType,
                field.GetValue(behaviour),
                !field.IsInitOnly,
                field.IsPublic,
                field.GetCustomAttribute<SerializeFieldAttribute>() is not null,
                Classify(field.FieldType),
                field.GetCustomAttribute<RangeAttribute>()?.Minimum,
                field.GetCustomAttribute<RangeAttribute>()?.Maximum));
        }

        return [.. descriptors];
    }

    /// <summary>
    /// 尝试把 Inspector 修改值写回脚本字段；调用方应在相位 1 使用。
    /// </summary>
    /// <param name="behaviour">目标脚本组件。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="value">要写入的值。</param>
    /// <returns>字段存在、可写且值类型兼容时返回 true。</returns>
    public static bool TrySetFieldValue(Behaviour behaviour, string fieldName, object? value)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        FieldInfo? field = behaviour.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is null || !ShouldShow(field) || field.IsInitOnly || !IsAssignable(field.FieldType, value))
        {
            return false;
        }

        field.SetValue(behaviour, value);
        return true;
    }

    private static bool ShouldShow(FieldInfo field)
    {
        return field.GetCustomAttribute<HideInInspectorAttribute>() is null
            && (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() is not null);
    }

    private static bool IsAssignable(Type targetType, object? value)
    {
        return value is null
            ? !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null
            : targetType.IsInstanceOfType(value);
    }

    private static ScriptFieldKind Classify(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        return target == typeof(bool)
            ? ScriptFieldKind.Boolean
            : target == typeof(string)
                ? ScriptFieldKind.String
                : target.IsEnum
                    ? ScriptFieldKind.Enum
                    : target == typeof(MaterialId)
                        ? ScriptFieldKind.Material
                        : ClassifyNonScalar(target);
    }

    private static ScriptFieldKind ClassifyNonScalar(Type target)
    {
        return target == typeof(System.Numerics.Vector2) ||
            target == typeof(System.Numerics.Vector3) ||
            target == typeof(System.Numerics.Vector4)
            ? ScriptFieldKind.Vector
            : IsNumeric(target) ? ScriptFieldKind.Number : ScriptFieldKind.Unsupported;
    }

    private static bool IsNumeric(Type type)
    {
        return type == typeof(byte) ||
            type == typeof(sbyte) ||
            type == typeof(short) ||
            type == typeof(ushort) ||
            type == typeof(int) ||
            type == typeof(uint) ||
            type == typeof(long) ||
            type == typeof(ulong) ||
            type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal);
    }
}

/// <summary>
/// Inspector 可编辑字段的编辑器类别。
/// </summary>
public enum ScriptFieldKind
{
    /// <summary>
    /// 当前 Editor 不支持的字段类型，仅展示文本。
    /// </summary>
    Unsupported,

    /// <summary>
    /// 布尔字段。
    /// </summary>
    Boolean,

    /// <summary>
    /// 数值字段。
    /// </summary>
    Number,

    /// <summary>
    /// 字符串字段。
    /// </summary>
    String,

    /// <summary>
    /// 枚举字段。
    /// </summary>
    Enum,

    /// <summary>
    /// System.Numerics 向量字段。
    /// </summary>
    Vector,

    /// <summary>
    /// PixelEngine 脚本材质引用字段。
    /// </summary>
    Material,
}

/// <summary>
/// 描述一个可供编辑器 Inspector 展示和编辑的脚本字段。
/// </summary>
/// <param name="Name">字段名。</param>
/// <param name="FieldType">字段运行时类型。</param>
/// <param name="Value">字段当前值。</param>
/// <param name="CanWrite">字段是否可写。</param>
/// <param name="IsPublic">字段是否为 public。</param>
/// <param name="IsSerializedPrivate">字段是否通过 SerializeField 暴露。</param>
/// <param name="Kind">字段在 Inspector 中的编辑器类别。</param>
/// <param name="RangeMinimum">字段范围滑条最小值；无范围时为 null。</param>
/// <param name="RangeMaximum">字段范围滑条最大值；无范围时为 null。</param>
public readonly record struct ScriptFieldDescriptor(
    string Name,
    Type FieldType,
    object? Value,
    bool CanWrite,
    bool IsPublic,
    bool IsSerializedPrivate,
    ScriptFieldKind Kind,
    double? RangeMinimum,
    double? RangeMaximum);
