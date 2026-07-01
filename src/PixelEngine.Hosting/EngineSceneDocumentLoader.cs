using System.Globalization;
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
    /// 从 .scene 文件加载脚本实体场景。
    /// </summary>
    /// <param name="path">.scene 文件路径。</param>
    /// <param name="scriptAssemblies">已注册脚本程序集。</param>
    /// <returns>物化后的脚本场景。</returns>
    public static PixelEngine.Scripting.Scene Load(string path, ScriptAssemblyRegistry scriptAssemblies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(scriptAssemblies);
        string json = File.ReadAllText(path);
        EngineSceneDocument document = JsonSerializer.Deserialize(
                json,
                EngineSceneJsonContext.Default.EngineSceneDocument) ??
            throw new JsonException(".scene 文件为空或格式无效。");
        return Build(document, scriptAssemblies);
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
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException($"不支持的 .scene 格式版本：{document.FormatVersion}。");
        }

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
        return typeof(Behaviour).IsAssignableFrom(resolved)
            ? resolved
            : throw new InvalidOperationException($"{typeName} 不是 Behaviour 类型。");
    }

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
