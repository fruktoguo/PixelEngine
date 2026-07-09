using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Editor.Shell;

internal readonly record struct EditorAssetDropPayload(
    string AssetId,
    string LogicalPath,
    EditorAssetType AssetType)
{
    public static EditorAssetDropPayload FromAsset(EditorAssetRecord asset)
    {
        return new EditorAssetDropPayload(asset.Id, asset.LogicalPath, asset.AssetType);
    }

    public static bool TryFromBrowserPayload(AssetBrowserDragPayload payload, out EditorAssetDropPayload result)
    {
        if (string.IsNullOrWhiteSpace(payload.AssetId) || string.IsNullOrWhiteSpace(payload.Path))
        {
            result = default;
            return false;
        }

        result = new EditorAssetDropPayload(payload.AssetId, payload.Path, MapKind(payload.Kind));
        return true;
    }

    private static EditorAssetType MapKind(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Material => EditorAssetType.Material,
            AssetBrowserItemKind.Texture => EditorAssetType.Texture,
            AssetBrowserItemKind.Audio => EditorAssetType.Audio,
            AssetBrowserItemKind.Scene => EditorAssetType.Scene,
            AssetBrowserItemKind.Prefab => EditorAssetType.Prefab,
            AssetBrowserItemKind.Script => EditorAssetType.Script,
            AssetBrowserItemKind.Json => EditorAssetType.Json,
            AssetBrowserItemKind.Other => EditorAssetType.Other,
            _ => EditorAssetType.Other,
        };
    }
}

internal readonly record struct EditorAssetInspectorFieldTarget(
    int StableId,
    int ComponentIndex,
    string FieldName,
    EditorAssetType ExpectedAssetType)
{
    public static bool TryCreate(int stableId, int componentIndex, ScriptFieldDescriptor field, out EditorAssetInspectorFieldTarget target)
    {
        if (field.Kind != ScriptFieldKind.AssetReference || field.AssetKind is not ScriptAssetKind assetKind)
        {
            target = default;
            return false;
        }

        target = new EditorAssetInspectorFieldTarget(stableId, componentIndex, field.Name, ToEditorAssetType(assetKind));
        return true;
    }

    public static EditorAssetType ToEditorAssetType(ScriptAssetKind assetKind)
    {
        return assetKind switch
        {
            ScriptAssetKind.Material => EditorAssetType.Material,
            ScriptAssetKind.Texture => EditorAssetType.Texture,
            ScriptAssetKind.Audio => EditorAssetType.Audio,
            ScriptAssetKind.Scene => EditorAssetType.Scene,
            ScriptAssetKind.Prefab => EditorAssetType.Prefab,
            ScriptAssetKind.Script => EditorAssetType.Script,
            _ => throw new ArgumentOutOfRangeException(nameof(assetKind), assetKind, "未知脚本资产字段类型。"),
        };
    }

    public static bool TryMapAssetType(ScriptAssetKind assetKind, out EditorAssetType assetType)
    {
        assetType = assetKind switch
        {
            ScriptAssetKind.Material => EditorAssetType.Material,
            ScriptAssetKind.Texture => EditorAssetType.Texture,
            ScriptAssetKind.Audio => EditorAssetType.Audio,
            ScriptAssetKind.Scene => EditorAssetType.Scene,
            ScriptAssetKind.Prefab => EditorAssetType.Prefab,
            ScriptAssetKind.Script => EditorAssetType.Script,
            _ => EditorAssetType.Other,
        };
        return assetType != EditorAssetType.Other;
    }
}

internal readonly record struct EditorAssetDropResult(
    bool Succeeded,
    string Diagnostic,
    int? StableId = null)
{
    public static EditorAssetDropResult Success(string diagnostic, int? stableId = null)
    {
        return new EditorAssetDropResult(true, diagnostic, stableId);
    }

    public static EditorAssetDropResult Failure(string diagnostic)
    {
        return new EditorAssetDropResult(false, diagnostic);
    }
}

