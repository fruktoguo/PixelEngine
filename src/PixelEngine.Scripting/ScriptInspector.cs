using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PixelEngine.Scripting;

/// <summary>
/// 提供脚本组件的 Inspector 反射能力；仅用于编辑器/迭代路径，不进入帧热循环。
/// </summary>
public static class ScriptInspector
{
    /// <summary>
    /// 枚举指定脚本组件可在 Inspector 中显示和编辑的字段与 public 属性。
    /// </summary>
    /// <param name="behaviour">要检查的脚本组件实例。</param>
    /// <returns>成员描述数组，包含名称、类型、当前值与可写性。</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Inspector reflects over live runtime script Behaviour instances for editor display; it is not used by trimmed engine hot paths.")]
    public static ScriptFieldDescriptor[] InspectFields(Behaviour behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        Type type = behaviour.GetType();
        List<ScriptFieldDescriptor> descriptors = [];
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in type.GetFields(InstanceMembers))
        {
            if (!ShouldShow(field))
            {
                continue;
            }

            ScriptFieldKind kind = Classify(field, out ScriptAssetKind? assetKind);
            descriptors.Add(new ScriptFieldDescriptor(
                field.Name,
                field.FieldType,
                field.GetValue(behaviour),
                !field.IsInitOnly,
                field.IsPublic,
                field.GetCustomAttribute<SerializeFieldAttribute>() is not null,
                kind,
                field.GetCustomAttribute<RangeAttribute>()?.Minimum,
                field.GetCustomAttribute<RangeAttribute>()?.Maximum,
                assetKind));
        }

        foreach (PropertyInfo property in type.GetProperties(InstanceMembers))
        {
            if (!ShouldShow(property))
            {
                continue;
            }

            ScriptFieldKind kind = Classify(property, property.PropertyType, out ScriptAssetKind? assetKind);
            object? value;
            bool canWrite = property.SetMethod?.IsPublic == true;
            try
            {
                value = property.GetValue(behaviour);
            }
            catch (TargetInvocationException exception)
            {
                // 用户 property getter 属于脚本边界，可能依赖尚未 Attach 的 Entity/Context 或主动抛错。
                // Inspector 必须降级展示错误，不能让一个 getter 关闭整个 Editor。
                Exception cause = exception.InnerException ?? exception;
                value = $"<getter error: {cause.GetType().Name}: {cause.Message}>";
                canWrite = false;
                kind = ScriptFieldKind.Unsupported;
            }

            descriptors.Add(new ScriptFieldDescriptor(
                property.Name,
                property.PropertyType,
                value,
                canWrite,
                IsPublic: true,
                IsSerializedPrivate: false,
                kind,
                property.GetCustomAttribute<RangeAttribute>()?.Minimum,
                property.GetCustomAttribute<RangeAttribute>()?.Maximum,
                assetKind));
        }

