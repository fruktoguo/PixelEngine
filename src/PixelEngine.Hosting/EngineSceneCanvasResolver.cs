using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 .scene v1/v2/v3 文档解析为不进入脚本组件桶的 runtime Web Canvas 集合。
/// </summary>
public static class EngineSceneCanvasResolver
{
    /// <summary>
    /// 校验场景并解析 implicit/explicit Canvas、层级 enabled、primary、排序与 scaler fallback。
    /// </summary>
    /// <param name="document">已读取的场景文档。</param>
    /// <returns>确定性 runtime Canvas 集合。</returns>
    public static EngineSceneCanvasSet Resolve(EngineSceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EngineSceneDocumentLoader.ValidateDocument(document, isPrefab: false);
        EngineSceneEntityDocument[] entities = document.Entities ?? [];
        List<EngineSceneCanvasDiagnostic> diagnostics = [];
        bool hasExplicitCanvases = false;
        for (int i = 0; i < entities.Length; i++)
        {
            hasExplicitCanvases |= entities[i].WebCanvas is not null;
            if (entities[i].CanvasScaler is not null && entities[i].WebCanvas is null)
            {
                diagnostics.Add(new EngineSceneCanvasDiagnostic(
                    EngineSceneCanvasDiagnosticKind.OrphanScaler,
                    entities[i].StableId,
                    "Canvas Scaler 没有同对象 Canvas (Web)，已保留但不会物化运行时 Canvas。"));
            }
        }

        if (!hasExplicitCanvases)
        {
            diagnostics.Add(new EngineSceneCanvasDiagnostic(
                EngineSceneCanvasDiagnosticKind.LegacyImplicit,
                0,
                "场景没有显式 Canvas (Web)，正在使用不落盘的 implicit primary Canvas。"));
            EngineSceneCanvasDefinition implicitCanvas = new(
                GameUiCanvasIdentity.LegacyImplicit,
                stableId: 0,
                sortingOrder: 0,
                isPrimary: true,
                isImplicit: true,
                manifestAssetId: null,
                manifestPath: null,
                initialScreenId: null,
                UiCanvasScalerSettings.Default);
            return new EngineSceneCanvasSet(false, [implicitCanvas], [.. diagnostics]);
        }

        Dictionary<int, bool> effectiveEnabled = new(entities.Length);
        List<EngineSceneCanvasDefinition> enabled = [];
        int enabledExplicitPrimaryCount = 0;
        int enabledExplicitPrimaryStableId = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            EngineSceneEntityDocument entity = entities[i];
            EngineSceneWebCanvasDocument? canvas = entity.WebCanvas;
            if (canvas is null)
            {
                continue;
            }

            bool gameObjectEnabled = ResolveEffectiveEnabled(entities, i, effectiveEnabled, []);
            bool canvasEnabled = gameObjectEnabled && canvas.Enabled;
            if (!canvasEnabled)
            {
                if (canvas.Primary)
                {
                    diagnostics.Add(new EngineSceneCanvasDiagnostic(
                        EngineSceneCanvasDiagnosticKind.DisabledPrimary,
                        entity.StableId,
                        "该 Canvas 被标记为 primary，但自身或父级 disabled，因此不会成为运行时 primary。"));
                }

                continue;
            }

            if (canvas.Primary)
            {
                enabledExplicitPrimaryCount++;
                enabledExplicitPrimaryStableId = entity.StableId;
            }

            UiCanvasScalerSettings settings = entity.CanvasScaler?.ToSettings() ?? UiCanvasScalerSettings.Default;
            if (entity.CanvasScaler is null)
            {
                diagnostics.Add(new EngineSceneCanvasDiagnostic(
                    EngineSceneCanvasDiagnosticKind.MissingScaler,
                    entity.StableId,
                    "Canvas (Web) 没有同对象 Canvas Scaler，运行时使用 Unity-compatible 默认值。"));
            }

            ValidateScaler(in settings);
            enabled.Add(new EngineSceneCanvasDefinition(
                GameUiCanvasIdentity.FromStableId(entity.StableId),
                entity.StableId,
                canvas.SortingOrder,
                isPrimary: false,
                isImplicit: false,
                NormalizeOptional(canvas.ManifestAssetId),
                NormalizeOptional(canvas.ManifestPath),
                NormalizeOptional(canvas.InitialScreenId),
                settings));
        }

        if (enabledExplicitPrimaryCount > 1)
        {
            throw new InvalidOperationException(".scene 包含多个已启用的 explicit primary Web Canvas，必须修复后才能 Save/Play。");
        }

        enabled.Sort(static (left, right) =>
        {
            int order = left.SortingOrder.CompareTo(right.SortingOrder);
            return order != 0 ? order : left.StableId.CompareTo(right.StableId);
        });
        if (enabled.Count == 0)
        {
            diagnostics.Add(new EngineSceneCanvasDiagnostic(
                EngineSceneCanvasDiagnosticKind.AllCanvasesDisabled,
                0,
                "场景包含显式 Canvas，但全部 disabled；Primary Canvas 为 None，不会复活 implicit Canvas。"));
            return new EngineSceneCanvasSet(true, [], [.. diagnostics]);
        }

        int primaryIndex = 0;
        if (enabledExplicitPrimaryCount == 1)
        {
            for (int i = 0; i < enabled.Count; i++)
            {
                if (enabled[i].StableId == enabledExplicitPrimaryStableId)
                {
                    primaryIndex = i;
                    break;
                }
            }
        }

        enabled[primaryIndex] = enabled[primaryIndex] with { IsPrimary = true };
        return new EngineSceneCanvasSet(true, [.. enabled], [.. diagnostics]);
    }

    private static bool ResolveEffectiveEnabled(
        EngineSceneEntityDocument[] entities,
        int index,
        Dictionary<int, bool> resolved,
        HashSet<int> resolving)
    {
        EngineSceneEntityDocument entity = entities[index];
        if (resolved.TryGetValue(entity.StableId, out bool cached))
        {
            return cached;
        }

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

            enabled = ResolveEffectiveEnabled(entities, parentIndex, resolved, resolving);
        }

        _ = resolving.Remove(entity.StableId);
        resolved[entity.StableId] = enabled;
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

    private static void ValidateScaler(in UiCanvasScalerSettings settings)
    {
        UiDisplayMetrics validationDisplay = new(1, 1, 1f, 1f, null, 0, 0);
        _ = UiCanvasScaleResolver.Resolve(in settings, in validationDisplay);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
