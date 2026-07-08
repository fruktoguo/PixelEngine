using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PixelEngine.Scripting;

internal sealed class ScriptStateSnapshot
{
    private readonly Dictionary<string, object?> _values = [];

    public int Count => _values.Count;

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
            if (!ShouldPersist(property) || !IsSupportedValueType(property.PropertyType) || property.GetMethod is null || property.SetMethod is null)
            {
                continue;
            }

            snapshot._values[Key(property.Name, property.PropertyType)] = property.GetValue(behaviour);
        }

        return snapshot;
    }

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

    private static bool IsSupportedValueType(Type type)
    {
        return type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(ScriptAssetReference);
    }

    private static string Key(string name, Type type)
    {
        return $"{name}:{type.FullName}";
    }
}