        return [.. descriptors];
    }

    /// <summary>
    /// 尝试把 Inspector 修改值写回脚本字段或 public 属性；调用方应在相位安全 Editor 边界使用。
    /// </summary>
    /// <param name="behaviour">目标脚本组件。</param>
    /// <param name="fieldName">字段或属性名。</param>
    /// <param name="value">要写入的值。</param>
    /// <returns>成员存在、可写且值类型兼容时返回 true。</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Inspector field mutation targets live runtime script Behaviour instances selected by editor tooling.")]
    public static bool TrySetFieldValue(Behaviour behaviour, string fieldName, object? value)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        Type behaviourType = behaviour.GetType();
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo? property = behaviourType.GetProperty(fieldName, InstanceMembers);
        if (property is not null && ShouldShow(property) && property.SetMethod?.IsPublic == true)
        {
            if (!TryNormalizeAssignable(property.PropertyType, value, out object? normalizedProperty))
            {
                return false;
            }

            try
            {
                property.SetValue(behaviour, normalizedProperty);
                return true;
            }
            catch (TargetInvocationException)
            {
                // 与 getter 相同，用户 setter 也属于脚本隔离边界；拒绝本次写回而不终止 Editor。
                return false;
            }
        }

        FieldInfo? field = behaviourType.GetField(fieldName, InstanceMembers);
        if (field is null || !ShouldShow(field) || field.IsInitOnly ||
            !TryNormalizeAssignable(field.FieldType, value, out object? normalized))
        {
            return false;
        }

        field.SetValue(behaviour, normalized);
        return true;
    }

    private static bool ShouldShow(FieldInfo field)
    {
        return field.GetCustomAttribute<HideInInspectorAttribute>() is null
            && (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() is not null);
    }

    private static bool ShouldShow(PropertyInfo property)
    {
        MethodInfo? getter = property.GetMethod;
        return property.GetIndexParameters().Length == 0 &&
            property.DeclaringType != typeof(Behaviour) &&
            getter?.IsPublic == true &&
            !property.PropertyType.IsByRefLike &&
            !property.PropertyType.IsPointer &&
            !property.PropertyType.IsFunctionPointer &&
            !getter.ReturnParameter.ParameterType.IsByRef &&
            property.GetCustomAttribute<HideInInspectorAttribute>() is null;
    }

    private static bool TryNormalizeAssignable(Type memberType, object? value, out object? normalized)
    {
        Type targetType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (value is null)
        {
            normalized = null;
            return !memberType.IsValueType || Nullable.GetUnderlyingType(memberType) is not null;
        }

        if (targetType.IsInstanceOfType(value))
        {
            normalized = value;
            return true;
        }

        if (targetType == typeof(ScriptAssetReference) &&
            value is string encoded &&
            ScriptAssetReference.TryDecode(encoded, out ScriptAssetReference reference))
        {
            normalized = reference;
            return true;
        }

        if (targetType == typeof(string) && value is ScriptAssetReference assetReference)
        {
            normalized = assetReference.ToString();
            return true;
        }

        normalized = null;
        return false;
    }

    private static ScriptFieldKind Classify(FieldInfo field, out ScriptAssetKind? assetKind)
    {
        return Classify(field, field.FieldType, out assetKind);
    }

    private static ScriptFieldKind Classify(MemberInfo member, Type memberType, out ScriptAssetKind? assetKind)
    {
        AssetFieldAttribute? assetField = member.GetCustomAttribute<AssetFieldAttribute>();
        Type target = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (assetField is not null)
        {
            assetKind = assetField.AssetType;
            return target == typeof(string) || target == typeof(ScriptAssetReference)
                ? ScriptFieldKind.AssetReference
                : ScriptFieldKind.Unsupported;
        }

        assetKind = null;
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

    /// <summary>
    /// Project Window stable asset reference 字段。
    /// </summary>
    AssetReference,
}

/// <summary>
/// 描述一个可供编辑器 Inspector 展示和编辑的脚本字段或属性。
/// </summary>
/// <param name="Name">字段名。</param>
/// <param name="FieldType">成员运行时类型；保留 FieldType 名称以兼容既有 Editor API。</param>
/// <param name="Value">字段当前值。</param>
/// <param name="CanWrite">字段是否可写。</param>
/// <param name="IsPublic">字段是否为 public。</param>
/// <param name="IsSerializedPrivate">字段是否通过 SerializeField 暴露。</param>
/// <param name="Kind">字段在 Inspector 中的编辑器类别。</param>
/// <param name="RangeMinimum">字段范围滑条最小值；无范围时为 null。</param>
/// <param name="RangeMaximum">字段范围滑条最大值；无范围时为 null。</param>
/// <param name="AssetKind">typed asset reference 字段要求的资产类别；非资产字段为 null。</param>
public readonly record struct ScriptFieldDescriptor(
    string Name,
    Type FieldType,
    object? Value,
    bool CanWrite,
    bool IsPublic,
    bool IsSerializedPrivate,
    ScriptFieldKind Kind,
    double? RangeMinimum,
    double? RangeMaximum,
    ScriptAssetKind? AssetKind);
