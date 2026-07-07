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
        SerializedFieldBinder.Bind(component, fields);
    }
}