/// <summary>
/// 资产拖放到 Hierarchy/Inspector 的解析与引用编码。
/// </summary>
internal static class EditorAssetDropService
{
    public static EditorAssetDropResult DropOnHierarchy(
        EditorSceneModel scene,
        EditorUndoStack undo,
        EditorPrefabAssetStore prefabs,
        EditorAssetDropPayload payload,
        int? parentStableId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(prefabs);
        if (!TryValidate(payload, out string diagnostic))
        {
            return EditorAssetDropResult.Failure(diagnostic);
        }

        if (payload.AssetType != EditorAssetType.Prefab)
        {
            return EditorAssetDropResult.Failure($"{payload.AssetType} 资产不能拖拽到 Hierarchy；仅 prefab 可实例化为 GameObject。");
        }

        try
        {
            undo.Execute(scene, new InstantiatePrefabCommand(prefabs, payload.LogicalPath, parentStableId));
            return EditorAssetDropResult.Success($"已实例化 prefab：{payload.LogicalPath}", scene.SelectedStableId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException or FileNotFoundException)
        {
            return EditorAssetDropResult.Failure($"prefab 拖拽到 Hierarchy 失败：{ex.Message}");
        }
    }

    public static EditorAssetDropResult DropOnSceneView(
        EditorSceneModel scene,
        EditorUndoStack undo,
        EditorPrefabAssetStore prefabs,
        EditorAssetDropPayload payload,
        EditorSceneTransform worldTransform)
    {
        ArgumentNullException.ThrowIfNull(worldTransform);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(prefabs);
        if (!TryValidate(payload, out string diagnostic))
        {
            return EditorAssetDropResult.Failure(diagnostic);
        }

        if (payload.AssetType != EditorAssetType.Prefab)
        {
            return EditorAssetDropResult.Failure($"{payload.AssetType} 资产不能拖拽到 Scene View；仅 prefab 可放置为 GameObject。");
        }

        try
        {
            undo.Execute(scene, new InstantiatePrefabCommand(prefabs, payload.LogicalPath, parentId: null, worldTransform));
            return EditorAssetDropResult.Success($"已在 Scene View 放置 prefab：{payload.LogicalPath}", scene.SelectedStableId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException or FileNotFoundException)
        {
            return EditorAssetDropResult.Failure($"prefab 拖拽到 Scene View 失败：{ex.Message}");
        }
    }

    public static EditorAssetDropResult DropOnInspectorField(
        EditorSceneModel scene,
        EditorUndoStack undo,
        EditorAssetDropPayload payload,
        EditorAssetInspectorFieldTarget target)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        if (!TryValidate(payload, out string diagnostic))
        {
            return EditorAssetDropResult.Failure(diagnostic);
        }

        if (payload.AssetType != target.ExpectedAssetType)
        {
            return EditorAssetDropResult.Failure($"字段 {target.FieldName} 需要 {target.ExpectedAssetType} 资产，收到 {payload.AssetType}。");
        }

        try
        {
            _ = scene.Get(target.StableId).Components[target.ComponentIndex];
            if (!EditorAssetReferenceCodec.TryEncode(payload.AssetId, payload.LogicalPath, payload.AssetType, out string value, out string encodeDiagnostic))
            {
                return EditorAssetDropResult.Failure($"资产拖拽到 Inspector 字段失败：{encodeDiagnostic}");
            }

            undo.Execute(scene, new SetComponentFieldCommand(target.StableId, target.ComponentIndex, target.FieldName, value));
            return EditorAssetDropResult.Success($"已把 {payload.LogicalPath} 绑定到字段 {target.FieldName}。", target.StableId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException or ArgumentOutOfRangeException)
        {
            return EditorAssetDropResult.Failure($"资产拖拽到 Inspector 字段失败：{ex.Message}");
        }
    }

    public static EditorAssetDropResult DropScriptOnComponentList(
        EditorSceneModel scene,
        EditorUndoStack undo,
        ScriptAssemblyRegistry scripts,
        EditorAssetDropPayload payload,
        int stableId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(scripts);
        if (!TryValidate(payload, out string diagnostic))
        {
            return EditorAssetDropResult.Failure(diagnostic);
        }

        if (payload.AssetType != EditorAssetType.Script)
        {
            return EditorAssetDropResult.Failure($"{payload.AssetType} 资产不能拖拽到组件列表；仅 script 可添加为 Behaviour。");
        }

        if (!TryResolveBehaviourType(payload, scripts, out string typeName))
        {
            return EditorAssetDropResult.Failure($"脚本资产 {payload.LogicalPath} 未解析到可挂载 Behaviour。");
        }

        try
        {
            _ = scene.Get(stableId);
            undo.Execute(scene, new AddComponentCommand(stableId, new EditorComponentModel(typeName)));
            return EditorAssetDropResult.Success($"已添加脚本组件：{typeName}", stableId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return EditorAssetDropResult.Failure($"脚本拖拽到 Inspector 组件列表失败：{ex.Message}");
        }
    }

    private static bool TryValidate(EditorAssetDropPayload payload, out string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(payload.AssetId))
        {
            diagnostic = "拖拽资产缺少 stable asset id。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.LogicalPath))
        {
            diagnostic = "拖拽资产缺少 logical path。";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private static bool TryResolveBehaviourType(EditorAssetDropPayload payload, ScriptAssemblyRegistry scripts, out string typeName)
    {
        string scriptName = Path.GetFileNameWithoutExtension(payload.LogicalPath);
        for (int i = 0; i < scripts.Assemblies.Count; i++)
        {
            foreach (Type type in scripts.Assemblies[i].GetTypes())
            {
                if (type is not { IsAbstract: false } ||
                    !typeof(Behaviour).IsAssignableFrom(type) ||
                    type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                if (string.Equals(type.Name, scriptName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, scriptName, StringComparison.Ordinal))
                {
                    typeName = type.FullName ?? type.Name;
                    return true;
                }
            }
        }

        typeName = string.Empty;
        return false;
    }
}

internal readonly record struct EditorAssetReference(
    string AssetId,
    string LogicalPath,
    EditorAssetType AssetType);

/// <summary>
/// 资产引用字符串的编解码。
/// </summary>
internal static class EditorAssetReferenceCodec
{
    public static string Encode(string assetId, string logicalPath, EditorAssetType assetType)
    {
        return TryEncode(assetId, logicalPath, assetType, out string value, out string diagnostic)
            ? value
            : throw new InvalidOperationException(diagnostic);
    }

    public static bool TryEncode(
        string assetId,
        string logicalPath,
        EditorAssetType assetType,
        out string value,
        out string diagnostic)
    {
        if (!TryMapToScriptAssetKind(assetType, out ScriptAssetKind scriptAssetKind))
        {
            value = string.Empty;
            diagnostic = $"{assetType} 资产不能编码为脚本 typed asset reference。";
            return false;
        }

        value = ScriptAssetReference.Encode(assetId, logicalPath, scriptAssetKind);
        diagnostic = string.Empty;
        return true;
    }

    public static bool TryDecode(string? value, out EditorAssetReference reference)
    {
        reference = default;
        if (!ScriptAssetReference.TryDecode(value, out ScriptAssetReference scriptReference) ||
            !EditorAssetInspectorFieldTarget.TryMapAssetType(scriptReference.AssetType, out EditorAssetType assetType))
        {
            return false;
        }

        reference = new EditorAssetReference(scriptReference.AssetId, scriptReference.LogicalPath, assetType);
        return true;
    }

    private static bool TryMapToScriptAssetKind(EditorAssetType assetType, out ScriptAssetKind scriptAssetKind)
    {
        switch (assetType)
        {
            case EditorAssetType.Material:
                scriptAssetKind = ScriptAssetKind.Material;
                return true;
            case EditorAssetType.Texture:
                scriptAssetKind = ScriptAssetKind.Texture;
                return true;
            case EditorAssetType.Audio:
                scriptAssetKind = ScriptAssetKind.Audio;
                return true;
            case EditorAssetType.Scene:
                scriptAssetKind = ScriptAssetKind.Scene;
                return true;
            case EditorAssetType.Prefab:
                scriptAssetKind = ScriptAssetKind.Prefab;
                return true;
            case EditorAssetType.Script:
                scriptAssetKind = ScriptAssetKind.Script;
                return true;
            case EditorAssetType.Json:
            case EditorAssetType.Other:
            default:
                scriptAssetKind = default;
                return false;
        }
    }

    public static bool TryRewrite(
        string value,
        string oldPath,
        string newPath,
        string assetId,
        EditorAssetType assetType,
        out string rewritten)
    {
        rewritten = value;
        if (!TryDecode(value, out EditorAssetReference reference) || reference.AssetType != assetType)
        {
            return false;
        }

        bool matchesId = string.Equals(reference.AssetId, assetId, StringComparison.OrdinalIgnoreCase);
        bool matchesPath = string.Equals(reference.LogicalPath, oldPath, StringComparison.OrdinalIgnoreCase);
        if (!matchesId && !matchesPath)
        {
            return false;
        }

        rewritten = Encode(assetId, newPath, assetType);
        return !string.Equals(value, rewritten, StringComparison.Ordinal);
    }
}
