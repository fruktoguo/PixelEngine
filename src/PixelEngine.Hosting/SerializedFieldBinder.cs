using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 统一 .scene / EditorShell 运行时投影的脚本 SerializedFields 绑定规则。
/// </summary>
public static class SerializedFieldBinder
{
    /// <summary>
    /// 按 Inspector 暴露规则把序列化字段绑定到脚本组件实例。
    /// </summary>
    /// <param name="component">目标脚本组件。</param>
    /// <param name="fields">字段名到序列化字符串值的映射。</param>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Serialized field binding is an editor/script content boundary over live Behaviour instances, not a trimmed engine hot path.")]
    public static void Bind(IComponent component, IReadOnlyDictionary<string, string>? fields)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (fields is null || fields.Count == 0)
        {
            return;
        }

        Type type = component.GetType();
        foreach (KeyValuePair<string, string> field in fields)
        {
            if (!TryResolveBindableMember(type, field.Key, out SerializedFieldMember member))
            {
                throw new InvalidOperationException($"{type.FullName} 不存在可写公开属性、公开字段或 [SerializeField] non-public 字段：{field.Key}。");
            }

            member.SetValue(component, ConvertValue(field.Value, member.MemberType));
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Serialized field binding reflects over live script Behaviour instances that are outside the trimmed engine closure.")]
    private static bool TryResolveBindableMember(Type type, string name, out SerializedFieldMember member)
    {
        const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        PropertyInfo? property = type.GetProperty(name, PublicInstance);
        if (property is not null && CanBind(property))
        {
            member = SerializedFieldMember.ForProperty(property);
            return true;
        }

        FieldInfo? field = type.GetField(name, PublicInstance | BindingFlags.NonPublic);
        if (field is not null && CanBind(field))
        {
            member = SerializedFieldMember.ForField(field);
            return true;
        }

        member = default;
        return false;
    }

    private static bool CanBind(PropertyInfo property)
    {
        return property.GetCustomAttribute<HideInInspectorAttribute>() is null &&
            property.GetMethod?.IsPublic == true &&
            property.SetMethod?.IsPublic == true;
    }

    private static bool CanBind(FieldInfo field)
    {
        return field.GetCustomAttribute<HideInInspectorAttribute>() is null &&
            !field.IsInitOnly &&
            (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() is not null);
    }

    /// <summary>按与 Scene/runtime projection 相同的规则解析单个 Inspector 序列化值。</summary>
    /// <param name="value">序列化字符串。</param>
    /// <param name="targetType">目标字段或属性类型。</param>
    /// <returns>可赋给目标类型的规范值。</returns>
    public static object? ConvertValue(string value, Type targetType)
    {
        Type normalized = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return string.IsNullOrEmpty(value) && Nullable.GetUnderlyingType(targetType) is not null
            ? null
            : normalized switch
            {
                _ when normalized == typeof(string) => value,
                _ when normalized == typeof(byte) => byte.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(sbyte) => sbyte.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(short) => short.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(ushort) => ushort.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(int) => int.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(uint) => uint.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(long) => long.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(ulong) => ulong.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(float) => float.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(double) => double.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(decimal) => decimal.Parse(value, CultureInfo.InvariantCulture),
                _ when normalized == typeof(bool) => bool.Parse(value),
                _ when normalized == typeof(MaterialId) => new MaterialId(ushort.Parse(value, CultureInfo.InvariantCulture)),
                _ when normalized == typeof(ScriptAssetReference) && ScriptAssetReference.TryDecode(value, out ScriptAssetReference reference) => reference,
                _ when normalized == typeof(Vector2) => ParseVector2(value),
                _ when normalized == typeof(Vector3) => ParseVector3(value),
                _ when normalized == typeof(Vector4) => ParseVector4(value),
                _ when normalized.IsEnum => Enum.Parse(normalized, value, ignoreCase: true),
                _ => throw new NotSupportedException($"不支持绑定字段类型：{targetType.FullName}。"),
            };
    }

    private static Vector2 ParseVector2(string value)
    {
        return SerializedFieldValueCodec.TryParseVector2(value, out Vector2 parsed)
            ? parsed
            : throw new FormatException($"Vector2 字段必须使用有限数值的 \"x,y\" 格式：{value}");
    }

    private static Vector3 ParseVector3(string value)
    {
        return SerializedFieldValueCodec.TryParseVector3(value, out Vector3 parsed)
            ? parsed
            : throw new FormatException($"Vector3 字段必须使用有限数值的 \"x,y,z\" 格式：{value}");
    }

    private static Vector4 ParseVector4(string value)
    {
        return SerializedFieldValueCodec.TryParseVector4(value, out Vector4 parsed)
            ? parsed
            : throw new FormatException($"Vector4 字段必须使用有限数值的 \"x,y,z,w\" 格式：{value}");
    }

    private readonly struct SerializedFieldMember
    {
        private readonly PropertyInfo? _property;
        private readonly FieldInfo? _field;

        private SerializedFieldMember(PropertyInfo? property, FieldInfo? field, Type memberType)
        {
            _property = property;
            _field = field;
            MemberType = memberType;
        }

        public Type MemberType { get; }

        public static SerializedFieldMember ForProperty(PropertyInfo property)
        {
            return new SerializedFieldMember(property, field: null, property.PropertyType);
        }

        public static SerializedFieldMember ForField(FieldInfo field)
        {
            return new SerializedFieldMember(property: null, field, field.FieldType);
        }

        public void SetValue(object instance, object? value)
        {
            if (_property is not null)
            {
                _property.SetValue(instance, value);
                return;
            }

            _field!.SetValue(instance, value);
        }
    }
}
