using System.Globalization;
using System.Diagnostics.CodeAnalysis;
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
    public const int CurrentFormatVersion = 1;

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

        PixelEngine.Scripting.Scene scene = new();
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = scene.CreateEntity();
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
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException($"不支持的 .scene 格式版本：{document.FormatVersion}。");
        }
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
            _ when targetType.IsEnum => Enum.Parse(targetType, value, ignoreCase: true),
            _ => throw new NotSupportedException($"不支持绑定字段类型：{targetType.FullName}。"),
        };
    }
}
