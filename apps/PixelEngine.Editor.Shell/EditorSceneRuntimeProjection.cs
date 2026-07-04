using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorSceneRuntimeProjection
{
    private readonly Dictionary<int, int> _stableToRuntime = [];

    private EditorSceneRuntimeProjection(PixelEngine.Scripting.Scene scene)
    {
        Scene = scene;
    }

    public PixelEngine.Scripting.Scene Scene { get; }

    public IReadOnlyDictionary<int, int> StableIdToEntityId => _stableToRuntime;

    public bool TryGetRuntimeEntityId(int stableId, out int entityId)
    {
        return _stableToRuntime.TryGetValue(stableId, out entityId);
    }

    public static EditorSceneRuntimeProjection Build(EditorSceneModel model, ScriptAssemblyRegistry scriptAssemblies)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(scriptAssemblies);
        EditorSceneRuntimeProjection projection = new(new PixelEngine.Scripting.Scene());
        foreach (EditorGameObject gameObject in model.EnumerateDepthFirst())
        {
            Entity entity = projection.Scene.CreateEntity();
            projection._stableToRuntime.Add(gameObject.StableId, entity.Id);
            Transform transform = entity.AddComponent<Transform>();
            ApplyTransform(transform, model.ComputeWorldTransform(gameObject.StableId));
            for (int i = 0; i < gameObject.Components.Count; i++)
            {
                EditorComponentModel component = gameObject.Components[i];
                Type type = ResolveBehaviourType(component.TypeName, scriptAssemblies);
                IComponent runtimeComponent = entity.AddComponent(type);
                BindSerializedFields(runtimeComponent, component.SerializedFields);
            }
        }

        return projection;
    }

    private static void ApplyTransform(Transform transform, EditorSceneTransform source)
    {
        transform.SetPosition(source.X, source.Y);
        transform.RotationRadians = source.RotationRadians;
        transform.ScaleX = source.ScaleX;
        transform.ScaleY = source.ScaleY;
    }

    private static Type ResolveBehaviourType(string typeName, ScriptAssemblyRegistry scriptAssemblies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        for (int i = 0; i < scriptAssemblies.Assemblies.Count; i++)
        {
            Type? type = scriptAssemblies.Assemblies[i].GetType(typeName, throwOnError: false);
            if (IsConcreteBehaviour(type))
            {
                return type!;
            }

            foreach (Type candidate in scriptAssemblies.Assemblies[i].GetTypes())
            {
                if (candidate.Name == typeName && IsConcreteBehaviour(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException($"未找到 Behaviour 类型：{typeName}。");
    }

    private static bool IsConcreteBehaviour(Type? type)
    {
        return type is not null &&
            !type.IsAbstract &&
            typeof(Behaviour).IsAssignableFrom(type) &&
            type.GetConstructor(Type.EmptyTypes) is not null;
    }

    private static void BindSerializedFields(IComponent component, SortedDictionary<string, string> fields)
    {
        if (fields.Count == 0)
        {
            return;
        }

        Type type = component.GetType();
        foreach (KeyValuePair<string, string> field in fields)
        {
            System.Reflection.PropertyInfo? property = type.GetProperty(field.Key, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (property is { CanWrite: true })
            {
                property.SetValue(component, ConvertValue(field.Value, property.PropertyType));
                continue;
            }

            System.Reflection.FieldInfo? fieldInfo = type.GetField(field.Key, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (fieldInfo is not null)
            {
                fieldInfo.SetValue(component, ConvertValue(field.Value, fieldInfo.FieldType));
                continue;
            }

            throw new InvalidOperationException($"{type.FullName} 不存在可写公开字段或属性：{field.Key}。");
        }
    }

    private static object ConvertValue(string value, Type targetType)
    {
        return targetType switch
        {
            _ when targetType == typeof(string) => value,
            _ when targetType == typeof(int) => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            _ when targetType == typeof(long) => long.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            _ when targetType == typeof(float) => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            _ when targetType == typeof(double) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            _ when targetType == typeof(bool) => bool.Parse(value),
            _ when targetType == typeof(ushort) => ushort.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            _ when targetType == typeof(MaterialId) => new MaterialId(ushort.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
            _ when targetType == typeof(System.Numerics.Vector2) => ParseVector2(value),
            _ when targetType.IsEnum => Enum.Parse(targetType, value, ignoreCase: true),
            _ => throw new NotSupportedException($"不支持绑定字段类型：{targetType.FullName}。"),
        };
    }

    private static System.Numerics.Vector2 ParseVector2(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? new System.Numerics.Vector2(
                float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture))
            : throw new FormatException($"Vector2 字段必须使用 \"x,y\" 格式：{value}");
    }
}
