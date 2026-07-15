using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAutomationAuthoringApi
{
    private const string MaterialEditorResource = "editor:materials:document";
    private static readonly string[] MaterialEditorContentPaths = ["materials.json", "reactions.json"];

    private AutomationOperationResult GetMaterialEditor(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.MaterialEditorGetMethod);
        (MaterialReactionEditorPanel panel, _) = RequireMaterialEditor();
        AutomationMaterialEditorSnapshot response = MapMaterialEditorSnapshot(panel);
        return Result(
            response,
            AutomationJsonContext.Default.AutomationMaterialEditorSnapshot,
            [MaterialEditorResource, RuntimeMaterialsResource]);
    }

    private AutomationOperationResult SetMaterialEditor(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationMaterialEditorSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationMaterialEditorSetRequest,
            AutomationProtocolConstants.MaterialEditorSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.MaterialEditorSetMethod);
        (MaterialReactionEditorPanel panel, _) = RequireMaterialEditor();
        _ = RequireEditSession();
        MaterialReactionEditorDocument target = ToEditorDocument(
            request.Document,
            panel.CaptureDocument());
        MaterialReactionEditorPanelState before = panel.CaptureState();
        panel.ReplaceDocument(
            target,
            $"automation 已更新 {target.Materials.Count} 个材质、{target.Reactions.Count} 条源反应");
        MaterialReactionEditorPanelState after = panel.CaptureState();
        AutomationMaterialEditorSnapshot response = MapMaterialEditorSnapshot(panel);
        string[] resources = [MaterialEditorResource, RuntimeMaterialsResource];
        return CompleteMaterialEditorWrite(
            panel,
            "Set Materials Editor Draft",
            before,
            after,
            response,
            AutomationJsonContext.Default.AutomationMaterialEditorSnapshot,
            resources);
    }

    private AutomationBackgroundPreparation PrepareMaterialEditorReload(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.MaterialEditorReloadMethod);
        EditorProjectSession session = RequireEditSession();
        (MaterialReactionEditorPanel panel, FileMaterialReactionContentService content) =
            RequireMaterialEditor();
        MaterialReactionEditorPanelState before = panel.CaptureState();
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken => ValueTask.FromResult<object?>(
                new MaterialEditorReloadPreparation(
                    session,
                    panel,
                    content,
                    before,
                    content.LoadFiles(cancellationToken))),
        };
    }

    private AutomationOperationResult ReloadMaterialEditor(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        MaterialEditorReloadPreparation prepared =
            context.RequirePreparedState<MaterialEditorReloadPreparation>();
        EditorProjectSession session = RequireEditSession();
        (MaterialReactionEditorPanel panel, FileMaterialReactionContentService content) =
            RequireMaterialEditor();
        if (!ReferenceEquals(session, prepared.Session) ||
            !ReferenceEquals(panel, prepared.Panel) ||
            !ReferenceEquals(content, prepared.Content) ||
            !panel.StateEquals(prepared.Before))
        {
            throw StateUnavailable(
                "materials.editor.reload 后台读取期间 project session 或面板草稿已变化；请重试。");
        }

        MaterialReactionEditorDocument target = content.CreateEditorDocument(prepared.Files);
        panel.ReplaceDocument(
            target,
            $"已加载 {target.Materials.Count} 个材质、{target.Reactions.Count} 条源反应");
        MaterialReactionEditorPanelState after = panel.CaptureState();
        AutomationMaterialEditorSnapshot response = MapMaterialEditorSnapshot(panel);
        string[] resources = [MaterialEditorResource, RuntimeMaterialsResource];
        return CompleteMaterialEditorWrite(
            panel,
            "Reload Materials Editor Draft",
            prepared.Before,
            after,
            response,
            AutomationJsonContext.Default.AutomationMaterialEditorSnapshot,
            resources);
    }

    private AutomationOperationResult PreviewMaterialEditor(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.MaterialEditorPreviewMethod);
        (MaterialReactionEditorPanel panel, FileMaterialReactionContentService content) =
            RequireMaterialEditor();
        MaterialReactionEditorPanelState before = panel.CaptureState();
        MaterialReactionPreviewResult preview = content.Preview(panel.CaptureDocument());
        panel.SetStatus(preview.Message);
        MaterialReactionEditorPanelState after = panel.CaptureState();
        AutomationMaterialEditorPreviewResult response = new()
        {
            MaterialCount = preview.MaterialCount,
            SourceReactionCount = preview.SourceReactionCount,
            PackedReactionCount = preview.PackedReactionCount,
            Message = preview.Message,
        };
        string[] resources = [MaterialEditorResource, RuntimeMaterialsResource];
        return CompleteMaterialEditorWrite(
            panel,
            "Preview Materials Editor Draft",
            before,
            after,
            response,
            AutomationJsonContext.Default.AutomationMaterialEditorPreviewResult,
            resources);
    }

    private AutomationBackgroundPreparation PrepareMaterialEditorApply(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.MaterialEditorApplyMethod);
        EditorProjectSession session = RequireEditSession();
        (MaterialReactionEditorPanel panel, FileMaterialReactionContentService content) =
            RequireMaterialEditor();
        MaterialReactionEditorPanelState panelBefore = panel.CaptureState();
        FileMaterialReactionContentService.PreparedApply frozen =
            content.PrepareApply(panel.CaptureDocument());
        MaterialEditorApplyPreparation? completed = null;
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken =>
            {
                try
                {
                    frozen.PrepareFiles(cancellationToken);
                    MaterialEditorApplyPreparation result = new(
                        session,
                        panel,
                        content,
                        panelBefore,
                        frozen);
                    Volatile.Write(ref completed, result);
                    return ValueTask.FromResult<object?>(result);
                }
                catch
                {
                    frozen.Dispose();
                    throw;
                }
            },
            AbortAtEditorIngress = () =>
                Volatile.Read(ref completed)?.DisposeUncommitted(),
        };
    }

    private AutomationOperationResult ApplyMaterialEditor(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        MaterialEditorApplyPreparation prepared =
            context.RequirePreparedState<MaterialEditorApplyPreparation>();
        EditorProjectSession session = RequireEditSession();
        (MaterialReactionEditorPanel panel, FileMaterialReactionContentService content) =
            RequireMaterialEditor();
        bool authorityCurrent = ReferenceEquals(session, prepared.Session) &&
            ReferenceEquals(panel, prepared.Panel) &&
            ReferenceEquals(content, prepared.Content) &&
            panel.StateEquals(prepared.PanelBefore);
        return !authorityCurrent
            ? throw StateUnavailable(
                "materials.editor.apply 后台准备期间 project session 或面板草稿已变化；请重试。")
            : prepared.Plan.StateChanged
                ? ApplyChangedMaterialEditor(prepared, panel, session)
                : ApplyNoChangeMaterialEditor(prepared, panel);
    }

    private AutomationOperationResult ApplyChangedMaterialEditor(
        MaterialEditorApplyPreparation prepared,
        MaterialReactionEditorPanel panel,
        EditorProjectSession session)
    {
        FileMaterialReactionContentService.CommittedApply committed =
            prepared.Plan.CommitReversible();
        MaterialReactionApplyResult result = committed.Result;
        EditorAssetBrowserDataSource assets = session.AutomationAssetDatabase;
        try
        {
            _ = assets.SynchronizeKnownContentFiles(MaterialEditorContentPaths);
        }
        catch (Exception operationException)
        {
            try
            {
                committed.Undo();
                _ = assets.SynchronizeKnownContentFiles(MaterialEditorContentPaths);
                committed.Dispose();
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "materials.editor.apply 资产索引同步失败，且双文件/运行时 before-image 回滚不完整。",
                    operationException,
                    rollbackException);
            }

            throw;
        }

        panel.SetStatus(MaterialReactionEditorPanel.FormatApplyStatus(result));
        MaterialReactionEditorPanelState panelAfter = panel.CaptureState();
        MaterialEditorApplyUndoAction action = new(
            committed,
            panel,
            assets,
            prepared.PanelBefore,
            panelAfter);
        AutomationMaterialEditorApplyResult response = MapMaterialEditorApplyResult(result, panel.Status);
        string[] resources = MaterialEditorApplyResources(session);
        try
        {
            return new AutomationOperationResult
            {
                Payload = JsonSerializer.SerializeToElement(
                    response,
                    AutomationJsonContext.Default.AutomationMaterialEditorApplyResult),
                ResourceIds = resources,
                UndoAction = action,
            };
        }
        catch (Exception operationException)
        {
            try
            {
                action.Undo();
                action.Dispose();
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "materials.editor.apply 响应失败，且完整 before-image 回滚失败。",
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }

    private AutomationOperationResult ApplyNoChangeMaterialEditor(
        MaterialEditorApplyPreparation prepared,
        MaterialReactionEditorPanel panel)
    {
        MaterialReactionApplyResult result = prepared.Plan.Commit();
        panel.SetStatus(MaterialReactionEditorPanel.FormatApplyStatus(result));
        MaterialReactionEditorPanelState panelAfter = panel.CaptureState();
        AutomationMaterialEditorApplyResult response = MapMaterialEditorApplyResult(result, panel.Status);
        string[] resources = [MaterialEditorResource, RuntimeMaterialsResource];
        return CompleteMaterialEditorWrite(
            panel,
            "Apply Materials Editor Draft",
            prepared.PanelBefore,
            panelAfter,
            response,
            AutomationJsonContext.Default.AutomationMaterialEditorApplyResult,
            resources);
    }

    private static AutomationMaterialEditorApplyResult MapMaterialEditorApplyResult(
        MaterialReactionApplyResult result,
        string status)
    {
        AutomationMaterialEditorAssetReload[] assetReloads = new AutomationMaterialEditorAssetReload[
            result.AssetReloads.Count];
        for (int i = 0; i < assetReloads.Length; i++)
        {
            MaterialAssetReloadRequest request = result.AssetReloads[i];
            assetReloads[i] = new AutomationMaterialEditorAssetReload
            {
                MaterialName = request.MaterialName,
                RuntimeId = request.RuntimeId,
                TextureChanged = request.TextureChanged,
                AudioChanged = request.AudioChanged,
            };
        }

        return new AutomationMaterialEditorApplyResult
        {
            TombstonedMaterialNames = [.. result.TombstonedMaterialNames],
            AddedCount = result.MaterialReload.AddedCount,
            PreservedCount = result.MaterialReload.PreservedCount,
            LiveGridFallbackReplacementCount = result.LiveGridFallbackReplacementCount,
            PackedReactionCount = result.PackedReactionCount,
            AssetReloads = assetReloads,
            CleanupPending = result.CleanupPending,
            RetainedJournalPath = result.RetainedJournalPath,
            CleanupError = result.CleanupError,
            Status = status,
        };
    }

    private static string[] MaterialEditorApplyResources(EditorProjectSession session)
    {
        HashSet<string> resources = new(StringComparer.Ordinal)
        {
            MaterialEditorResource,
            RuntimeMaterialsResource,
            RuntimeWorldResource,
            ProjectResource,
            AssetsResource,
        };
        string[] worldResources = RuntimeWorldResources(session);
        for (int i = 0; i < worldResources.Length; i++)
        {
            _ = resources.Add(worldResources[i]);
        }

        return [.. resources.Order(StringComparer.Ordinal)];
    }

    private AutomationOperationResult CompleteMaterialEditorWrite<TResponse>(
        MaterialReactionEditorPanel panel,
        string name,
        MaterialReactionEditorPanelState before,
        MaterialReactionEditorPanelState after,
        TResponse response,
        JsonTypeInfo<TResponse> typeInfo,
        string[] resources)
    {
        return panel.StateEquals(before)
            ? NoChange(response, typeInfo, resources)
            : CompleteSettingsWrite(
                name,
                before,
                after,
                panel.RestoreState,
                response,
                typeInfo,
                resources);
    }

    private (MaterialReactionEditorPanel Panel, FileMaterialReactionContentService Content)
        RequireMaterialEditor()
    {
        EditorProjectSession session = RequireSession();
        return session.TryGetAutomationMaterialEditor(out MaterialReactionEditorPanel panel, out FileMaterialReactionContentService content)
            ? (panel, content)
            : throw StateUnavailable("当前项目未初始化 Materials/Reactions 编辑器或缺少双文件内容。");
    }

    private static AutomationMaterialEditorSnapshot MapMaterialEditorSnapshot(
        MaterialReactionEditorPanel panel)
    {
        MaterialReactionEditorDocument document = panel.CaptureDocument();
        List<AutomationMaterialRuntimeBinding> bindings = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < document.Materials.Count; i++)
        {
            MaterialEditorRow row = document.Materials[i];
            if (row.RuntimeId is { } runtimeId && seen.Add(row.Name))
            {
                bindings.Add(new AutomationMaterialRuntimeBinding
                {
                    Name = row.Name,
                    RuntimeId = runtimeId,
                });
            }
        }

        return new AutomationMaterialEditorSnapshot
        {
            Document = MapEditorDocument(document),
            RuntimeBindings = [.. bindings],
            Status = panel.Status,
        };
    }

    private static AutomationMaterialEditorDocument MapEditorDocument(
        MaterialReactionEditorDocument document)
    {
        AutomationMaterialEditorRow[] materials = new AutomationMaterialEditorRow[document.Materials.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = MapMaterialEditorRow(document.Materials[i]);
        }

        AutomationMaterialTagRepresentative[] tags = new AutomationMaterialTagRepresentative[
            document.TagRepresentatives.Count];
        for (int i = 0; i < tags.Length; i++)
        {
            TagRepresentativeEditorRow row = document.TagRepresentatives[i];
            tags[i] = new AutomationMaterialTagRepresentative
            {
                Tag = row.Tag,
                Material = row.Material,
            };
        }

        AutomationMaterialReactionRow[] reactions = new AutomationMaterialReactionRow[
            document.Reactions.Count];
        for (int i = 0; i < reactions.Length; i++)
        {
            ReactionEditorRow row = document.Reactions[i];
            reactions[i] = new AutomationMaterialReactionRow
            {
                InputA = row.InputA,
                InputB = row.InputB,
                OutputA = row.OutputA,
                OutputB = row.OutputB,
                Probability = row.Probability,
                Flags = row.Flags,
            };
        }

        return new AutomationMaterialEditorDocument
        {
            Materials = materials,
            TagRepresentatives = tags,
            Reactions = reactions,
        };
    }

    private static AutomationMaterialEditorRow MapMaterialEditorRow(MaterialEditorRow row)
    {
        return new AutomationMaterialEditorRow
        {
            Name = row.Name,
            Type = row.Type,
            Density = row.Density,
            FlowRate = row.FlowRate,
            LiquidStatic = row.LiquidStatic,
            LiquidSand = row.LiquidSand,
            Flammability = row.Flammability,
            AutoIgnitionTemp = row.AutoIgnitionTemp,
            FireHp = row.FireHp,
            TemperatureOfFire = row.TemperatureOfFire,
            GeneratesSmoke = row.GeneratesSmoke,
            MeltPoint = row.MeltPoint,
            MeltTarget = row.MeltTarget,
            FreezePoint = row.FreezePoint,
            FreezeTarget = row.FreezeTarget,
            BoilPoint = row.BoilPoint,
            BoilTarget = row.BoilTarget,
            HeatConduct = row.HeatConduct,
            HeatCapacity = row.HeatCapacity,
            DefaultLifetime = row.DefaultLifetime,
            Durability = row.Durability,
            MaxIntegrity = row.MaxIntegrity,
            RubbleTarget = row.RubbleTarget,
            DebrisCount = row.DebrisCount,
            MineYield = row.MineYield,
            TextureId = row.TextureId,
            BaseColorBgra = row.BaseColor,
            ColorNoise = row.ColorNoise,
            RenderStyle = row.RenderStyle,
            LegendCategory = row.LegendCategory,
            OutlineColorBgra = row.OutlineColorBGRA,
            Alpha = row.Alpha,
            FlowTintBgra = row.FlowTintBGRA,
            DisplayName = row.DisplayName,
            LegendVisible = row.LegendVisible,
            Tags = row.Tags,
            ImpactCue = row.ImpactCue,
            FireCue = row.FireCue,
            SplashCue = row.SplashCue,
            ExplosionCue = row.ExplosionCue,
            ShatterCue = row.ShatterCue,
            AmbientCue = row.AmbientCue,
        };
    }

    private static MaterialReactionEditorDocument ToEditorDocument(
        AutomationMaterialEditorDocument source,
        MaterialReactionEditorDocument current)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(current);
        if (source.Materials is null ||
            source.TagRepresentatives is null ||
            source.Reactions is null)
        {
            throw Invalid("material editor document arrays 不能为 null。");
        }

        if (source.Materials.Length > ushort.MaxValue)
        {
            throw Invalid($"material editor 最多支持 {ushort.MaxValue} 个材质。");
        }

        Dictionary<string, ushort> runtimeBindings = new(StringComparer.Ordinal);
        for (int i = 0; i < current.Materials.Count; i++)
        {
            MaterialEditorRow row = current.Materials[i];
            if (row.RuntimeId is { } runtimeId && !string.IsNullOrWhiteSpace(row.Name))
            {
                _ = runtimeBindings.TryAdd(row.Name, runtimeId);
            }
        }

        MaterialReactionEditorDocument document = new();
        for (int i = 0; i < source.Materials.Length; i++)
        {
            AutomationMaterialEditorRow row = source.Materials[i] ??
                throw Invalid($"materials[{i}] 不能为 null。");
            ValidateMaterialEditorRow(row, i);
            ushort? runtimeId = runtimeBindings.TryGetValue(row.Name, out ushort id)
                ? id
                : null;
            document.Materials.Add(new MaterialEditorRow
            {
                RuntimeId = runtimeId,
                Name = row.Name,
                Type = row.Type,
                Density = row.Density,
                FlowRate = row.FlowRate,
                LiquidStatic = row.LiquidStatic,
                LiquidSand = row.LiquidSand,
                Flammability = row.Flammability,
                AutoIgnitionTemp = row.AutoIgnitionTemp,
                FireHp = row.FireHp,
                TemperatureOfFire = row.TemperatureOfFire,
                GeneratesSmoke = row.GeneratesSmoke,
                MeltPoint = row.MeltPoint,
                MeltTarget = row.MeltTarget,
                FreezePoint = row.FreezePoint,
                FreezeTarget = row.FreezeTarget,
                BoilPoint = row.BoilPoint,
                BoilTarget = row.BoilTarget,
                HeatConduct = row.HeatConduct,
                HeatCapacity = row.HeatCapacity,
                DefaultLifetime = row.DefaultLifetime,
                Durability = row.Durability,
                MaxIntegrity = row.MaxIntegrity,
                RubbleTarget = row.RubbleTarget,
                DebrisCount = row.DebrisCount,
                MineYield = row.MineYield,
                TextureId = row.TextureId,
                BaseColor = row.BaseColorBgra,
                ColorNoise = row.ColorNoise,
                RenderStyle = row.RenderStyle,
                LegendCategory = row.LegendCategory,
                OutlineColorBGRA = row.OutlineColorBgra,
                Alpha = row.Alpha,
                FlowTintBGRA = row.FlowTintBgra,
                DisplayName = row.DisplayName,
                LegendVisible = row.LegendVisible,
                Tags = row.Tags,
                ImpactCue = row.ImpactCue,
                FireCue = row.FireCue,
                SplashCue = row.SplashCue,
                ExplosionCue = row.ExplosionCue,
                ShatterCue = row.ShatterCue,
                AmbientCue = row.AmbientCue,
            });
        }

        for (int i = 0; i < source.TagRepresentatives.Length; i++)
        {
            AutomationMaterialTagRepresentative row = source.TagRepresentatives[i] ??
                throw Invalid($"tagRepresentatives[{i}] 不能为 null。");
            document.TagRepresentatives.Add(new TagRepresentativeEditorRow
            {
                Tag = ValidateEditorString(row.Tag, 64, $"tagRepresentatives[{i}].tag"),
                Material = ValidateEditorString(row.Material, 96, $"tagRepresentatives[{i}].material"),
            });
        }

        for (int i = 0; i < source.Reactions.Length; i++)
        {
            AutomationMaterialReactionRow row = source.Reactions[i] ??
                throw Invalid($"reactions[{i}] 不能为 null。");
            document.Reactions.Add(new ReactionEditorRow
            {
                InputA = ValidateEditorString(row.InputA, 96, $"reactions[{i}].inputA"),
                InputB = ValidateEditorString(row.InputB, 96, $"reactions[{i}].inputB"),
                OutputA = ValidateEditorString(row.OutputA, 96, $"reactions[{i}].outputA"),
                OutputB = ValidateEditorString(row.OutputB, 96, $"reactions[{i}].outputB"),
                Probability = row.Probability,
                Flags = ValidateEditorString(row.Flags, 128, $"reactions[{i}].flags"),
            });
        }

        return document;
    }

    private static void ValidateMaterialEditorRow(AutomationMaterialEditorRow row, int index)
    {
        _ = ValidateEditorString(row.Name, 96, $"materials[{index}].name");
        _ = ValidateEditorString(row.Type, 32, $"materials[{index}].type");
        _ = ValidateEditorString(row.MeltTarget, 96, $"materials[{index}].meltTarget");
        _ = ValidateEditorString(row.FreezeTarget, 96, $"materials[{index}].freezeTarget");
        _ = ValidateEditorString(row.BoilTarget, 96, $"materials[{index}].boilTarget");
        _ = ValidateEditorString(row.RubbleTarget, 96, $"materials[{index}].rubbleTarget");
        _ = ValidateEditorString(row.RenderStyle, 64, $"materials[{index}].renderStyle");
        _ = ValidateEditorString(row.LegendCategory, 64, $"materials[{index}].legendCategory");
        _ = ValidateEditorString(row.DisplayName, 96, $"materials[{index}].displayName");
        _ = ValidateEditorString(row.Tags, 192, $"materials[{index}].tags");
        if (!float.IsFinite(row.HeatCapacity) ||
            (row.MeltPoint.HasValue && !float.IsFinite(row.MeltPoint.Value)) ||
            (row.FreezePoint.HasValue && !float.IsFinite(row.FreezePoint.Value)) ||
            (row.BoilPoint.HasValue && !float.IsFinite(row.BoilPoint.Value)))
        {
            throw Invalid($"materials[{index}] 温度与 heatCapacity 必须是有限值。");
        }
    }

    private static string ValidateEditorString(string? value, int maximumLength, string field)
    {
        return value is null
            ? throw Invalid($"{field} 不能为 null。")
            : value.Length >= maximumLength
                ? throw Invalid($"{field} 长度必须小于 {maximumLength}。")
                : value;
    }

    private sealed record MaterialEditorReloadPreparation(
        EditorProjectSession Session,
        MaterialReactionEditorPanel Panel,
        FileMaterialReactionContentService Content,
        MaterialReactionEditorPanelState Before,
        MaterialReactionContentFiles Files);

    private sealed class MaterialEditorApplyPreparation(
        EditorProjectSession session,
        MaterialReactionEditorPanel panel,
        FileMaterialReactionContentService content,
        MaterialReactionEditorPanelState panelBefore,
        FileMaterialReactionContentService.PreparedApply plan)
    {
        internal EditorProjectSession Session { get; } = session;

        internal MaterialReactionEditorPanel Panel { get; } = panel;

        internal FileMaterialReactionContentService Content { get; } = content;

        internal MaterialReactionEditorPanelState PanelBefore { get; } = panelBefore;

        internal FileMaterialReactionContentService.PreparedApply Plan { get; } = plan;

        internal void DisposeUncommitted()
        {
            Plan.Dispose();
        }
    }

    private sealed class MaterialEditorApplyUndoAction(
        FileMaterialReactionContentService.CommittedApply committed,
        MaterialReactionEditorPanel panel,
        EditorAssetBrowserDataSource assets,
        MaterialReactionEditorPanelState before,
        MaterialReactionEditorPanelState after) : IAutomationUndoAction, IDisposable
    {
        public string Name => "Apply Materials And Reactions";

        public void Undo()
        {
            if (!panel.StateEquals(after))
            {
                throw new InvalidOperationException(
                    "Materials/Reactions 面板 after-image 已变化，拒绝覆盖较新的草稿。");
            }

            bool transitioned = false;
            try
            {
                committed.Undo();
                transitioned = true;
                _ = assets.SynchronizeKnownContentFiles(MaterialEditorContentPaths);
                panel.RestoreState(before);
            }
            catch (Exception operationException)
            {
                if (!transitioned)
                {
                    throw;
                }

                try
                {
                    committed.Redo();
                    _ = assets.SynchronizeKnownContentFiles(MaterialEditorContentPaths);
                    panel.RestoreState(after);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Materials/Reactions Undo 失败，且完整 after-image 无法恢复。",
                        operationException,
                        rollbackException);
                }

                throw;
            }
        }

        public void Redo()
        {
            if (!panel.StateEquals(before))
            {
                throw new InvalidOperationException(
                    "Materials/Reactions 面板 before-image 已变化，拒绝覆盖较新的草稿。");
            }

            bool transitioned = false;
            try
            {
                committed.Redo();
                transitioned = true;
                _ = assets.SynchronizeKnownContentFiles(MaterialEditorContentPaths);
                panel.RestoreState(after);
            }
            catch (Exception operationException)
            {
                if (!transitioned)
                {
                    throw;
                }

                try
                {
                    committed.Undo();
                    _ = assets.SynchronizeKnownContentFiles(MaterialEditorContentPaths);
                    panel.RestoreState(before);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Materials/Reactions Redo 失败，且完整 before-image 无法恢复。",
                        operationException,
                        rollbackException);
                }

                throw;
            }
        }

        public void Dispose()
        {
            committed.Dispose();
        }
    }
}
