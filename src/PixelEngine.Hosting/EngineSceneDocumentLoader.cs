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
    public const int CurrentFormatVersion = 3;

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
        ValidateDocument(document, IsPrefabPath(path));
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
        bool isPrefab = IsPrefabPath(path);
        EngineSceneDocument normalized = NormalizeForSave(document, isPrefab);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            normalized,
            EngineSceneJsonContext.Default.EngineSceneDocument);
        AtomicTextFile.WriteAllText(path, json);
    }

    /// <summary>
    /// 从 .scene 文件加载脚本实体场景。
    /// </summary>
    /// <param name="path">.scene 文件路径。</param>
    /// <param name="scriptAssemblies">已注册脚本程序集。</param>
    /// <returns>物化后的脚本场景。</returns>
    public static Scripting.Scene Load(string path, ScriptAssemblyRegistry scriptAssemblies)
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
    public static Scripting.Scene Build(EngineSceneDocument document, ScriptAssemblyRegistry scriptAssemblies)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scriptAssemblies);
        ValidateDocument(document, isPrefab: false);

        Scripting.Scene scene = new();
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        Dictionary<int, EngineSceneTransformDocument> transformsByStableId = new(entities.Length);
        Dictionary<int, bool> enabledByStableId = new(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = scene.CreateEntity();
            Transform transform = entity.AddComponent<Transform>();
            EngineSceneTransformDocument worldTransform = ResolveWorldTransform(entities, i, transformsByStableId, []);
            ApplyTransform(transform, worldTransform);
            transformsByStableId[entities[i].StableId] = worldTransform;
            bool effectivelyEnabled = ResolveEffectiveEnabled(entities, i, enabledByStableId, []);
            enabledByStableId[entities[i].StableId] = effectivelyEnabled;
            EngineSceneBehaviourDocument[] behaviours = entities[i].Behaviours ?? [];
            for (int j = 0; j < behaviours.Length; j++)
            {
                Type behaviourType = ResolveBehaviourType(behaviours[j], scriptAssemblies);
                IComponent component = entity.AddComponent(behaviourType);
                BindSerializedFields(component, behaviours[j].SerializedFields);
                if (!effectivelyEnabled && component is Behaviour behaviour)
                {
                    behaviour.Enabled = false;
                }
            }
        }

        return scene;
    }

    internal static void ValidateDocument(EngineSceneDocument document, bool isPrefab)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion is < 1 or > CurrentFormatVersion)
        {
            throw new NotSupportedException($"不支持的 .scene 格式版本：{document.FormatVersion}。");
        }

        ValidateSceneGraph(document);
        ValidateCanvasRules(document, isPrefab);
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

    private static EngineSceneDocument NormalizeForSave(EngineSceneDocument document, bool isPrefab)
    {
        ValidateDocument(document, isPrefab);
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
                Enabled = sorted[i].Enabled ?? true,
                Transform = sorted[i].Transform ?? new EngineSceneTransformDocument(),
                Prefab = NormalizePrefab(sorted[i].Prefab),
                WebCanvas = NormalizeWebCanvas(sorted[i].WebCanvas),
                CanvasScaler = NormalizeCanvasScaler(sorted[i].CanvasScaler),
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

    private static EngineScenePrefabDocument? NormalizePrefab(EngineScenePrefabDocument? prefab)
    {
        if (prefab is null)
        {
            return null;
        }

        EngineScenePrefabOverrideDocument[] overrides = prefab.Overrides ?? [];
        return new EngineScenePrefabDocument
        {
            AssetId = prefab.AssetId,
            AssetPath = prefab.AssetPath,
            SourceStableId = prefab.SourceStableId,
            Overrides =
            [
                .. overrides
                    .OrderBy(static item => item.SourceStableId, StringComparer.Ordinal)
                    .ThenBy(static item => item.PropertyPath, StringComparer.Ordinal)
                    .Select(static item => new EngineScenePrefabOverrideDocument
                    {
                        SourceStableId = item.SourceStableId,
                        PropertyPath = item.PropertyPath,
                        Value = item.Value,
                    }),
            ],
        };
    }

    private static EngineSceneWebCanvasDocument? NormalizeWebCanvas(EngineSceneWebCanvasDocument? canvas)
    {
        return canvas is null
            ? null
            : new EngineSceneWebCanvasDocument
            {
                ManifestAssetId = NormalizeOptional(canvas.ManifestAssetId),
                ManifestPath = NormalizeOptional(canvas.ManifestPath),
                InitialScreenId = NormalizeOptional(canvas.InitialScreenId),
                Enabled = canvas.Enabled,
                SortingOrder = canvas.SortingOrder,
                Primary = canvas.Primary,
            };
    }

    private static EngineSceneCanvasScalerDocument? NormalizeCanvasScaler(EngineSceneCanvasScalerDocument? scaler)
    {
        if (scaler is null)
        {
            return null;
        }

        PixelEngine.UI.UiCanvasScalerSettings settings = scaler.ToSettings();
        ValidateCanvasScaler(in settings);
        return EngineSceneCanvasScalerDocument.FromSettings(in settings);
    }

    private static void ValidateCanvasRules(EngineSceneDocument document, bool isPrefab)
    {
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        bool hasVersionThreeComponent = false;
        int enabledPrimaryCount = 0;
        Dictionary<int, bool> enabledByStableId = new(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            EngineSceneEntityDocument entity = entities[i];
            EngineSceneWebCanvasDocument? canvas = entity.WebCanvas;
            hasVersionThreeComponent |= canvas is not null || entity.CanvasScaler is not null;
            if (entity.CanvasScaler is not null)
            {
                PixelEngine.UI.UiCanvasScalerSettings settings = entity.CanvasScaler.ToSettings();
                ValidateCanvasScaler(in settings);
            }

            if (canvas is null)
            {
                continue;
            }

            ValidateRelativeAssetPath(canvas.ManifestPath, entity.StableId);
            if (isPrefab && canvas.Primary)
            {
                throw new InvalidOperationException(
                    $"Prefab asset 不能持久化 primary Web Canvas：stableId={entity.StableId}。");
            }

            if (canvas.Primary &&
                canvas.Enabled &&
                ResolveEffectiveEnabled(entities, i, enabledByStableId, []))
            {
                enabledPrimaryCount++;
            }
        }

        if (document.FormatVersion < 3 && hasVersionThreeComponent)
        {
            throw new InvalidOperationException(
                $".scene v{document.FormatVersion} 不能包含 WebCanvas/CanvasScaler；请显式转换为 v3。");
        }

        if (enabledPrimaryCount > 1)
        {
            throw new InvalidOperationException(
                ".scene 包含多个已启用的 explicit primary Web Canvas，必须修复后才能 Save/Play。");
        }
    }

    private static void ValidateCanvasScaler(in PixelEngine.UI.UiCanvasScalerSettings settings)
    {
        PixelEngine.UI.UiDisplayMetrics display = new(1, 1, 1f, 1f, null, 0, 0);
        _ = PixelEngine.UI.UiCanvasScaleResolver.Resolve(in settings, in display);
    }

    private static void ValidateRelativeAssetPath(string? path, int stableId)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string value = path.Trim();
        if (Path.IsPathRooted(value))
        {
            throw new InvalidDataException(
                $"WebCanvas manifestPath 必须相对 content 根目录：stableId={stableId}, path={value}");
        }

        string[] segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment == ".."))
        {
            throw new InvalidDataException(
                $"WebCanvas manifestPath 不能逃逸 content 根目录：stableId={stableId}, path={value}");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsPrefabPath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".prefab", StringComparison.OrdinalIgnoreCase);
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

    private static bool ResolveEffectiveEnabled(
        EngineSceneEntityDocument[] entities,
        int index,
        Dictionary<int, bool> resolved,
        HashSet<int> resolving)
    {
        EngineSceneEntityDocument entity = entities[index];
        if (!resolving.Add(entity.StableId))
        {
            throw new InvalidOperationException($".scene 实体层级包含循环引用：{entity.StableId}。");
        }

        bool enabled = entity.Enabled ?? true;
        if (enabled && entity.ParentId.HasValue)
        {
            int parentIndex = FindEntityIndex(entities, entity.ParentId.Value);
            if (parentIndex < 0)
            {
                throw new InvalidOperationException($".scene 实体 {entity.StableId} 引用了不存在的父实体 {entity.ParentId.Value}。");
            }

            enabled = resolved.TryGetValue(entity.ParentId.Value, out bool parentEnabled)
                ? parentEnabled
                : ResolveEffectiveEnabled(entities, parentIndex, resolved, resolving);
        }

        _ = resolving.Remove(entity.StableId);
        return enabled;
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

    private static void BindSerializedFields(IComponent component, Dictionary<string, string>? fields)
    {
        SerializedFieldBinder.Bind(component, fields);
    }
}
