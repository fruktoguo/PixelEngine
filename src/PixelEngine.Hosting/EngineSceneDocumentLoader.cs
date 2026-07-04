using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// .scene 文档加载器，负责把脚本实体与 Behaviour 组件物化到 Scripting.Scene。
/// </summary>
public static class EngineSceneDocumentLoader
{
    /// <summary>
    /// 当前支持的 .scene 格式版本。
    /// </summary>
    public const int CurrentFormatVersion = 2;

    /// <summary>
    /// 读取并验证 .scene JSON 文档，不物化脚本实体。
    /// </summary>
    /// <param name="path">.scene 文件路径。</param>
    /// <returns>解析后的场景文档。</returns>
    public static EngineSceneDocument LoadDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        EngineSceneDocument document = JsonSerializer.Deserialize(
                json,
                EngineSceneJsonContext.Default.EngineSceneDocument) ??
            throw new JsonException(".scene 文件为空或格式无效。");
        ValidateFormat(document);
        return document;
    }

    /// <summary>
    /// 以稳定顺序写出 .scene JSON 文档。
    /// </summary>
    /// <param name="document">待写出的场景文档。</param>
    /// <param name="path">目标 .scene 文件路径。</param>
    public static void SaveDocument(EngineSceneDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EngineSceneDocument normalized = NormalizeForSave(document);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            normalized,
            EngineSceneJsonContext.Default.EngineSceneDocument);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 从 .scene 文件加载脚本实体场景。
    /// </summary>
    /// <param name="path">.scene 文件路径。</param>
    /// <param name="scriptAssemblies">已注册脚本程序集。</param>
    /// <returns>物化后的脚本场景。</returns>
    public static PixelEngine.Scripting.Scene Load(string path, ScriptAssemblyRegistry scriptAssemblies)
    {
        ArgumentNullException.ThrowIfNull(scriptAssemblies);
        return Build(LoadDocument(path), scriptAssemblies);
    }

    /// <summary>
    /// 从已解析文档加载脚本实体场景。
    /// </summary>
    /// <param name="document">场景文档。</param>
    /// <param name="scriptAssemblies">已注册脚本程序集。</param>
    /// <returns>物化后的脚本场景。</returns>
    public static PixelEngine.Scripting.Scene Build(EngineSceneDocument document, ScriptAssemblyRegistry scriptAssemblies)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scriptAssemblies);
        ValidateFormat(document);
        ValidateSceneGraph(document);

        PixelEngine.Scripting.Scene scene = new();
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        Dictionary<int, EngineSceneTransformDocument> transformsByStableId = new(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = scene.CreateEntity();
            Transform transform = entity.AddComponent<Transform>();
            EngineSceneTransformDocument worldTransform = ResolveWorldTransform(entities, i, transformsByStableId, []);
            ApplyTransform(transform, worldTransform);
            transformsByStableId[entities[i].StableId] = worldTransform;
            EngineSceneBehaviourDocument[] behaviours = entities[i].Behaviours ?? [];
            for (int j = 0; j < behaviours.Length; j++)
            {
                Type behaviourType = ResolveBehaviourType(behaviours[j], scriptAssemblies);
                IComponent component = entity.AddComponent(behaviourType);
                BindSerializedFields(component, behaviours[j].SerializedFields);
            }
        }

        return scene;
    }

    private static void ValidateFormat(EngineSceneDocument document)
    {
        if (document.FormatVersion is < 1 or > CurrentFormatVersion)
        {
            throw new NotSupportedException($"不支持的 .scene 格式版本：{document.FormatVersion}。");
        }
    }

    private static void ValidateSceneGraph(EngineSceneDocument document)
    {
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        HashSet<int> stableIds = new(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i].StableId <= 0)
            {
                throw new InvalidOperationException($".scene 实体 stableId 必须为正数：{entities[i].StableId}。");
            }

            if (!stableIds.Add(entities[i].StableId))
            {
                throw new InvalidOperationException($".scene 包含重复实体 stableId：{entities[i].StableId}。");
            }
        }
    }

    private static EngineSceneDocument NormalizeForSave(EngineSceneDocument document)
    {
        ValidateFormat(document);
        ValidateSceneGraph(document);
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        EngineSceneEntityDocument[] normalizedEntities = new EngineSceneEntityDocument[entities.Length];
        EngineSceneEntityDocument[] sorted = [.. entities.OrderBy(static entity => entity.StableId)];
        for (int i = 0; i < sorted.Length; i++)
        {
            EngineSceneBehaviourDocument[] behaviours = sorted[i].Behaviours ?? [];
            EngineSceneBehaviourDocument[] normalizedBehaviours = new EngineSceneBehaviourDocument[behaviours.Length];
            for (int j = 0; j < behaviours.Length; j++)
            {
                normalizedBehaviours[j] = new EngineSceneBehaviourDocument
                {
                    TypeName = behaviours[j].TypeName,
                    SerializedFields = NormalizeFields(behaviours[j].SerializedFields),
                };
            }

            normalizedEntities[i] = new EngineSceneEntityDocument
            {
                StableId = sorted[i].StableId,
                Name = sorted[i].Name,
                ParentId = sorted[i].ParentId,
                Transform = sorted[i].Transform ?? new EngineSceneTransformDocument(),
                Behaviours = normalizedBehaviours,
            };
        }

        return new EngineSceneDocument
        {
            FormatVersion = CurrentFormatVersion,
            Name = document.Name,
            InitialSaveDirectory = document.InitialSaveDirectory,
            Entities = normalizedEntities,
        };
    }

    private static Dictionary<string, string>? NormalizeFields(Dictionary<string, string>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return fields;
        }

        Dictionary<string, string> normalized = new(fields.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> field in fields.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            normalized.Add(field.Key, field.Value);
        }

        return normalized;
    }

    private static EngineSceneTransformDocument ResolveWorldTransform(
        EngineSceneEntityDocument[] entities,
        int index,
        Dictionary<int, EngineSceneTransformDocument> resolved,
        HashSet<int> resolving)
    {
        EngineSceneEntityDocument entity = entities[index];
        EngineSceneTransformDocument local = entity.Transform ?? new EngineSceneTransformDocument();
        if (!resolving.Add(entity.StableId))
        {
            throw new InvalidOperationException($".scene 实体层级包含循环引用：{entity.StableId}。");
        }

        if (entity.ParentId is null)
        {
            _ = resolving.Remove(entity.StableId);
            return local;
        }

        int parentIndex = FindEntityIndex(entities, entity.ParentId.Value);
        if (parentIndex < 0)
        {
            throw new InvalidOperationException($".scene 实体 {entity.StableId} 引用了不存在的父实体 {entity.ParentId.Value}。");
        }

        EngineSceneTransformDocument parent = resolved.TryGetValue(entity.ParentId.Value, out EngineSceneTransformDocument? cachedParent)
            ? cachedParent
            : ResolveWorldTransform(entities, parentIndex, resolved, resolving);
        EngineSceneTransformDocument world = Compose(parent, local);
        _ = resolving.Remove(entity.StableId);
        return world;
    }

    private static EngineSceneTransformDocument Compose(EngineSceneTransformDocument parent, EngineSceneTransformDocument local)
    {
        float scaledX = local.X * parent.ScaleX;
        float scaledY = local.Y * parent.ScaleY;
        float cos = MathF.Cos(parent.RotationRadians);
        float sin = MathF.Sin(parent.RotationRadians);
        return new EngineSceneTransformDocument
        {
            X = parent.X + (scaledX * cos) - (scaledY * sin),
            Y = parent.Y + (scaledX * sin) + (scaledY * cos),
            RotationRadians = parent.RotationRadians + local.RotationRadians,
            ScaleX = parent.ScaleX * local.ScaleX,
            ScaleY = parent.ScaleY * local.ScaleY,
        };
    }

    private static int FindEntityIndex(EngineSceneEntityDocument[] entities, int stableId)
    {
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i].StableId == stableId)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ApplyTransform(Transform transform, EngineSceneTransformDocument document)
    {
        transform.SetPosition(document.X, document.Y);
        transform.RotationRadians = document.RotationRadians;
        transform.ScaleX = document.ScaleX;
        transform.ScaleY = document.ScaleY;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = ".scene Behaviour types are resolved from runtime script assemblies; they are not discovered through trimmed engine metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = ".scene type names are user content and may target runtime script assemblies, so static trimmer validation is intentionally not applicable.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = ".scene Behaviour constructors live in runtime script assemblies and cannot be described by the trimmed engine closure.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2073",
        Justification = ".scene returns Behaviour Type values from runtime script assemblies after validation; the trimmer cannot statically model those assemblies.")]
    private static Type ResolveBehaviourType(
        EngineSceneBehaviourDocument behaviour,
        ScriptAssemblyRegistry scriptAssemblies)
    {
        string typeName = string.IsNullOrWhiteSpace(behaviour.TypeName)
            ? throw new ArgumentException(".scene Behaviour 缺少 TypeName。", nameof(behaviour))
            : behaviour.TypeName.Trim();
        Type? type = Type.GetType(typeName, throwOnError: false);
        if (type is null)
        {
            IReadOnlyList<Assembly> assemblies = scriptAssemblies.Assemblies;
            for (int i = 0; i < assemblies.Count; i++)
            {
                type = assemblies[i].GetType(typeName, throwOnError: false);
                if (type is not null)
                {
                    break;
                }
            }
        }

        Type resolved = type ?? throw new InvalidOperationException($"未找到 Behaviour 类型：{typeName}。");
        return IsConcreteBehaviour(resolved)
            ? resolved
            : throw new InvalidOperationException($"{typeName} 不是可实例化的 Behaviour 类型。");
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = ".scene Behaviour constructor validation runs over runtime script types that are outside the trimmed engine closure.")]
    private static bool IsConcreteBehaviour([NotNullWhen(true)] Type? type)
    {
        return type is not null &&
            !type.IsAbstract &&
            typeof(Behaviour).IsAssignableFrom(type) &&
            type.GetConstructor(Type.EmptyTypes) is not null;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Serialized field binding is an editor/script content boundary over live Behaviour instances, not a trimmed engine hot path.")]
    private static void BindSerializedFields(IComponent component, Dictionary<string, string>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return;
        }

        Type type = component.GetType();
        foreach (KeyValuePair<string, string> field in fields)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            PropertyInfo? property = type.GetProperty(field.Key, flags);
            if (property is { CanWrite: true })
            {
                property.SetValue(component, ConvertValue(field.Value, property.PropertyType));
                continue;
            }

            FieldInfo? fieldInfo = type.GetField(field.Key, flags);
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
            _ when targetType == typeof(int) => int.Parse(value, CultureInfo.InvariantCulture),
            _ when targetType == typeof(long) => long.Parse(value, CultureInfo.InvariantCulture),
            _ when targetType == typeof(float) => float.Parse(value, CultureInfo.InvariantCulture),
            _ when targetType == typeof(double) => double.Parse(value, CultureInfo.InvariantCulture),
            _ when targetType == typeof(bool) => bool.Parse(value),
            _ when targetType == typeof(ushort) => ushort.Parse(value, CultureInfo.InvariantCulture),
            _ when targetType == typeof(MaterialId) => new MaterialId(ushort.Parse(value, CultureInfo.InvariantCulture)),
            _ when targetType == typeof(Vector2) => ParseVector2(value),
            _ when targetType.IsEnum => Enum.Parse(targetType, value, ignoreCase: true),
            _ => throw new NotSupportedException($"不支持绑定字段类型：{targetType.FullName}。"),
        };
    }

    private static Vector2 ParseVector2(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? new Vector2(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture))
            : throw new FormatException($"Vector2 字段必须使用 \"x,y\" 格式：{value}");
    }
}
