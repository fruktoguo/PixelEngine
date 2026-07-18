using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>真实外部 Editor 进程的 Scene/Game automation screenshot 回归。</summary>
public sealed class EditorAutomationCaptureE2ETests
{
    private const string EnableVariable = "PIXELENGINE_EDITOR_AUTOMATION_CAPTURE_E2E";

    /// <summary>通过真实 discovery、Named Pipe 与 GL present 捕获并验证两类图片制品。</summary>
    [Fact]
    [Trait("Category", "NativeSmoke")]
    public async Task ExternalEditorCapturesSceneAndGameArtifacts()
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "pixelengine-automation-capture-" + Guid.NewGuid().ToString("N"));
        string projectRoot = Path.Combine(root, "Project");
        string discoveryRoot = Path.Combine(root, "discovery");
        string artifactRoot = Path.Combine(root, "artifacts");
        string logRoot = Path.Combine(root, "logs");
        _ = EditorProject.CreateNew(projectRoot, "Automation Capture");
        _ = Directory.CreateDirectory(logRoot);
        string shellPath = Path.Combine(AppContext.BaseDirectory, "PixelEngine.Editor.Shell.exe");
        Assert.True(File.Exists(shellPath), shellPath);
        using Process process = new()
        {
            StartInfo = CreateStartInfo(
                shellPath,
                projectRoot,
                discoveryRoot,
                artifactRoot,
                logRoot),
        };
        Assert.True(process.Start());
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            AutomationDiscoveredInstance instance = await WaitForProjectInstanceAsync(
                process,
                discoveryRoot,
                stdout,
                stderr);
            await using EditorAutomationClient client = await EditorAutomationClient.ConnectAsync(
                instance,
                new AutomationClientOptions
                {
                    ClientInstanceId = "capture-e2e",
                    ClientName = "hosting-tests",
                    ClientVersion = "1.0",
                    RequestedScopes =
                    [
                        AutomationScopes.EditorRead,
                        AutomationScopes.EditorControl,
                        AutomationScopes.SettingsWrite,
                        AutomationScopes.ProjectWrite,
                    ],
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    RequestTimeout = TimeSpan.FromSeconds(30),
                });

            AutomationGamePresentationSnapshot presentation = await GetPresentationAsync(client);
            AssertNoProjectLayoutLeak(projectRoot, "initial-connect");
            Assert.True(presentation.HasCommittedPresentation);
            await AssertEmptySaveSlotsAsync(client);
            AutomationArtifactReference scene = await CaptureAsync(
                client,
                AutomationProtocolConstants.SceneCaptureMethod);
            AutomationArtifactReference game = await CaptureAsync(
                client,
                AutomationProtocolConstants.GameCaptureMethod);
            AutomationGamePresentationSnapshot afterCapture = await GetPresentationAsync(client);
            ValidateArtifact(scene, artifactRoot, requireVisiblePixel: true);
            ValidateArtifact(game, artifactRoot, requireVisiblePixel: false);
            Assert.Equal(afterCapture.PresentationWidth, game.Width);
            Assert.Equal(afterCapture.PresentationHeight, game.Height);
            long capturedRevision = game.Metadata?.GetProperty("contentRevision").GetInt64() ?? -1;
            Assert.InRange(
                capturedRevision,
                presentation.PresentationRevision,
                afterCapture.PresentationRevision);
            Assert.NotEqual(scene.ArtifactId, game.ArtifactId);
            AssertNoProjectLayoutLeak(projectRoot, "capture");
            await SelectProjectAssetFolderAndRestoreAsync(client);
            AssertNoProjectLayoutLeak(projectRoot, "project-selection");
            await SetProjectWindowViewAndRestoreAsync(client);
            AssertNoProjectLayoutLeak(projectRoot, "project-window");
            await OpenCodeProjectAndRestorePreferencesAsync(client);
            AssertNoProjectLayoutLeak(projectRoot, "code-project");
            await ToggleProfilerVSyncAndRestoreAsync(client);
            AssertNoProjectLayoutLeak(projectRoot, "profiler");
            await ToggleSimulationRateAndRestoreAsync(client);
            AssertNoProjectLayoutLeak(projectRoot, "simulation");
            await ToggleRuntimeTuningAndRestoreAsync(client);
            AssertNoProjectLayoutLeak(projectRoot, "runtime-tuning");
            await CreateMoveAndRestoreAssetsAsync(client, projectRoot);
            await SetProjectRootsAndUndoAsync(client, projectRoot);
            AutomationInvocationResult restoredWorkspace =
                await ExerciseWorkspaceProjectTransitionsAsync(client, root, projectRoot);
            await ExerciseRecentProjectsAndShortcutsAsync(client, root, projectRoot);
            AutomationInvocationResult exit = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.WorkspaceExitMethod,
                payload: null,
                options: new AutomationInvocationOptions
                {
                    ExpectedRevision = ToResourcePrecondition(restoredWorkspace.Revision),
                    IdempotencyKey = "capture-e2e-workspace-exit",
                });
            AutomationTransitionResult exitResult = exit.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationTransitionResult)
                ?? throw new InvalidOperationException("workspace.exit 未返回 transition result。");
            Assert.Equal("executed", exitResult.Status);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
        }
        catch (Exception exception)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            string stdoutText = await stdout;
            string stderrText = await stderr;
            string logText = ReadDiagnosticLogs(logRoot);
            throw new InvalidOperationException(
                $"外部 Editor automation E2E 失败。ExitCode={process.ExitCode}\n" +
                $"automation error:\n{FormatAutomationError(exception)}\n" +
                $"stdout:\n{stdoutText}\n" +
                $"stderr:\n{stderrText}\n" +
                $"logs:\n{logText}",
                exception);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            _ = await stdout;
            _ = await stderr;
            TryDeleteDirectory(root);
        }
    }

    private static string ReadDiagnosticLogs(string logRoot)
    {
        if (!Directory.Exists(logRoot))
        {
            return "<missing>";
        }

        const int MaximumCharacters = 64 * 1024;
        System.Text.StringBuilder builder = new();
        foreach (string path in Directory.EnumerateFiles(logRoot, "*", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            _ = builder.AppendLine($"--- {Path.GetRelativePath(logRoot, path)} ---");
            string content = File.ReadAllText(path);
            int remaining = MaximumCharacters - builder.Length;
            if (remaining <= 0)
            {
                _ = builder.AppendLine("<truncated>");
                break;
            }

            _ = builder.AppendLine(content.Length <= remaining ? content : content[..remaining]);
        }

        return builder.Length == 0 ? "<empty>" : builder.ToString();
    }

    private static async Task AssertEmptySaveSlotsAsync(EditorAutomationClient client)
    {
        AutomationSaveSlotListResponse response = await ListSaveSlotsAsync(client);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.Page.Total);
        Assert.Null(response.Page.NextCursor);
    }

    private static async Task<AutomationSaveSlotListResponse> ListSaveSlotsAsync(
        EditorAutomationClient client)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeSaveSlotListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = 25 },
                AutomationJsonContext.Default.AutomationPageRequest));
        return result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationSaveSlotListResponse)
            ?? throw new InvalidOperationException("runtime.saves.list 未返回结果。");
    }

    private static async Task ExerciseWorldSaveLoadAsync(
        EditorAutomationClient client,
        string projectRoot,
        AutomationMaterialDefinition[] materials)
    {
        const int WorldX = 10;
        const int WorldY = 10;
        const string SlotId = "capture-e2e-world";
        Assert.DoesNotContain(
            (await ListSaveSlotsAsync(client)).Items,
            item => item.SlotId == SlotId);
        AutomationInvocationResult initialInspect = await InspectCellAsync(client, WorldX, WorldY);
        AutomationRuntimeCellInspection initial = DeserializeCell(initialInspect);

        AutomationInvocationResult saved = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeSaveSlotSaveMethod,
            JsonSerializer.SerializeToElement(
                new AutomationSaveSlotRequest { SlotId = SlotId },
                AutomationJsonContext.Default.AutomationSaveSlotRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(initialInspect.Revision),
                IdempotencyKey = "capture-e2e-world-save",
            });
        AutomationSaveSlotOperationResult saveResult = saved.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationSaveSlotOperationResult)
            ?? throw new InvalidOperationException("runtime.saves.save 未返回结果。");
        Assert.Equal(SlotId, saveResult.Slot.SlotId);
        Assert.Equal(saveResult.WorldSeed, saveResult.Slot.WorldSeed);
        Assert.Equal(saveResult.GameTimeTicks, saveResult.Slot.GameTimeTicks);
        Assert.Equal(saveResult.ChunkCount, saveResult.Slot.ChunkCount);
        Assert.True(saveResult.ChunkCount > 0);
        string canonicalProjectRoot = Path.GetFullPath(projectRoot) + Path.DirectorySeparatorChar;
        string canonicalSlotPath = Path.GetFullPath(saveResult.Slot.Path);
        Assert.StartsWith(canonicalProjectRoot, canonicalSlotPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(canonicalSlotPath, "manifest.bin")));
        _ = Assert.Single((await ListSaveSlotsAsync(client)).Items, item => item.SlotId == SlotId);

        AutomationInvocationResult saveUndone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            saved.Revision,
            "capture-e2e-world-save-undo");
        Assert.DoesNotContain(
            (await ListSaveSlotsAsync(client)).Items,
            item => item.SlotId == SlotId);
        AutomationInvocationResult saveRedone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            saveUndone.Revision,
            "capture-e2e-world-save-redo");
        _ = Assert.Single((await ListSaveSlotsAsync(client)).Items, item => item.SlotId == SlotId);

        AutomationInvocationResult toolGet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.SceneToolGetMethod);
        AutomationSceneToolSnapshot toolBefore = toolGet.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationSceneToolSnapshot)
            ?? throw new InvalidOperationException("tool.scene.get 未返回结果。");
        AutomationBrushSettings brushBefore = toolBefore.Brush
            ?? throw new InvalidOperationException("Editor 未提供世界画刷设置。");
        AutomationMaterialDefinition brushMaterial = Assert.Single(
            materials,
            material => string.Equals(
                material.Name,
                brushBefore.MaterialName,
                StringComparison.Ordinal));
        Assert.Equal(brushMaterial.RuntimeId, brushBefore.MaterialId);
        Assert.True(initial.TemperatureAvailable);
        float expectedMutatedTemperature = initial.TemperatureCelsius == 64f ? 32f : 64f;
        AutomationRemoteException invalidMaterial = await Assert.ThrowsAsync<AutomationRemoteException>(
            async () => await client.InvokeDetailedAsync(
                AutomationProtocolConstants.SceneToolSetMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationSceneToolSetRequest
                    {
                        Brush = brushBefore with
                        {
                            MaterialName = "missing-e2e-material",
                            MaterialId = null,
                        },
                    },
                    AutomationJsonContext.Default.AutomationSceneToolSetRequest),
                new AutomationInvocationOptions
                {
                    ExpectedRevision = ToPrecondition(saveRedone.Revision),
                    IdempotencyKey = "capture-e2e-world-brush-invalid-material",
                }));
        Assert.Equal(AutomationErrorCodes.InvalidRequest, invalidMaterial.Error.Code);
        AutomationBrushSettings mutationBrush = brushBefore with
        {
            MaterialId = null,
            Tool = "Temperature",
            Shape = "Point",
            Radius = 0,
            RadiusX = 0,
            RadiusY = 0,
            Probability = 1f,
            TemperatureMode = "Target",
            TemperatureCelsius = expectedMutatedTemperature,
        };
        AutomationInvocationResult toolSet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.SceneToolSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationSceneToolSetRequest
                {
                    Tool = AutomationSceneTool.Brush,
                    Brush = mutationBrush,
                    BrushPanelVisible = true,
                },
                AutomationJsonContext.Default.AutomationSceneToolSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(saveRedone.Revision),
                IdempotencyKey = "capture-e2e-world-brush-configure",
            });
        AutomationInvocationResult brushed = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.BrushApplyMethod,
            JsonSerializer.SerializeToElement(
                new AutomationBrushApplyRequest { X = WorldX, Y = WorldY },
                AutomationJsonContext.Default.AutomationBrushApplyRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(toolSet.Revision),
                IdempotencyKey = "capture-e2e-world-brush-apply",
            });
        AutomationBrushApplyResult brushResult = brushed.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationBrushApplyResult)
            ?? throw new InvalidOperationException("tool.brush.apply 未返回结果。");
        Assert.Equal(1, brushResult.WrittenCells);
        AutomationRuntimeCellInspection mutated = DeserializeCell(
            await InspectCellAsync(client, WorldX, WorldY));
        Assert.Equal(expectedMutatedTemperature, mutated.TemperatureCelsius);

        AutomationInvocationResult loaded = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeSaveSlotLoadMethod,
            JsonSerializer.SerializeToElement(
                new AutomationSaveSlotRequest { SlotId = SlotId },
                AutomationJsonContext.Default.AutomationSaveSlotRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(brushed.Revision),
                IdempotencyKey = "capture-e2e-world-load",
            });
        AutomationSaveSlotOperationResult loadResult = loaded.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationSaveSlotOperationResult)
            ?? throw new InvalidOperationException("runtime.saves.load 未返回结果。");
        Assert.Equal(SlotId, loadResult.Slot.SlotId);
        Assert.Equal(saveResult.WorldSeed, loadResult.WorldSeed);
        AssertWorldCellState(initial, DeserializeCell(await InspectCellAsync(client, WorldX, WorldY)));

        AutomationInvocationResult loadUndone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            loaded.Revision,
            "capture-e2e-world-load-undo");
        Assert.Equal(
            expectedMutatedTemperature,
            DeserializeCell(await InspectCellAsync(client, WorldX, WorldY)).TemperatureCelsius);
        AutomationInvocationResult loadRedone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            loadUndone.Revision,
            "capture-e2e-world-load-redo");
        AssertWorldCellState(initial, DeserializeCell(await InspectCellAsync(client, WorldX, WorldY)));

        AutomationInvocationResult repeatedLoad = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeSaveSlotLoadMethod,
            JsonSerializer.SerializeToElement(
                new AutomationSaveSlotRequest { SlotId = SlotId },
                AutomationJsonContext.Default.AutomationSaveSlotRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(loadRedone.Revision),
                IdempotencyKey = "capture-e2e-world-load-no-change",
            });
        Assert.Equal(
            loadRedone.Revision?.GlobalRevision,
            repeatedLoad.Revision?.GlobalRevision);
        AutomationInvocationResult repeatedLoadUndone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            repeatedLoad.Revision,
            "capture-e2e-world-load-no-change-undo");
        Assert.Equal(
            expectedMutatedTemperature,
            DeserializeCell(await InspectCellAsync(client, WorldX, WorldY)).TemperatureCelsius);
        AutomationInvocationResult repeatedLoadRedone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            repeatedLoadUndone.Revision,
            "capture-e2e-world-load-no-change-redo");
        AssertWorldCellState(initial, DeserializeCell(await InspectCellAsync(client, WorldX, WorldY)));

        _ = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.SceneToolSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationSceneToolSetRequest
                {
                    Tool = toolBefore.Tool,
                    GizmoSpace = toolBefore.GizmoSpace,
                    GridVisible = toolBefore.GridVisible,
                    SnapEnabled = toolBefore.SnapEnabled,
                    MoveSnap = toolBefore.MoveSnap,
                    RotationSnapDegrees = toolBefore.RotationSnapDegrees,
                    ScaleSnap = toolBefore.ScaleSnap,
                    CameraCenterX = toolBefore.CameraCenterX,
                    CameraCenterY = toolBefore.CameraCenterY,
                    CameraCellsPerPixel = toolBefore.CameraCellsPerPixel,
                    Brush = toolBefore.Brush,
                    BrushPanelVisible = toolBefore.BrushPanelVisible,
                    OverlayDock = toolBefore.OverlayDock,
                    OverlayOffsetX = toolBefore.OverlayOffsetX,
                    OverlayOffsetY = toolBefore.OverlayOffsetY,
                },
                AutomationJsonContext.Default.AutomationSceneToolSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(repeatedLoadRedone.Revision),
                IdempotencyKey = "capture-e2e-world-brush-restore",
            });
    }

    private static async Task<AutomationInvocationResult> InspectCellAsync(
        EditorAutomationClient client,
        int worldX,
        int worldY)
    {
        return await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeCellInspectMethod,
            JsonSerializer.SerializeToElement(
                new AutomationRuntimeCellInspectRequest { WorldX = worldX, WorldY = worldY },
                AutomationJsonContext.Default.AutomationRuntimeCellInspectRequest));
    }

    private static AutomationRuntimeCellInspection DeserializeCell(AutomationInvocationResult result)
    {
        return result.Payload?.Deserialize(AutomationJsonContext.Default.AutomationRuntimeCellInspection)
            ?? throw new InvalidOperationException("runtime.cell.inspect 未返回结果。");
    }

    private static async Task ExerciseWorldInspectorAsync(EditorAutomationClient client)
    {
        AutomationInvocationResult get = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeWorldInspectorGetMethod);
        AutomationWorldInspectorSnapshot before = get.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationWorldInspectorSnapshot)
            ?? throw new InvalidOperationException("runtime.world-inspector.get 未返回结果。");
        AutomationWorldInspectorSetRequest request = new()
        {
            FollowSelection = !before.FollowSelection,
            LockedWorldX = checked(before.LockedWorldX + 1),
            LockedWorldY = checked(before.LockedWorldY - 1),
        };
        AutomationInvocationResult changed = await SetWorldInspectorAsync(
            client,
            request,
            get.Revision,
            "runtime-e2e-world-inspector-set");
        AutomationWorldInspectorSnapshot after = changed.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationWorldInspectorSnapshot)
            ?? throw new InvalidOperationException("runtime.world-inspector.set 未返回结果。");
        Assert.Equal(request.FollowSelection, after.FollowSelection);
        Assert.Equal(request.LockedWorldX, after.LockedWorldX);
        Assert.Equal(request.LockedWorldY, after.LockedWorldY);

        AutomationInvocationResult noChange = await SetWorldInspectorAsync(
            client,
            request,
            changed.Revision,
            "runtime-e2e-world-inspector-no-change");
        Assert.Equal(changed.Revision?.GlobalRevision, noChange.Revision?.GlobalRevision);

        AutomationInvocationResult undone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            noChange.Revision,
            "runtime-e2e-world-inspector-undo");
        Assert.Equal(before, await GetWorldInspectorAsync(client));
        AutomationInvocationResult redone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            undone.Revision,
            "runtime-e2e-world-inspector-redo");
        Assert.Equal(after, await GetWorldInspectorAsync(client));
        _ = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            redone.Revision,
            "runtime-e2e-world-inspector-restore");
        Assert.Equal(before, await GetWorldInspectorAsync(client));
    }

    private static async Task<AutomationInvocationResult> SetWorldInspectorAsync(
        EditorAutomationClient client,
        AutomationWorldInspectorSetRequest request,
        AutomationRevisionSnapshot? revision,
        string idempotencyKey)
    {
        return await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeWorldInspectorSetMethod,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationWorldInspectorSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(revision),
                IdempotencyKey = idempotencyKey,
            });
    }

    private static async Task<AutomationWorldInspectorSnapshot> GetWorldInspectorAsync(
        EditorAutomationClient client)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeWorldInspectorGetMethod);
        return result.Payload?.Deserialize(AutomationJsonContext.Default.AutomationWorldInspectorSnapshot)
            ?? throw new InvalidOperationException("runtime.world-inspector.get 未返回结果。");
    }

    private static async Task<AutomationMaterialDefinition[]> ReadAndVerifyMaterialsAsync(
        EditorAutomationClient client)
    {
        AutomationInvocationResult listResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeMaterialListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest
                {
                    PageSize = 500,
                    Sort = [new AutomationSortClause { Field = "name" }],
                },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationMaterialListResponse response = listResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialListResponse)
            ?? throw new InvalidOperationException("runtime.materials.list 未返回 DTO。");
        Assert.Equal(response.Page.Total, response.Items.Length);
        Assert.NotEmpty(response.Items);
        Assert.Equal(
            response.Items.Length,
            response.Items.Select(static material => material.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(response.Items, material =>
        {
            Assert.NotEmpty(material.ResourceId);
            Assert.NotEmpty(material.Name);
            Assert.NotEmpty(material.DisplayName);
            Assert.NotEmpty(material.CellType);
            Assert.True(float.IsFinite(material.HeatCapacity));
            Assert.True(material.HeatCapacity > 0f);
        });

        AutomationMaterialDefinition expected = response.Items[response.Items.Length / 2];
        AutomationInvocationResult getResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeMaterialGetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationMaterialRequest { Name = expected.Name },
                AutomationJsonContext.Default.AutomationMaterialRequest));
        AutomationMaterialDefinition actual = getResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialDefinition)
            ?? throw new InvalidOperationException("runtime.material.get 未返回 DTO。");
        Assert.Equal(expected, actual);
        return response.Items;
    }

    private static async Task ExerciseAssetRefreshAsync(
        EditorAutomationClient client,
        string projectRoot)
    {
        string manifestPath = Path.Combine(
            projectRoot,
            EditorAssetManifestStore.ManifestRelativePath);
        byte[] canonicalBefore = File.ReadAllBytes(manifestPath);
        EditorAssetManifestDocument manifest = JsonSerializer.Deserialize(
                canonicalBefore,
                EditorShellJsonContext.Default.EditorAssetManifestDocument) ??
            throw new InvalidOperationException("Asset manifest 反序列化返回 null。");
        EditorAssetRecordDocument[] assets = manifest.Assets ?? [];
        Assert.True(assets.Length > 1);
        EditorAssetManifestDocument reordered = new()
        {
            FormatVersion = manifest.FormatVersion,
            Assets = [.. assets.Reverse()],
        };
        byte[] nonCanonical = JsonSerializer.SerializeToUtf8Bytes(
            reordered,
            EditorShellJsonContext.Default.EditorAssetManifestDocument);
        Assert.False(nonCanonical.AsSpan().SequenceEqual(canonicalBefore));
        File.WriteAllBytes(manifestPath, nonCanonical);

        AutomationInvocationResult historyBeforeResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.EditorHistoryGetMethod);
        AutomationHistorySnapshot historyBefore = historyBeforeResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationHistorySnapshot)
            ?? throw new InvalidOperationException("editor.history.get 未返回结果。");
        AutomationInvocationResult listBefore = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = 500 },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationAssetListResponse catalogBefore = listBefore.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationAssetListResponse)
            ?? throw new InvalidOperationException("project.assets.list 未返回结果。");
        AutomationInvocationResult refreshedResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetRefreshMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(listBefore.Revision),
                IdempotencyKey = "runtime-e2e-assets-refresh",
            });
        AutomationAssetRefreshResult refreshed = refreshedResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationAssetRefreshResult)
            ?? throw new InvalidOperationException("project.assets.refresh 未返回结果。");
        Assert.True(refreshed.StateChanged);
        Assert.Equal(catalogBefore.Page.Total, refreshed.AssetCount);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(manifestPath));

        AutomationInvocationResult noChange = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetRefreshMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(refreshedResult.Revision),
                IdempotencyKey = "runtime-e2e-assets-refresh-no-change",
            });
        AutomationAssetRefreshResult repeated = noChange.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationAssetRefreshResult)
            ?? throw new InvalidOperationException("幂等 project.assets.refresh 未返回结果。");
        Assert.False(repeated.StateChanged);
        Assert.Equal(refreshedResult.Revision?.GlobalRevision, noChange.Revision?.GlobalRevision);
        AutomationInvocationResult historyAfterResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.EditorHistoryGetMethod);
        AutomationHistorySnapshot historyAfter = historyAfterResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationHistorySnapshot)
            ?? throw new InvalidOperationException("editor.history.get 未返回结果。");
        Assert.Equal(historyBefore.UndoCount, historyAfter.UndoCount);
        Assert.Equal(historyBefore.RedoCount, historyAfter.RedoCount);
    }

    private static async Task ExerciseMaterialEditorAsync(
        EditorAutomationClient client,
        string projectRoot)
    {
        string contentRoot = Path.Combine(projectRoot, "Content");
        string materialsPath = Path.Combine(contentRoot, "materials.json");
        string reactionsPath = Path.Combine(contentRoot, "reactions.json");
        byte[] materialsFileBefore = File.ReadAllBytes(materialsPath);
        byte[] reactionsFileBefore = File.ReadAllBytes(reactionsPath);
        AutomationInvocationResult get = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.MaterialEditorGetMethod);
        AutomationMaterialEditorSnapshot initial = get.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialEditorSnapshot)
            ?? throw new InvalidOperationException("materials.editor.get 未返回结果。");
        Assert.NotEmpty(initial.Document.Materials);
        Assert.All(initial.RuntimeBindings, binding =>
            Assert.Contains(
                initial.Document.Materials,
                row => string.Equals(row.Name, binding.Name, StringComparison.Ordinal)));

        AutomationInvocationResult previewed = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.MaterialEditorPreviewMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(get.Revision),
                IdempotencyKey = "runtime-e2e-material-preview",
            });
        AutomationMaterialEditorPreviewResult preview = previewed.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialEditorPreviewResult)
            ?? throw new InvalidOperationException("materials.editor.preview 未返回结果。");
        Assert.Equal(initial.Document.Materials.Length, preview.MaterialCount);
        Assert.Equal(initial.Document.Reactions.Length, preview.SourceReactionCount);

        AutomationMaterialEditorRow[] changedRows = [.. initial.Document.Materials];
        AutomationMaterialEditorRow originalRow = changedRows[0];
        AutomationMaterialDefinition originalRuntime = await GetRuntimeMaterialAsync(
            client,
            originalRow.Name);
        string displayNameBase = string.IsNullOrWhiteSpace(originalRow.DisplayName)
            ? originalRuntime.DisplayName
            : originalRow.DisplayName;
        string changedDisplayName = displayNameBase + " Automation E2E";
        changedRows[0] = originalRow with { DisplayName = changedDisplayName };
        AutomationMaterialEditorDocument changedDocument = initial.Document with
        {
            Materials = changedRows,
        };
        AutomationInvocationResult set = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.MaterialEditorSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationMaterialEditorSetRequest { Document = changedDocument },
                AutomationJsonContext.Default.AutomationMaterialEditorSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(previewed.Revision),
                IdempotencyKey = "runtime-e2e-material-set",
            });
        AutomationMaterialEditorSnapshot setSnapshot = set.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialEditorSnapshot)
            ?? throw new InvalidOperationException("materials.editor.set 未返回结果。");
        Assert.Equal(changedDisplayName, setSnapshot.Document.Materials[0].DisplayName);

        AutomationInvocationResult applied = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.MaterialEditorApplyMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(set.Revision),
                IdempotencyKey = "runtime-e2e-material-apply",
            });
        AutomationMaterialEditorApplyResult apply = applied.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialEditorApplyResult)
            ?? throw new InvalidOperationException("materials.editor.apply 未返回结果。");
        Assert.Empty(apply.TombstonedMaterialNames);
        Assert.False(apply.CleanupPending, apply.CleanupError);
        Assert.Equal(initial.Document.Materials.Length, apply.PreservedCount);
        byte[] materialsFileAfter = File.ReadAllBytes(materialsPath);
        byte[] reactionsFileAfter = File.ReadAllBytes(reactionsPath);
        Assert.False(materialsFileBefore.AsSpan().SequenceEqual(materialsFileAfter));

        AutomationMaterialDefinition changedRuntime = await GetRuntimeMaterialAsync(
            client,
            originalRow.Name);
        Assert.Equal(changedDisplayName, changedRuntime.DisplayName);

        AutomationInvocationResult undone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            applied.Revision,
            "runtime-e2e-material-undo");
        Assert.Equal(materialsFileBefore, File.ReadAllBytes(materialsPath));
        Assert.Equal(reactionsFileBefore, File.ReadAllBytes(reactionsPath));
        Assert.Equal(
            originalRuntime.DisplayName,
            (await GetRuntimeMaterialAsync(client, originalRow.Name)).DisplayName);

        AutomationInvocationResult redone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            undone.Revision,
            "runtime-e2e-material-redo");
        Assert.Equal(materialsFileAfter, File.ReadAllBytes(materialsPath));
        Assert.Equal(reactionsFileAfter, File.ReadAllBytes(reactionsPath));
        Assert.Equal(
            changedDisplayName,
            (await GetRuntimeMaterialAsync(client, originalRow.Name)).DisplayName);

        AutomationInvocationResult repeated = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.MaterialEditorApplyMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(redone.Revision),
                IdempotencyKey = "runtime-e2e-material-apply-no-change",
            });
        Assert.Equal(
            redone.Revision?.GlobalRevision,
            repeated.Revision?.GlobalRevision);
        Assert.Equal(materialsFileAfter, File.ReadAllBytes(materialsPath));
        Assert.Equal(reactionsFileAfter, File.ReadAllBytes(reactionsPath));

        AutomationInvocationResult restoredApply = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            repeated.Revision,
            "runtime-e2e-material-restore-apply");
        AutomationInvocationResult restoredSet = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            restoredApply.Revision,
            "runtime-e2e-material-restore-set");
        AutomationInvocationResult restoredPreview = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            restoredSet.Revision,
            "runtime-e2e-material-restore-preview");
        Assert.Equal(materialsFileBefore, File.ReadAllBytes(materialsPath));
        Assert.Equal(reactionsFileBefore, File.ReadAllBytes(reactionsPath));
        AutomationInvocationResult restoredGet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.MaterialEditorGetMethod);
        AutomationMaterialEditorSnapshot restored = restoredGet.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialEditorSnapshot)
            ?? throw new InvalidOperationException("restored materials.editor.get 未返回结果。");
        Assert.True(MaterialEditorDocumentsEqual(initial.Document, restored.Document));
        Assert.Equal(initial.Status, restored.Status);

        AutomationInvocationResult evictionSet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.MaterialEditorSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationMaterialEditorSetRequest { Document = initial.Document },
                AutomationJsonContext.Default.AutomationMaterialEditorSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(restoredPreview.Revision),
                IdempotencyKey = "runtime-e2e-material-evict-redo",
            });
        string journalRoot = Path.Combine(
            contentRoot,
            ".pixelengine",
            "material-reaction-journals");
        Assert.False(Directory.Exists(journalRoot) && Directory.EnumerateDirectories(journalRoot).Any());
        _ = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            evictionSet.Revision,
            "runtime-e2e-material-evict-redo-undo");
    }

    private static async Task<AutomationMaterialDefinition> GetRuntimeMaterialAsync(
        EditorAutomationClient client,
        string name)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeMaterialGetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationMaterialRequest { Name = name },
                AutomationJsonContext.Default.AutomationMaterialRequest));
        return result.Payload?.Deserialize(AutomationJsonContext.Default.AutomationMaterialDefinition)
            ?? throw new InvalidOperationException("runtime.material.get 未返回结果。");
    }

    private static bool MaterialEditorDocumentsEqual(
        AutomationMaterialEditorDocument left,
        AutomationMaterialEditorDocument right)
    {
        JsonElement leftJson = JsonSerializer.SerializeToElement(
            left,
            AutomationJsonContext.Default.AutomationMaterialEditorDocument);
        JsonElement rightJson = JsonSerializer.SerializeToElement(
            right,
            AutomationJsonContext.Default.AutomationMaterialEditorDocument);
        return JsonElement.DeepEquals(leftJson, rightJson);
    }

    private static void AssertWorldCellState(
        AutomationRuntimeCellInspection expected,
        AutomationRuntimeCellInspection actual)
    {
        Assert.Equal(expected.MaterialId, actual.MaterialId);
        Assert.Equal(expected.MaterialName, actual.MaterialName);
        Assert.Equal(expected.TemperatureAvailable, actual.TemperatureAvailable);
        Assert.Equal(expected.TemperatureCelsius, actual.TemperatureCelsius);
        Assert.Equal(expected.Settled, actual.Settled);
        Assert.Equal(expected.Burning, actual.Burning);
        Assert.Equal(expected.FreeFalling, actual.FreeFalling);
        Assert.Equal(expected.RigidOwned, actual.RigidOwned);
    }

    private static async Task SelectProjectAssetFolderAndRestoreAsync(
        EditorAutomationClient client)
    {
        AutomationAssetListResponse assets = await ListAssetsAsync(client);
        AutomationAssetInfo asset = Assert.Single(
            assets.Items,
            item => item.Kind == AutomationAssetKind.Scene);
        AutomationInvocationResult get = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectSelectionGetMethod);
        AutomationProjectSelectionSnapshot before = get.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectSelectionSnapshot)
            ?? throw new InvalidOperationException("project.selection.get 未返回 snapshot。");
        AutomationInvocationResult selected = await SetProjectSelectionAsync(
            client,
            new AutomationProjectSelectionSetRequest { AssetId = asset.AssetId },
            get.Revision,
            "capture-e2e-project-selection-asset");
        AutomationProjectSelectionSnapshot assetSelection = selected.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectSelectionSnapshot)
            ?? throw new InvalidOperationException("project.selection.set 未返回 asset snapshot。");
        Assert.Equal(asset.AssetId, assetSelection.AssetId, ignoreCase: true);
        Assert.Equal(asset.Path, assetSelection.AssetPath, ignoreCase: true);
        AutomationInvocationResult noChange = await SetProjectSelectionAsync(
            client,
            new AutomationProjectSelectionSetRequest { AssetId = asset.AssetId },
            selected.Revision,
            "capture-e2e-project-selection-no-change");
        Assert.Equal(selected.Revision?.GlobalRevision, noChange.Revision?.GlobalRevision);

        AutomationInvocationResult folderSelected = await SetProjectSelectionAsync(
            client,
            new AutomationProjectSelectionSetRequest { FolderPath = string.Empty },
            noChange.Revision,
            "capture-e2e-project-selection-folder");
        AutomationProjectSelectionSnapshot folderSelection = folderSelected.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectSelectionSnapshot)
            ?? throw new InvalidOperationException("project.selection.set 未返回 folder snapshot。");
        Assert.Equal(string.Empty, folderSelection.FolderPath);
        Assert.False(string.IsNullOrEmpty(folderSelection.FolderId));
        Assert.Null(folderSelection.AssetId);

        AutomationInvocationResult cleared = await SetProjectSelectionAsync(
            client,
            new AutomationProjectSelectionSetRequest { Clear = true },
            folderSelected.Revision,
            "capture-e2e-project-selection-clear");
        AutomationProjectSelectionSnapshot empty = cleared.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectSelectionSnapshot)
            ?? throw new InvalidOperationException("project.selection.set 未返回 clear snapshot。");
        Assert.Null(empty.AssetId);
        Assert.Null(empty.FolderPath);

        AutomationProjectSelectionSetRequest restore = before.AssetId is not null
            ? new AutomationProjectSelectionSetRequest { AssetId = before.AssetId }
            : before.FolderPath is not null
                ? new AutomationProjectSelectionSetRequest { FolderPath = before.FolderPath }
                : new AutomationProjectSelectionSetRequest { Clear = true };
        _ = await SetProjectSelectionAsync(
            client,
            restore,
            cleared.Revision,
            "capture-e2e-project-selection-restore");
    }

    private static async Task<AutomationInvocationResult> SetProjectSelectionAsync(
        EditorAutomationClient client,
        AutomationProjectSelectionSetRequest request,
        AutomationRevisionSnapshot? revision,
        string idempotencyKey)
    {
        return await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectSelectionSetMethod,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationProjectSelectionSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(revision),
                IdempotencyKey = idempotencyKey,
            });
    }

    private static async Task SetProjectWindowViewAndRestoreAsync(
        EditorAutomationClient client)
    {
        AutomationInvocationResult get = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectWindowGetMethod);
        AutomationProjectWindowSnapshot before = get.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectWindowSnapshot)
            ?? throw new InvalidOperationException("project.window.get 未返回 snapshot。");
        AutomationProjectWindowSetRequest targetRequest = new()
        {
            Search = "project-window-e2e",
            KindFilter = AutomationAssetKind.Script,
            SortMode = before.SortMode == AutomationProjectSortMode.SizeDescending
                ? AutomationProjectSortMode.PathAscending
                : AutomationProjectSortMode.SizeDescending,
            ViewMode = before.ViewMode == AutomationProjectViewMode.Grid
                ? AutomationProjectViewMode.List
                : AutomationProjectViewMode.Grid,
            ThumbnailSize = before.ThumbnailSize == 96f ? 80f : 96f,
        };
        AutomationInvocationResult set = await SetProjectWindowViewAsync(
            client,
            targetRequest,
            get.Revision,
            "capture-e2e-project-window-set");
        AutomationProjectWindowSnapshot changed = set.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectWindowSnapshot)
            ?? throw new InvalidOperationException("project.window.set 未返回 snapshot。");
        Assert.Equal(targetRequest.Search, changed.Search);
        Assert.Equal(targetRequest.KindFilter, changed.KindFilter);
        Assert.Equal(targetRequest.SortMode, changed.SortMode);
        Assert.Equal(targetRequest.ViewMode, changed.ViewMode);
        Assert.Equal(targetRequest.ThumbnailSize, changed.ThumbnailSize);
        Assert.Equal(before.ActiveFolderPath, changed.ActiveFolderPath);

        AutomationInvocationResult noChange = await SetProjectWindowViewAsync(
            client,
            targetRequest,
            set.Revision,
            "capture-e2e-project-window-no-change");
        Assert.Equal(set.Revision?.GlobalRevision, noChange.Revision?.GlobalRevision);

        AutomationProjectWindowSetRequest restore = new()
        {
            Search = before.Search,
            KindFilter = before.KindFilter,
            ClearKindFilter = !before.KindFilter.HasValue,
            SortMode = before.SortMode,
            ViewMode = before.ViewMode,
            ThumbnailSize = before.ThumbnailSize,
        };
        AutomationInvocationResult restoredResult = await SetProjectWindowViewAsync(
            client,
            restore,
            noChange.Revision,
            "capture-e2e-project-window-restore");
        AutomationProjectWindowSnapshot restored = restoredResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectWindowSnapshot)
            ?? throw new InvalidOperationException("project.window.set restore 未返回 snapshot。");
        Assert.Equal(before, restored);
    }

    private static async Task<AutomationInvocationResult> SetProjectWindowViewAsync(
        EditorAutomationClient client,
        AutomationProjectWindowSetRequest request,
        AutomationRevisionSnapshot? revision,
        string idempotencyKey)
    {
        return await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectWindowSetMethod,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationProjectWindowSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(revision),
                IdempotencyKey = idempotencyKey,
            });
    }

    private static async Task OpenCodeProjectAndRestorePreferencesAsync(
        EditorAutomationClient client)
    {
        AutomationInvocationResult preferencesGet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.PreferencesGetMethod);
        AutomationEditorPreferences original = preferencesGet.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationEditorPreferences)
            ?? throw new InvalidOperationException("settings.preferences.get 未返回 DTO。");
        AutomationEditorPreferences testPreference = original with
        {
            ExternalScriptEditor = "where.exe {project}",
        };
        AutomationInvocationResult preferenceSet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.PreferencesSetMethod,
            JsonSerializer.SerializeToElement(
                testPreference,
                AutomationJsonContext.Default.AutomationEditorPreferences),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(preferencesGet.Revision),
                IdempotencyKey = "capture-e2e-code-project-preference",
            });

        AutomationInvocationResult project = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectGetMethod);
        AutomationInvocationResult firstOpen = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectCodeOpenMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(project.Revision),
                IdempotencyKey = "capture-e2e-code-project-open-first",
            });
        AutomationCodeProjectOpenResult first = firstOpen.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationCodeProjectOpenResult)
            ?? throw new InvalidOperationException("project.code.open 未返回 DTO。");
        Assert.True(first.Succeeded, first.Diagnostic);
        Assert.Equal(AutomationCodeEditorKind.Custom, first.EditorKind);
        Assert.True(first.ProjectGenerated);
        Assert.True(first.SolutionGenerated);
        Assert.True(first.FilesChanged);
        string projectPath = Assert.IsType<string>(first.ProjectPath);
        string solutionPath = Assert.IsType<string>(first.SolutionPath);
        Assert.True(File.Exists(projectPath));
        Assert.True(File.Exists(solutionPath));
        DateTime projectWriteTime = File.GetLastWriteTimeUtc(projectPath);
        DateTime solutionWriteTime = File.GetLastWriteTimeUtc(solutionPath);

        AutomationInvocationResult secondOpen = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectCodeOpenMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(firstOpen.Revision),
                IdempotencyKey = "capture-e2e-code-project-open-second",
            });
        AutomationCodeProjectOpenResult second = secondOpen.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationCodeProjectOpenResult)
            ?? throw new InvalidOperationException("第二次 project.code.open 未返回 DTO。");
        Assert.True(second.Succeeded, second.Diagnostic);
        Assert.False(second.FilesChanged);
        Assert.Equal(projectWriteTime, File.GetLastWriteTimeUtc(projectPath));
        Assert.Equal(solutionWriteTime, File.GetLastWriteTimeUtc(solutionPath));

        _ = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.PreferencesSetMethod,
            JsonSerializer.SerializeToElement(
                original,
                AutomationJsonContext.Default.AutomationEditorPreferences),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(preferenceSet.Revision),
                IdempotencyKey = "capture-e2e-code-project-preference-restore",
            });
    }

    private static string FormatAutomationError(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is AutomationRemoteException remote)
            {
                AutomationInternalErrorDetails? details = remote.Error.Details?.Deserialize(
                    AutomationJsonContext.Default.AutomationInternalErrorDetails);
                if (details is null)
                {
                    return $"{remote.Error.Code}: {remote.Error.Message}";
                }

                string causes = string.Join(
                    Environment.NewLine,
                    details.Causes.Select(static cause =>
                        $"  {cause.ExceptionType}: {cause.Message}"));
                return $"{remote.Error.Code}: {details.ExceptionType}: {details.Message}" +
                    (causes.Length == 0 ? string.Empty : Environment.NewLine + causes);
            }

            current = current.InnerException;
        }

        return exception.ToString();
    }

    private static void AssertNoProjectLayoutLeak(string projectRoot, string stage)
    {
        string path = Path.Combine(projectRoot, "content", "imgui.ini");
        Assert.False(File.Exists(path), $"{stage}: Editor layout 泄漏到 Project Content：{path}");
    }

    private static async Task<AutomationInvocationResult> ExerciseWorkspaceProjectTransitionsAsync(
        EditorAutomationClient client,
        string root,
        string originalProjectRoot)
    {
        AutomationInvocationResult workspaceBefore = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceGetMethod);
        AutomationGameObjectCreateRequest dirtyRequest = new()
        {
            Name = "Dirty Guard Probe",
        };
        AutomationInvocationResult dirty = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.GameObjectCreateMethod,
            JsonSerializer.SerializeToElement(
                dirtyRequest,
                AutomationJsonContext.Default.AutomationGameObjectCreateRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(workspaceBefore.Revision),
                IdempotencyKey = "capture-e2e-workspace-dirty",
            });

        const string CancelledName = "Cancelled Automation Project";
        string cancelledRoot = Path.Combine(root, CancelledName);
        AutomationInvocationResult pending = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceProjectCreateMethod,
            JsonSerializer.SerializeToElement(
                new AutomationProjectCreateRequest
                {
                    LocationPath = root,
                    Name = CancelledName,
                },
                AutomationJsonContext.Default.AutomationProjectCreateRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(dirty.Revision),
                IdempotencyKey = "capture-e2e-workspace-create-cancelled",
            });
        AutomationTransitionResult pendingTransition = pending.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransitionResult)
            ?? throw new InvalidOperationException("workspace.project.create 未返回 pending transition。");
        Assert.Equal("confirmationRequired", pendingTransition.Status);
        Assert.Equal("CreateProject", pendingTransition.Kind);
        Assert.Equal(cancelledRoot, pendingTransition.Target, ignoreCase: true);
        Assert.False(Directory.Exists(cancelledRoot));

        AutomationInvocationResult cancelled = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceTransitionResolveMethod,
            JsonSerializer.SerializeToElement(
                new AutomationTransitionResolveRequest
                {
                    Decision = AutomationTransitionDecision.Cancel,
                },
                AutomationJsonContext.Default.AutomationTransitionResolveRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(pending.Revision),
                IdempotencyKey = "capture-e2e-workspace-create-cancel",
            });
        AutomationTransitionResult cancelledTransition = cancelled.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransitionResult)
            ?? throw new InvalidOperationException("workspace transition cancel 未返回结果。");
        Assert.Equal("cancelled", cancelledTransition.Status);
        Assert.False(Directory.Exists(cancelledRoot));
        Assert.Empty(Directory.EnumerateDirectories(
            root,
            ".pixelengine-create-*",
            SearchOption.TopDirectoryOnly));

        AutomationInvocationResult history = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.EditorHistoryGetMethod);
        _ = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(history.Revision),
                IdempotencyKey = "capture-e2e-workspace-dirty-undo",
            });
        AutomationInvocationResult cleanWorkspace = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceGetMethod);
        AutomationWorkspaceSnapshot clean = cleanWorkspace.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationWorkspaceSnapshot)
            ?? throw new InvalidOperationException("workspace.get 未返回 clean snapshot。");
        Assert.False(clean.SceneDirty);

        const string CreatedName = "Created Automation Project";
        string createdRoot = Path.Combine(root, CreatedName);
        AutomationInvocationResult created = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceProjectCreateMethod,
            JsonSerializer.SerializeToElement(
                new AutomationProjectCreateRequest
                {
                    LocationPath = root,
                    Name = CreatedName,
                },
                AutomationJsonContext.Default.AutomationProjectCreateRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(cleanWorkspace.Revision),
                IdempotencyKey = "capture-e2e-workspace-create",
            });
        Assert.Equal(
            "executed",
            created.Payload?.Deserialize(AutomationJsonContext.Default.AutomationTransitionResult)?.Status);
        AutomationInvocationResult createdWorkspace = await WaitForWorkspaceAsync(
            client,
            snapshot => snapshot.ProjectOpen &&
                string.Equals(snapshot.ProjectRoot, createdRoot, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(createdRoot, EditorProject.ProjectFileName)));

        AutomationInvocationResult opened = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceProjectOpenMethod,
            JsonSerializer.SerializeToElement(
                new AutomationProjectOpenRequest { Path = originalProjectRoot },
                AutomationJsonContext.Default.AutomationProjectOpenRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(createdWorkspace.Revision),
                IdempotencyKey = "capture-e2e-workspace-open-original",
            });
        Assert.Equal(
            "executed",
            opened.Payload?.Deserialize(AutomationJsonContext.Default.AutomationTransitionResult)?.Status);
        AutomationInvocationResult originalWorkspace = await WaitForWorkspaceAsync(
            client,
            snapshot => snapshot.ProjectOpen &&
                string.Equals(snapshot.ProjectRoot, originalProjectRoot, StringComparison.OrdinalIgnoreCase));

        AutomationInvocationResult closed = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceProjectCloseMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(originalWorkspace.Revision),
                IdempotencyKey = "capture-e2e-workspace-close",
            });
        Assert.Equal(
            "executed",
            closed.Payload?.Deserialize(AutomationJsonContext.Default.AutomationTransitionResult)?.Status);
        AutomationInvocationResult emptyWorkspace = await WaitForWorkspaceAsync(
            client,
            static snapshot => !snapshot.ProjectOpen);

        _ = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceProjectOpenMethod,
            JsonSerializer.SerializeToElement(
                new AutomationProjectOpenRequest { Path = originalProjectRoot },
                AutomationJsonContext.Default.AutomationProjectOpenRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(emptyWorkspace.Revision),
                IdempotencyKey = "capture-e2e-workspace-reopen-original",
            });
        return await WaitForWorkspaceAsync(
            client,
            snapshot => snapshot.ProjectOpen &&
                string.Equals(snapshot.ProjectRoot, originalProjectRoot, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ExerciseRecentProjectsAndShortcutsAsync(
        EditorAutomationClient client,
        string root,
        string originalProjectRoot)
    {
        AutomationInvocationResult firstPageResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = 1 },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationRecentProjectListResponse firstPage = firstPageResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRecentProjectListResponse)
            ?? throw new InvalidOperationException("workspace.recent.list 未返回首屏。");
        _ = Assert.Single(firstPage.Items);
        Assert.True(firstPage.Page.Total >= 2);
        Assert.NotNull(firstPage.Page.NextCursor);

        AutomationInvocationResult secondPageResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest
                {
                    PageSize = 1,
                    Cursor = firstPage.Page.NextCursor,
                },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationRecentProjectListResponse secondPage = secondPageResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRecentProjectListResponse)
            ?? throw new InvalidOperationException("workspace.recent.list 未返回第二屏。");
        _ = Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].ProjectId, secondPage.Items[0].ProjectId);

        AutomationInvocationResult listedResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = RecentProjectsStore.MaxEntries },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationRecentProjectListResponse listed = listedResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRecentProjectListResponse)
            ?? throw new InvalidOperationException("workspace.recent.list 未返回完整列表。");
        AutomationRecentProjectInfo original = Assert.Single(
            listed.Items,
            item => string.Equals(
                item.RootPath,
                originalProjectRoot,
                StringComparison.OrdinalIgnoreCase));
        Assert.True(original.ProjectFileExists);
        string createdRoot = Path.Combine(root, "Created Automation Project");
        AutomationRecentProjectInfo created = Assert.Single(
            listed.Items,
            item => string.Equals(item.RootPath, createdRoot, StringComparison.OrdinalIgnoreCase));
        Assert.True(created.ProjectFileExists);
        Assert.False(created.Favorite);

        AutomationRecentProjectFavoriteSetRequest favoriteRequest = new()
        {
            ProjectId = created.ProjectId,
            Favorite = true,
        };
        JsonElement favoritePayload = JsonSerializer.SerializeToElement(
            favoriteRequest,
            AutomationJsonContext.Default.AutomationRecentProjectFavoriteSetRequest);
        AutomationInvocationOptions favoriteOptions = new()
        {
            ExpectedRevision = ToResourcePrecondition(listedResult.Revision),
            IdempotencyKey = "capture-e2e-recent-favorite",
        };
        AutomationInvocationResult favorited = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod,
            favoritePayload,
            favoriteOptions);
        AutomationRecentProjectMutationResult favoriteResult = favorited.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRecentProjectMutationResult)
            ?? throw new InvalidOperationException("workspace.recent.favorite.set 未返回结果。");
        Assert.False(favoriteResult.Removed);
        Assert.True(favoriteResult.Favorite);
        AutomationInvocationResult favoriteReplay = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod,
            favoritePayload,
            favoriteOptions);
        Assert.Equal(favorited.Revision?.GlobalRevision, favoriteReplay.Revision?.GlobalRevision);

        AutomationInvocationResult afterFavoriteResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = RecentProjectsStore.MaxEntries },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationRecentProjectListResponse afterFavorite = afterFavoriteResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRecentProjectListResponse)
            ?? throw new InvalidOperationException("收藏后 workspace.recent.list 未返回结果。");
        Assert.True(Assert.Single(
            afterFavorite.Items,
            item => item.ProjectId == created.ProjectId).Favorite);

        AutomationInvocationResult removedResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentRemoveMethod,
            JsonSerializer.SerializeToElement(
                new AutomationRecentProjectRemoveRequest { ProjectId = created.ProjectId },
                AutomationJsonContext.Default.AutomationRecentProjectRemoveRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(afterFavoriteResult.Revision),
                IdempotencyKey = "capture-e2e-recent-remove",
            });
        AutomationRecentProjectMutationResult removed = removedResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRecentProjectMutationResult)
            ?? throw new InvalidOperationException("workspace.recent.remove 未返回结果。");
        Assert.True(removed.Removed);
        Assert.Null(removed.Favorite);
        AutomationInvocationResult afterRemoveResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WorkspaceRecentListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = RecentProjectsStore.MaxEntries },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationRecentProjectListResponse afterRemove = afterRemoveResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRecentProjectListResponse)
            ?? throw new InvalidOperationException("移除后 workspace.recent.list 未返回结果。");
        Assert.DoesNotContain(afterRemove.Items, item => item.ProjectId == created.ProjectId);
        Assert.Contains(afterRemove.Items, item => item.ProjectId == original.ProjectId);

        AutomationInvocationResult shortcutsResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ShortcutListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = 100 },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationShortcutListResponse shortcuts = shortcutsResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationShortcutListResponse)
            ?? throw new InvalidOperationException("settings.shortcuts.list 未返回结果。");
        Assert.Equal(Enum.GetValues<EditorShortcutCommand>().Length, shortcuts.Items.Length);
        Assert.Equal(shortcuts.Items.Length, shortcuts.Items.Select(static item => item.UiCommandId).Distinct().Count());
        AutomationShortcutInfo saveAs = Assert.Single(
            shortcuts.Items,
            static item => item.UiCommandId == "shortcut.ctrl-shift-s");
        Assert.Equal("S", saveAs.Key);
        Assert.True(saveAs.Control);
        Assert.True(saveAs.Shift);
        Assert.Equal("Ctrl+Shift+S", saveAs.DisplayText);
    }

    private static async Task<AutomationInvocationResult> WaitForWorkspaceAsync(
        EditorAutomationClient client,
        Func<AutomationWorkspaceSnapshot, bool> predicate)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            AutomationInvocationResult result = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.WorkspaceGetMethod);
            AutomationWorkspaceSnapshot snapshot = result.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationWorkspaceSnapshot)
                ?? throw new InvalidOperationException("workspace.get 未返回 snapshot。");
            if (predicate(snapshot))
            {
                return result;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("等待 Editor workspace 转场完成超时。");
    }

    private static async Task ToggleProfilerVSyncAndRestoreAsync(EditorAutomationClient client)
    {
        AutomationInvocationResult panelsResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.WindowPanelListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = 500 },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationPanelListResponse panels = panelsResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationPanelListResponse)
            ?? throw new InvalidOperationException("window.panels.list 未返回快照。");
        AutomationPanelInfo profilerPanel = Assert.Single(
            panels.Items,
            panel => string.Equals(panel.Id, EditorPanelIds.Profiler, StringComparison.Ordinal));
        AutomationRevisionSnapshot? panelRevision = panelsResult.Revision;
        if (!profilerPanel.Visible)
        {
            _ = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.WindowPanelSetMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationPanelSetRequest
                    {
                        PanelId = EditorPanelIds.Profiler,
                        Visible = true,
                    },
                    AutomationJsonContext.Default.AutomationPanelSetRequest),
                new AutomationInvocationOptions
                {
                    ExpectedRevision = ToPrecondition(panelRevision),
                    IdempotencyKey = "capture-e2e-profiler-show",
                });
        }

        AutomationInvocationResult? get = null;
        AutomationProfilerSnapshot? before = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            get = await client.InvokeDetailedAsync(AutomationProtocolConstants.ProfilerGetMethod);
            before = get.Payload?.Deserialize(AutomationJsonContext.Default.AutomationProfilerSnapshot)
                ?? throw new InvalidOperationException("profiler.get 未返回快照。");
            if (before.History.Length >= 3)
            {
                break;
            }

            await Task.Delay(20);
        }

        AutomationInvocationResult profilerGet = get ??
            throw new InvalidOperationException("profiler.get 未执行。");
        AutomationProfilerSnapshot profilerBefore = before ??
            throw new InvalidOperationException("profiler.get 未返回历史。");
        Assert.True(profilerBefore.History.Length >= 3);
        Assert.Equal(512, profilerBefore.HistoryCapacity);
        Assert.True(profilerBefore.CapturedSampleCount >= profilerBefore.History.Length);
        Assert.True(profilerBefore.History.Zip(
            profilerBefore.History.Skip(1),
            static (left, right) => left.FrameIndex < right.FrameIndex).All(static ordered => ordered));
        Assert.InRange(profilerBefore.FrameStatistics.SampleCount, 0, profilerBefore.History.Length);
        before = profilerBefore;
        Assert.True(before.CanToggleVSync);
        Assert.NotEmpty(before.MainPhases);
        Assert.NotEmpty(before.SubPhases);
        Assert.True(double.IsFinite(before.CpuWorkMilliseconds));
        Assert.True(double.IsFinite(before.EffectiveFrameMilliseconds));

        AutomationInvocationResult changed = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProfilerVSyncSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationProfilerVSyncSetRequest { Enabled = !before.VSyncEnabled },
                AutomationJsonContext.Default.AutomationProfilerVSyncSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(profilerGet.Revision),
                IdempotencyKey = "capture-e2e-profiler-vsync-change",
            });
        AutomationProfilerSnapshot changedSnapshot = changed.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProfilerSnapshot)
            ?? throw new InvalidOperationException("profiler.vsync.set 未返回变更快照。");
        Assert.Equal(!before.VSyncEnabled, changedSnapshot.VSyncEnabled);

        AutomationInvocationResult restored = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProfilerVSyncSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationProfilerVSyncSetRequest { Enabled = before.VSyncEnabled },
                AutomationJsonContext.Default.AutomationProfilerVSyncSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(changed.Revision),
                IdempotencyKey = "capture-e2e-profiler-vsync-restore",
            });
        AutomationProfilerSnapshot restoredSnapshot = restored.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProfilerSnapshot)
            ?? throw new InvalidOperationException("profiler.vsync.set 未返回恢复快照。");
        Assert.Equal(before.VSyncEnabled, restoredSnapshot.VSyncEnabled);
        if (!profilerPanel.Visible)
        {
            _ = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.WindowPanelSetMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationPanelSetRequest
                    {
                        PanelId = EditorPanelIds.Profiler,
                        Visible = false,
                    },
                    AutomationJsonContext.Default.AutomationPanelSetRequest),
                new AutomationInvocationOptions
                {
                    ExpectedRevision = ToPrecondition(restored.Revision),
                    IdempotencyKey = "capture-e2e-profiler-hide",
                });
        }
    }

    private static async Task ToggleSimulationRateAndRestoreAsync(EditorAutomationClient client)
    {
        AutomationInvocationResult get = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeSimulationGetMethod);
        AutomationRuntimeSimulationSnapshot before = get.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRuntimeSimulationSnapshot)
            ?? throw new InvalidOperationException("runtime.simulation.get 未返回快照。");
        Assert.True(
            Math.Abs(before.SimulationHz - 30.0) < 0.001 ||
            Math.Abs(before.SimulationHz - 60.0) < 0.001,
            $"Unexpected simulation rate: {before.SimulationHz}");
        double target = Math.Abs(before.SimulationHz - 30.0) < 0.001 ? 60.0 : 30.0;
        AutomationInvocationResult changed = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeSimulationSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationRuntimeSimulationSetRequest { SimulationHz = target },
                AutomationJsonContext.Default.AutomationRuntimeSimulationSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(get.Revision),
                IdempotencyKey = "capture-e2e-simulation-change",
            });
        AutomationRuntimeSimulationSnapshot changedSnapshot = changed.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRuntimeSimulationSnapshot)
            ?? throw new InvalidOperationException("runtime.simulation.set 未返回变更快照。");
        Assert.Equal(target, changedSnapshot.SimulationHz);

        AutomationInvocationResult restored = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeSimulationSetMethod,
            JsonSerializer.SerializeToElement(
                new AutomationRuntimeSimulationSetRequest { SimulationHz = before.SimulationHz },
                AutomationJsonContext.Default.AutomationRuntimeSimulationSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(changed.Revision),
                IdempotencyKey = "capture-e2e-simulation-restore",
            });
        AutomationRuntimeSimulationSnapshot restoredSnapshot = restored.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRuntimeSimulationSnapshot)
            ?? throw new InvalidOperationException("runtime.simulation.set 未返回恢复快照。");
        Assert.Equal(before.SimulationHz, restoredSnapshot.SimulationHz);
    }

    private static async Task ToggleRuntimeTuningAndRestoreAsync(EditorAutomationClient client)
    {
        AutomationInvocationResult physicsGet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimePhysicsGetMethod);
        AutomationRuntimePhysicsSnapshot physics = physicsGet.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRuntimePhysicsSnapshot)
            ?? throw new InvalidOperationException("runtime.physics.get 未返回快照。");
        AutomationRuntimePhysicsSetRequest physicsChangedRequest = new()
        {
            SubStepCount = physics.SubStepCount == 4 ? 5 : 4,
            FragmentPixelThreshold = physics.FragmentPixelThreshold,
            GravityX = physics.GravityX,
            GravityY = physics.GravityY,
        };
        AutomationInvocationResult physicsChanged = await InvokeRuntimeTuningAsync(
            client,
            AutomationProtocolConstants.RuntimePhysicsSetMethod,
            physicsChangedRequest,
            AutomationJsonContext.Default.AutomationRuntimePhysicsSetRequest,
            physicsGet,
            "capture-e2e-physics-change");
        Assert.Equal(
            physicsChangedRequest.SubStepCount,
            physicsChanged.Payload?.Deserialize(AutomationJsonContext.Default.AutomationRuntimePhysicsSnapshot)
                ?.SubStepCount);
        AutomationInvocationResult physicsNoChange = await InvokeRuntimeTuningAsync(
            client,
            AutomationProtocolConstants.RuntimePhysicsSetMethod,
            physicsChangedRequest,
            AutomationJsonContext.Default.AutomationRuntimePhysicsSetRequest,
            physicsChanged,
            "capture-e2e-physics-no-change");
        Assert.Equal(physicsChanged.Revision?.GlobalRevision, physicsNoChange.Revision?.GlobalRevision);
        AutomationRuntimePhysicsSetRequest physicsRestore = new()
        {
            SubStepCount = physics.SubStepCount,
            FragmentPixelThreshold = physics.FragmentPixelThreshold,
            GravityX = physics.GravityX,
            GravityY = physics.GravityY,
        };
        AutomationInvocationResult physicsRestored = await InvokeRuntimeTuningAsync(
            client,
            AutomationProtocolConstants.RuntimePhysicsSetMethod,
            physicsRestore,
            AutomationJsonContext.Default.AutomationRuntimePhysicsSetRequest,
            physicsNoChange,
            "capture-e2e-physics-restore");
        Assert.Equal(
            physics.SubStepCount,
            physicsRestored.Payload?.Deserialize(AutomationJsonContext.Default.AutomationRuntimePhysicsSnapshot)
                ?.SubStepCount);

        AutomationInvocationResult particlesGet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeParticlesGetMethod);
        AutomationRuntimeParticlesSnapshot particles = particlesGet.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRuntimeParticlesSnapshot)
            ?? throw new InvalidOperationException("runtime.particles.get 未返回快照。");
        AutomationRuntimeParticlesSetRequest particlesChangedRequest = new()
        {
            MaxCount = particles.MaxCount,
            GravityPerTick = particles.GravityPerTick,
            MaxLifetimeTicks = particles.MaxLifetimeTicks,
            DepositSpeedEpsilon = particles.DepositSpeedEpsilon,
            EjectionImpulseScale = particles.EjectionImpulseScale + 0.125f,
            MaxEjectionPerTick = particles.MaxEjectionPerTick,
        };
        AutomationInvocationResult particlesChanged = await InvokeRuntimeTuningAsync(
            client,
            AutomationProtocolConstants.RuntimeParticlesSetMethod,
            particlesChangedRequest,
            AutomationJsonContext.Default.AutomationRuntimeParticlesSetRequest,
            particlesGet,
            "capture-e2e-particles-change");
        Assert.Equal(
            particlesChangedRequest.EjectionImpulseScale,
            particlesChanged.Payload?.Deserialize(AutomationJsonContext.Default.AutomationRuntimeParticlesSnapshot)
                ?.EjectionImpulseScale);
        AutomationRuntimeParticlesSetRequest particlesRestore = new()
        {
            MaxCount = particles.MaxCount,
            GravityPerTick = particles.GravityPerTick,
            MaxLifetimeTicks = particles.MaxLifetimeTicks,
            DepositSpeedEpsilon = particles.DepositSpeedEpsilon,
            EjectionImpulseScale = particles.EjectionImpulseScale,
            MaxEjectionPerTick = particles.MaxEjectionPerTick,
        };
        AutomationInvocationResult particlesRestored = await InvokeRuntimeTuningAsync(
            client,
            AutomationProtocolConstants.RuntimeParticlesSetMethod,
            particlesRestore,
            AutomationJsonContext.Default.AutomationRuntimeParticlesSetRequest,
            particlesChanged,
            "capture-e2e-particles-restore");
        Assert.Equal(
            particles.EjectionImpulseScale,
            particlesRestored.Payload?.Deserialize(AutomationJsonContext.Default.AutomationRuntimeParticlesSnapshot)
                ?.EjectionImpulseScale);

        AutomationInvocationResult lightingGet = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.RuntimeLightingGetMethod);
        AutomationRuntimeLightingSnapshot lighting = lightingGet.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationRuntimeLightingSnapshot)
            ?? throw new InvalidOperationException("runtime.lighting.get 未返回快照。");
        AutomationRuntimeLightingSetRequest lightingChangedRequest = ToLightingRequest(
            lighting,
            !lighting.FogOfWarEnabled);
        AutomationInvocationResult lightingChanged = await InvokeRuntimeTuningAsync(
            client,
            AutomationProtocolConstants.RuntimeLightingSetMethod,
            lightingChangedRequest,
            AutomationJsonContext.Default.AutomationRuntimeLightingSetRequest,
            lightingGet,
            "capture-e2e-lighting-change");
        Assert.Equal(
            !lighting.FogOfWarEnabled,
            lightingChanged.Payload?.Deserialize(AutomationJsonContext.Default.AutomationRuntimeLightingSnapshot)
                ?.FogOfWarEnabled);
        AutomationInvocationResult lightingRestored = await InvokeRuntimeTuningAsync(
            client,
            AutomationProtocolConstants.RuntimeLightingSetMethod,
            ToLightingRequest(lighting, lighting.FogOfWarEnabled),
            AutomationJsonContext.Default.AutomationRuntimeLightingSetRequest,
            lightingChanged,
            "capture-e2e-lighting-restore");
        Assert.Equal(
            lighting.FogOfWarEnabled,
            lightingRestored.Payload?.Deserialize(AutomationJsonContext.Default.AutomationRuntimeLightingSnapshot)
                ?.FogOfWarEnabled);
    }

    private static AutomationRuntimeLightingSetRequest ToLightingRequest(
        AutomationRuntimeLightingSnapshot snapshot,
        bool fogOfWarEnabled)
    {
        return new AutomationRuntimeLightingSetRequest
        {
            Quality = snapshot.Quality,
            BloomEnabled = snapshot.BloomEnabled,
            BloomThreshold = snapshot.BloomThreshold,
            BloomIntensity = snapshot.BloomIntensity,
            FogOfWarEnabled = fogOfWarEnabled,
            DitherEnabled = snapshot.DitherEnabled,
            Gamma = snapshot.Gamma,
            RadianceCascadesEnabled = snapshot.RadianceCascadesEnabled,
        };
    }

    private static ValueTask<AutomationInvocationResult> InvokeRuntimeTuningAsync<TRequest>(
        EditorAutomationClient client,
        string method,
        TRequest request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> typeInfo,
        AutomationInvocationResult previous,
        string idempotencyKey)
    {
        return client.InvokeDetailedAsync(
            method,
            JsonSerializer.SerializeToElement(request, typeInfo),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(previous.Revision),
                IdempotencyKey = idempotencyKey,
            });
    }

    /// <summary>真实 Demo Play session 的 runtime Transform/field 临时编辑会在 Stop 后完整恢复。</summary>
    [Fact]
    [Trait("Category", "NativeSmoke")]
    public async Task ExternalEditorMutatesAndRestoresRuntimeState()
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "pixelengine-automation-runtime-" + Guid.NewGuid().ToString("N"));
        string projectRoot = Path.Combine(root, "Project");
        string discoveryRoot = Path.Combine(root, "discovery");
        string artifactRoot = Path.Combine(root, "artifacts");
        string logRoot = Path.Combine(root, "logs");
        CopyDemoProject(projectRoot);
        _ = Directory.CreateDirectory(logRoot);
        string shellPath = Path.Combine(AppContext.BaseDirectory, "PixelEngine.Editor.Shell.exe");
        Assert.True(File.Exists(shellPath), shellPath);
        using Process process = new()
        {
            StartInfo = CreateStartInfo(
                shellPath,
                projectRoot,
                discoveryRoot,
                artifactRoot,
                logRoot),
        };
        Assert.True(process.Start());
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            AutomationDiscoveredInstance instance = await WaitForProjectInstanceAsync(
                process,
                discoveryRoot,
                stdout,
                stderr);
            await using EditorAutomationClient client = await EditorAutomationClient.ConnectAsync(
                instance,
                new AutomationClientOptions
                {
                    ClientInstanceId = "runtime-e2e",
                    ClientName = "hosting-tests",
                    ClientVersion = "1.0",
                    RequestedScopes =
                    [
                        AutomationScopes.EditorRead,
                        AutomationScopes.EditorControl,
                        AutomationScopes.ProjectWrite,
                    ],
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    RequestTimeout = TimeSpan.FromSeconds(60),
                });

            await ExerciseAssetRefreshAsync(client, projectRoot);
            AutomationMaterialDefinition[] materials = await ReadAndVerifyMaterialsAsync(client);
            await ExerciseMaterialEditorAsync(client, projectRoot);
            await ExerciseWorldSaveLoadAsync(client, projectRoot, materials);
            await ExerciseWorldInspectorAsync(client);
            AutomationInvocationResult playGet = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.PlayGetMethod);
            AutomationInvocationResult entered = await EnterPlayAsync(
                client,
                playGet.Revision,
                "runtime-e2e-enter-first");
            (AutomationRuntimeEntity[] entities, _) = await WaitForRuntimeEntitiesAsync(client);
            AutomationInvocationResult bodiesResult = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.RuntimeBodyListMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationPageRequest { PageSize = 500 },
                    AutomationJsonContext.Default.AutomationPageRequest));
            AutomationRuntimeBodyListResponse bodies = bodiesResult.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationRuntimeBodyListResponse)
                ?? throw new InvalidOperationException("runtime.bodies.list 未返回 DTO。");
            Assert.Equal(bodies.Page.Total, bodies.Items.Length);
            if (bodies.Items.FirstOrDefault() is { } firstBody)
            {
                AutomationInvocationResult bodyGet = await client.InvokeDetailedAsync(
                    AutomationProtocolConstants.RuntimeBodyGetMethod,
                    JsonSerializer.SerializeToElement(
                        new AutomationRuntimeBodyRequest { BodyId = firstBody.BodyId },
                        AutomationJsonContext.Default.AutomationRuntimeBodyRequest));
                AutomationRuntimeBody currentBody = bodyGet.Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationRuntimeBody)
                    ?? throw new InvalidOperationException("runtime.body.get 未返回 DTO。");
                Assert.Equal(firstBody.BodyId, currentBody.BodyId);
                Assert.Equal(firstBody.BodyKey, currentBody.BodyKey);
                Assert.Equal(firstBody.MaskWidth, currentBody.MaskWidth);
                Assert.Equal(firstBody.MaskHeight, currentBody.MaskHeight);
                Assert.Equal(firstBody.SolidPixelCount, currentBody.SolidPixelCount);
                Assert.True(float.IsFinite(currentBody.PositionX));
                Assert.True(float.IsFinite(currentBody.PositionY));
                Assert.True(float.IsFinite(currentBody.RotationSin));
                Assert.True(float.IsFinite(currentBody.RotationCos));
                Assert.True(float.IsFinite(currentBody.LinearVelocityX));
                Assert.True(float.IsFinite(currentBody.LinearVelocityY));
                Assert.True(float.IsFinite(currentBody.AngularVelocityRadiansPerSecond));

                int inspectX = checked((int)MathF.Round(currentBody.PositionX));
                int inspectY = checked((int)MathF.Round(currentBody.PositionY));
                AutomationInvocationResult cellResult = await client.InvokeDetailedAsync(
                    AutomationProtocolConstants.RuntimeCellInspectMethod,
                    JsonSerializer.SerializeToElement(
                        new AutomationRuntimeCellInspectRequest
                        {
                            WorldX = inspectX,
                            WorldY = inspectY,
                        },
                        AutomationJsonContext.Default.AutomationRuntimeCellInspectRequest));
                AutomationRuntimeCellInspection cell = cellResult.Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationRuntimeCellInspection)
                    ?? throw new InvalidOperationException("runtime.cell.inspect 未返回 DTO。");
                Assert.Equal(inspectX, cell.WorldX);
                Assert.Equal(inspectY, cell.WorldY);
                Assert.NotEmpty(cell.MaterialName);
            }

            AutomationRuntimeEntity levelDirector = Assert.Single(
                entities,
                entity => entity.Components.Any(component =>
                    component.TypeName.EndsWith(".LevelDirector", StringComparison.Ordinal)));
            AutomationRuntimeTransform baselineTransform = levelDirector.Transform ??
                throw new InvalidOperationException("LevelDirector runtime entity 缺少 Transform。");
            AutomationRuntimeComponent levelComponent = Assert.Single(
                levelDirector.Components,
                component => component.TypeName.EndsWith(".LevelDirector", StringComparison.Ordinal));
            AutomationInspectorField baselineField = Assert.Single(
                levelComponent.Fields,
                field => field.CanWrite &&
                    field.ValueType == typeof(bool).FullName &&
                    string.Equals(field.Name, "BuildScriptEntities", StringComparison.Ordinal));

            AutomationInvocationResult transformed = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.RuntimeEntityTransformSetMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationRuntimeTransformSetRequest
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        EntityId = levelDirector.EntityId,
                        X = baselineTransform.X + 7f,
                        Y = baselineTransform.Y - 3f,
                        RotationRadians = baselineTransform.RotationRadians + 0.125f,
                        ScaleX = baselineTransform.ScaleX + 0.25f,
                        ScaleY = baselineTransform.ScaleY + 0.5f,
                    },
                    AutomationJsonContext.Default.AutomationRuntimeTransformSetRequest),
                new AutomationInvocationOptions
                {
                    ExpectedRevision = ToResourcePrecondition(entered.Revision),
                    IdempotencyKey = "runtime-e2e-transform",
                });
            AutomationRuntimeEntity transformedEntity = transformed.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationRuntimeEntity)
                ?? throw new InvalidOperationException("runtime.entity.transform.set 未返回 entity。");
            Assert.Equal(baselineTransform.X + 7f, transformedEntity.Transform?.X);

            AutomationInvocationResult currentEntity = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.RuntimeEntityGetMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationRuntimeEntityRequest
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        EntityId = levelDirector.EntityId,
                    },
                    AutomationJsonContext.Default.AutomationRuntimeEntityRequest));
            string targetBool = (!bool.Parse(baselineField.Value)).ToString();
            AutomationInvocationResult fieldSet = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.RuntimeComponentFieldSetMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationRuntimeComponentFieldSetRequest
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        EntityId = levelDirector.EntityId,
                        ComponentId = levelComponent.ComponentId,
                        FieldName = baselineField.Name,
                        Value = targetBool,
                    },
                    AutomationJsonContext.Default.AutomationRuntimeComponentFieldSetRequest),
                new AutomationInvocationOptions
                {
                    ExpectedRevision = ToResourcePrecondition(currentEntity.Revision),
                    IdempotencyKey = "runtime-e2e-field",
                });
            AutomationRuntimeEntity fieldEntity = fieldSet.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationRuntimeEntity)
                ?? throw new InvalidOperationException("runtime.component.field.set 未返回 entity。");
            Assert.Equal(
                targetBool,
                Assert.Single(
                    Assert.Single(
                        fieldEntity.Components,
                        component => component.ComponentId == levelComponent.ComponentId).Fields,
                    field => field.Name == baselineField.Name).Value);

            AutomationInvocationResult stopped = await StopPlayAsync(
                client,
                fieldSet.Revision,
                "runtime-e2e-stop-first");
            _ = await EnterPlayAsync(client, stopped.Revision, "runtime-e2e-enter-second");
            (AutomationRuntimeEntity[] restoredEntities, AutomationRevisionSnapshot restoredRevision) =
                await WaitForRuntimeEntitiesAsync(client);
            AutomationRuntimeEntity restored = Assert.Single(
                restoredEntities,
                entity => entity.NumericId == levelDirector.NumericId);
            Assert.Equal(baselineTransform.X, restored.Transform?.X);
            Assert.Equal(baselineTransform.Y, restored.Transform?.Y);
            AutomationRuntimeComponent restoredComponent = Assert.Single(
                restored.Components,
                component => component.TypeName == levelComponent.TypeName);
            Assert.NotEqual(levelComponent.ComponentId, restoredComponent.ComponentId);
            Assert.Equal(
                baselineField.Value,
                Assert.Single(restoredComponent.Fields, field => field.Name == baselineField.Name).Value);
            _ = await StopPlayAsync(
                client,
                restoredRevision,
                "runtime-e2e-stop-second");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            _ = await stdout;
            _ = await stderr;
            TryDeleteDirectory(root);
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string shellPath,
        string projectRoot,
        string discoveryRoot,
        string artifactRoot,
        string logRoot)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = shellPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectRoot);
        startInfo.ArgumentList.Add("--window-ticks");
        startInfo.ArgumentList.Add("10000000");
        startInfo.ArgumentList.Add("--automation-discovery-root");
        startInfo.ArgumentList.Add(discoveryRoot);
        startInfo.ArgumentList.Add("--automation-artifact-root");
        startInfo.ArgumentList.Add(artifactRoot);
        startInfo.ArgumentList.Add("--automation-import-root");
        startInfo.ArgumentList.Add(Path.GetDirectoryName(projectRoot)
            ?? throw new InvalidOperationException("E2E project root 缺少父目录。"));
        startInfo.ArgumentList.Add("--log-directory");
        startInfo.ArgumentList.Add(logRoot);
        startInfo.ArgumentList.Add("--ephemeral-user-state");
        return startInfo;
    }

    private static async Task<AutomationDiscoveredInstance> WaitForProjectInstanceAsync(
        Process process,
        string discoveryRoot,
        Task<string> stdout,
        Task<string> stderr)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Editor Shell 提前退出 ({process.ExitCode})。\nstdout:\n{await stdout}\nstderr:\n{await stderr}");
            }

            AutomationDiscoverySnapshot snapshot = await AutomationDiscovery.DiscoverAsync(discoveryRoot);
            AutomationDiscoveredInstance? instance = snapshot.Instances.FirstOrDefault(
                static candidate => candidate.Descriptor.Project is not null);
            if (instance is not null)
            {
                return instance;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("30 秒内未发现已打开工程的 Editor automation instance。");
    }

    private static async Task<AutomationArtifactReference> CaptureAsync(
        EditorAutomationClient client,
        string method)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(method);
        return result.Payload?.Deserialize(AutomationJsonContext.Default.AutomationArtifactReference)
            ?? throw new InvalidOperationException($"{method} 未返回 artifactReference。");
    }

    private static async Task<AutomationGamePresentationSnapshot> GetPresentationAsync(
        EditorAutomationClient client)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.GamePresentationGetMethod);
        return result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationGamePresentationSnapshot)
            ?? throw new InvalidOperationException("game.presentation.get 未返回 snapshot。");
    }

    private static async Task<AutomationInvocationResult> EnterPlayAsync(
        EditorAutomationClient client,
        AutomationRevisionSnapshot? revision,
        string idempotencyKey)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.PlayEnterMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPlayEnterRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Source = AutomationPlaySource.TemporarySnapshot,
                },
                AutomationJsonContext.Default.AutomationPlayEnterRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(revision),
                IdempotencyKey = idempotencyKey,
            });
        AutomationPlayCommandResult entered = result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationPlayCommandResult)
            ?? throw new InvalidOperationException("play.enter 未返回结果。");
        Assert.True(entered.Succeeded, entered.Diagnostic);
        Assert.Equal(AutomationEditorMode.Play, entered.Snapshot.Mode);
        return result;
    }

    private static async Task<AutomationInvocationResult> StopPlayAsync(
        EditorAutomationClient client,
        AutomationRevisionSnapshot? revision,
        string idempotencyKey)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.PlayStopMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToResourcePrecondition(revision),
                IdempotencyKey = idempotencyKey,
            });
        AutomationPlayCommandResult stopped = result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationPlayCommandResult)
            ?? throw new InvalidOperationException("play.stop 未返回结果。");
        Assert.True(stopped.Succeeded, stopped.Diagnostic);
        Assert.Equal(AutomationEditorMode.Edit, stopped.Snapshot.Mode);
        return result;
    }

    private static async Task<(AutomationRuntimeEntity[] Entities, AutomationRevisionSnapshot Revision)>
        WaitForRuntimeEntitiesAsync(EditorAutomationClient client)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            AutomationInvocationResult result = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.RuntimeEntityListMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationPageRequest
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        PageSize = 500,
                    },
                    AutomationJsonContext.Default.AutomationPageRequest));
            AutomationRuntimeEntityListResponse response = result.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationRuntimeEntityListResponse)
                ?? throw new InvalidOperationException("runtime.entities.list 未返回结果。");
            if (response.Items.Length > 0)
            {
                return (
                    response.Items,
                    result.Revision ?? throw new InvalidOperationException(
                        "runtime.entities.list 未返回 revision。"));
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("30 秒内 Play session 未产生 runtime entities。");
    }

    private static async Task SetProjectRootsAndUndoAsync(
        EditorAutomationClient client,
        string projectRoot)
    {
        AutomationInvocationResult get = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectSettingsGetMethod);
        AutomationProjectSettings before = get.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectSettings)
            ?? throw new InvalidOperationException("project settings get 未返回 snapshot。");
        Assert.False(before.RequiresReload);
        AutomationProjectSettingsSetRequest request = new()
        {
            Name = before.Name,
            ContentRoot = "content-next",
            ScriptSourceDir = "scripts-next",
            StartScene = before.StartScene,
            RequireStableMaterialNames = before.RequireStableMaterialNames,
            ContentFileGlobs = [.. before.ContentFileGlobs],
            DefaultUiBackend = before.DefaultUiBackend,
        };
        AutomationInvocationResult set = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectSettingsSetMethod,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationProjectSettingsSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(get.Revision),
                IdempotencyKey = "capture-e2e-project-settings",
            });
        AutomationProjectSettings configured = set.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectSettings)
            ?? throw new InvalidOperationException("project settings set 未返回 snapshot。");
        Assert.True(configured.RequiresReload);
        Assert.Equal(before.ContentRoot, configured.ActiveContentRoot);
        Assert.Equal(before.ScriptSourceDir, configured.ActiveScriptSourceDir);
        Assert.Equal(["contentRoot", "scriptSourceDir"], configured.ReloadReasons);
        AssertProjectSettingsFiles(
            projectRoot,
            request.ContentRoot,
            request.ScriptSourceDir,
            request.StartScene);
        AutomationInvocationResult noChange = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectSettingsSetMethod,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationProjectSettingsSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(set.Revision),
                IdempotencyKey = "capture-e2e-project-settings-no-change",
            });
        Assert.Equal(set.Revision?.GlobalRevision, noChange.Revision?.GlobalRevision);

        _ = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(noChange.Revision),
                IdempotencyKey = "capture-e2e-undo-project-settings",
            });
        AutomationInvocationResult restoredResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectSettingsGetMethod);
        AutomationProjectSettings restored = restoredResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationProjectSettings)
            ?? throw new InvalidOperationException("project settings restore 未返回 snapshot。");
        Assert.False(restored.RequiresReload);
        Assert.Equal(before.ContentRoot, restored.ContentRoot);
        Assert.Equal(before.ScriptSourceDir, restored.ScriptSourceDir);
        AssertProjectSettingsFiles(
            projectRoot,
            before.ContentRoot,
            before.ScriptSourceDir,
            before.StartScene);
        await SetProjectSettingsTransactionAndRestoreAsync(client, projectRoot, before);
    }

    private static async Task SetProjectSettingsTransactionAndRestoreAsync(
        EditorAutomationClient client,
        string projectRoot,
        AutomationProjectSettings original)
    {
        AutomationInvocationResult begin = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.TransactionBeginMethod,
            JsonSerializer.SerializeToElement(
                new AutomationTransactionBeginRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Name = "Sequential project settings",
                    LeaseMilliseconds = 30_000,
                },
                AutomationJsonContext.Default.AutomationTransactionBeginRequest),
            new AutomationInvocationOptions
            {
                IdempotencyKey = "capture-e2e-settings-transaction-begin",
            });
        AutomationTransactionInfo transaction = begin.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionInfo)
            ?? throw new InvalidOperationException("settings transaction.begin 未返回 info。");
        AutomationProjectSettingsSetRequest firstRequest = CreateProjectSettingsRequest(
            original,
            "content-transaction-a",
            "scripts-transaction-a");
        AutomationProjectSettingsSetRequest secondRequest = CreateProjectSettingsRequest(
            original,
            "content-transaction-b",
            "scripts-transaction-b");
        AutomationTransactionStagedOperationInfo first = await StageProjectSettingsAsync(
            client,
            transaction,
            firstRequest,
            "capture-e2e-settings-transaction-stage-a");
        AutomationTransactionStagedOperationInfo second = await StageProjectSettingsAsync(
            client,
            transaction,
            secondRequest,
            "capture-e2e-settings-transaction-stage-b");
        Assert.Equal(0, first.Ordinal);
        Assert.Equal(1, second.Ordinal);
        AssertProjectSettingsFiles(
            projectRoot,
            original.ContentRoot,
            original.ScriptSourceDir,
            original.StartScene);

        AutomationInvocationResult committed = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.TransactionCommitMethod,
            JsonSerializer.SerializeToElement(
                new AutomationTransactionRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    TransactionId = transaction.TransactionId,
                },
                AutomationJsonContext.Default.AutomationTransactionRequest),
            new AutomationInvocationOptions
            {
                IdempotencyKey = "capture-e2e-settings-transaction-commit",
            });
        AutomationTransactionCommitResult commit = committed.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionCommitResult)
            ?? throw new InvalidOperationException("settings transaction.commit 未返回结果。");
        Assert.Equal(2, commit.Operations.Length);
        Assert.All(commit.Operations, static operation => Assert.True(operation.StateChanged));
        AutomationProjectSettings firstResult = DeserializeProjectSettings(commit.Operations[0]);
        AutomationProjectSettings secondResult = DeserializeProjectSettings(commit.Operations[1]);
        Assert.Equal(firstRequest.ContentRoot, firstResult.ContentRoot);
        Assert.Equal(secondRequest.ContentRoot, secondResult.ContentRoot);
        AssertProjectSettingsFiles(
            projectRoot,
            secondRequest.ContentRoot,
            secondRequest.ScriptSourceDir,
            secondRequest.StartScene);

        AutomationInvocationResult undone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            committed.Revision,
            "capture-e2e-settings-transaction-undo");
        AssertProjectSettingsFiles(
            projectRoot,
            original.ContentRoot,
            original.ScriptSourceDir,
            original.StartScene);
        AutomationInvocationResult redone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            undone.Revision,
            "capture-e2e-settings-transaction-redo");
        AssertProjectSettingsFiles(
            projectRoot,
            secondRequest.ContentRoot,
            secondRequest.ScriptSourceDir,
            secondRequest.StartScene);
        _ = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            redone.Revision,
            "capture-e2e-settings-transaction-final-undo");
        AssertProjectSettingsFiles(
            projectRoot,
            original.ContentRoot,
            original.ScriptSourceDir,
            original.StartScene);
    }

    private static AutomationProjectSettingsSetRequest CreateProjectSettingsRequest(
        AutomationProjectSettings source,
        string contentRoot,
        string scriptSourceDir)
    {
        return new AutomationProjectSettingsSetRequest
        {
            Name = source.Name,
            ContentRoot = contentRoot,
            ScriptSourceDir = scriptSourceDir,
            StartScene = source.StartScene,
            RequireStableMaterialNames = source.RequireStableMaterialNames,
            ContentFileGlobs = [.. source.ContentFileGlobs],
            DefaultUiBackend = source.DefaultUiBackend,
        };
    }

    private static async Task<AutomationTransactionStagedOperationInfo> StageProjectSettingsAsync(
        EditorAutomationClient client,
        AutomationTransactionInfo transaction,
        AutomationProjectSettingsSetRequest request,
        string idempotencyKey)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectSettingsSetMethod,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationProjectSettingsSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(transaction.BaseRevision),
                IdempotencyKey = idempotencyKey,
                TransactionId = transaction.TransactionId,
            });
        return result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionStagedOperationInfo)
            ?? throw new InvalidOperationException("project.settings.set 未返回 transaction staging 回执。");
    }

    private static AutomationProjectSettings DeserializeProjectSettings(
        AutomationTransactionOperationResult operation)
    {
        return operation.Payload?.Deserialize(AutomationJsonContext.Default.AutomationProjectSettings)
            ?? throw new InvalidOperationException($"{operation.Method} 未返回 project settings 结果。");
    }

    private static void AssertProjectSettingsFiles(
        string projectRoot,
        string contentRoot,
        string scriptSourceDir,
        string startScene)
    {
        ProjectSettingsDto settings = EngineProjectSettingsStore.LoadProjectSettings(projectRoot);
        Assert.Equal(contentRoot, settings.ContentRoot);
        Assert.Equal(scriptSourceDir, settings.ScriptSourceDir);
        Assert.Equal(startScene, settings.StartScene);
        using JsonDocument projectDocument = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(projectRoot, EditorProject.ProjectFileName)));
        JsonElement root = projectDocument.RootElement;
        Assert.Equal(contentRoot, root.GetProperty("contentRoot").GetString());
        Assert.Equal(scriptSourceDir, root.GetProperty("scriptSourceDir").GetString());
        Assert.Equal(startScene, root.GetProperty("startScene").GetString());
    }

    private static async Task CreateMoveAndRestoreAssetsAsync(
        EditorAutomationClient client,
        string projectRoot)
    {
        string firstLogicalPath = "Content/automation/generated-a.json";
        string secondLogicalPath = "Content/automation/generated-b.json";
        string movedLogicalPath = "Content/automation/renamed-a.json";
        string firstPath = Path.Combine(projectRoot, "content", "automation", "generated-a.json");
        string secondPath = Path.Combine(projectRoot, "content", "automation", "generated-b.json");
        string movedPath = Path.Combine(projectRoot, "content", "automation", "renamed-a.json");
        AutomationInvocationResult begin = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.TransactionBeginMethod,
            JsonSerializer.SerializeToElement(
                new AutomationTransactionBeginRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Name = "Create automation assets",
                    LeaseMilliseconds = 30_000,
                },
                AutomationJsonContext.Default.AutomationTransactionBeginRequest),
            new AutomationInvocationOptions
            {
                IdempotencyKey = "capture-e2e-assets-begin",
            });
        AutomationTransactionInfo transaction = begin.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionInfo)
            ?? throw new InvalidOperationException("transaction.begin 未返回 info。");
        Assert.Equal(AutomationTransactionStatus.Active, transaction.Status);
        AssertNoProjectLayoutLeak(projectRoot, "asset-transaction-begin");

        AutomationTransactionStagedOperationInfo first = await StageCreateAssetAsync(
            client,
            transaction,
            firstLogicalPath,
            "capture-e2e-assets-stage-a");
        AutomationTransactionStagedOperationInfo second = await StageCreateAssetAsync(
            client,
            transaction,
            secondLogicalPath,
            "capture-e2e-assets-stage-b");
        Assert.Equal(0, first.Ordinal);
        Assert.Equal(1, second.Ordinal);
        AssertNoProjectLayoutLeak(projectRoot, "asset-transaction-staged");
        Assert.False(File.Exists(firstPath));
        Assert.False(File.Exists(secondPath));

        AutomationInvocationResult committed = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.TransactionCommitMethod,
            JsonSerializer.SerializeToElement(
                new AutomationTransactionRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    TransactionId = transaction.TransactionId,
                },
                AutomationJsonContext.Default.AutomationTransactionRequest),
            new AutomationInvocationOptions
            {
                IdempotencyKey = "capture-e2e-assets-commit",
            });
        AutomationTransactionCommitResult commit = committed.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionCommitResult)
            ?? throw new InvalidOperationException("transaction.commit 未返回结果。");
        AssertNoProjectLayoutLeak(projectRoot, "asset-transaction-commit");
        Assert.Equal(AutomationTransactionStatus.Committed, commit.Transaction.Status);
        Assert.Equal([first.OperationId, second.OperationId],
            commit.Operations.Select(static operation => operation.OperationId));
        Assert.All(commit.Operations, static operation => Assert.True(operation.StateChanged));
        Assert.DoesNotContain(
            "automation-preparation",
            committed.Payload?.GetRawText() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        AutomationAssetMutationResult firstCreated = DeserializeAssetMutation(commit.Operations[0]);
        AutomationAssetMutationResult secondCreated = DeserializeAssetMutation(commit.Operations[1]);
        Assert.True(firstCreated.Succeeded);
        Assert.True(secondCreated.Succeeded);
        Assert.Equal(firstLogicalPath, firstCreated.Asset?.Path, ignoreCase: true);
        Assert.Equal(secondLogicalPath, secondCreated.Asset?.Path, ignoreCase: true);
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        await AssertAssetsAsync(
            client,
            (firstCreated.Asset!.AssetId, firstLogicalPath),
            (secondCreated.Asset!.AssetId, secondLogicalPath));
        AssertNoProjectLayoutLeak(projectRoot, "asset-transaction-read");

        AutomationInvocationResult undoneCreate = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            committed.Revision,
            "capture-e2e-assets-undo-create");
        Assert.False(File.Exists(firstPath));
        Assert.False(File.Exists(secondPath));
        AutomationRevisionSnapshot afterCreateUndo = await AssertAssetsAbsentAsync(
            client,
            firstCreated.Asset.AssetId,
            secondCreated.Asset.AssetId);
        AssertNoProjectLayoutLeak(projectRoot, "asset-transaction-undo");
        AssertMatchingResourceRevision(undoneCreate.Revision, afterCreateUndo, "editor:project");
        AssertMatchingResourceRevision(undoneCreate.Revision, afterCreateUndo, "editor:project:assets");
        Assert.Equal(undoneCreate.Revision?.GlobalRevision, afterCreateUndo.GlobalRevision);

        AutomationInvocationResult redoneCreate = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            undoneCreate.Revision,
            "capture-e2e-assets-redo-create");
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        await AssertAssetsAsync(
            client,
            (firstCreated.Asset.AssetId, firstLogicalPath),
            (secondCreated.Asset.AssetId, secondLogicalPath));
        AssertNoProjectLayoutLeak(projectRoot, "asset-transaction-redo");

        AutomationInvocationResult movedResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetMoveMethod,
            JsonSerializer.SerializeToElement(
                new AutomationAssetMoveRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    AssetId = firstCreated.Asset.AssetId,
                    NewPath = movedLogicalPath,
                },
                AutomationJsonContext.Default.AutomationAssetMoveRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(redoneCreate.Revision),
                IdempotencyKey = "capture-e2e-assets-move",
            });
        AutomationAssetMutationResult moved = movedResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationAssetMutationResult)
            ?? throw new InvalidOperationException("project.asset.move 未返回结果。");
        Assert.True(moved.Succeeded);
        Assert.Equal(firstCreated.Asset.AssetId, moved.Asset?.AssetId);
        Assert.Equal(movedLogicalPath, moved.Asset?.Path, ignoreCase: true);
        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(movedPath));
        string assetManifestPath = Path.Combine(projectRoot, ".pixelengine", "assets.json");
        string manifestAfterMove = File.ReadAllText(assetManifestPath);
        await AssertAssetsAsync(
            client,
            (firstCreated.Asset.AssetId, movedLogicalPath),
            (secondCreated.Asset.AssetId, secondLogicalPath));
        string manifestAfterRead = File.ReadAllText(assetManifestPath);
        if (!string.Equals(manifestAfterMove, manifestAfterRead, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset watcher 重写了 automation Move manifest。\nBEFORE:\n{manifestAfterMove}\nAFTER:\n{manifestAfterRead}");
        }

        AutomationInvocationResult undoneMove;
        try
        {
            undoneMove = await InvokeHistoryAsync(
                client,
                AutomationProtocolConstants.EditorHistoryUndoMethod,
                movedResult.Revision,
                "capture-e2e-assets-undo-move");
        }
        catch (AutomationRemoteException exception)
        {
            AutomationInvocationResult consoleResult = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.ConsoleListMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationPageRequest { PageSize = 500 },
                    AutomationJsonContext.Default.AutomationPageRequest));
            AutomationConsoleListResponse console = consoleResult.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationConsoleListResponse)
                ?? throw new InvalidOperationException("Undo 失败后 Console 未返回诊断。", exception);
            string diagnostic = string.Join(
                Environment.NewLine,
                console.Items
                    .Where(static entry => entry.Source == "automation-undo-cleanup")
                    .Select(static entry => entry.Text + Environment.NewLine + entry.Details));
            throw new InvalidOperationException(
                $"Asset move Undo 失败，本地 Console 诊断：{diagnostic}",
                exception);
        }
        Assert.True(File.Exists(firstPath));
        Assert.False(File.Exists(movedPath));
        await AssertAssetsAsync(
            client,
            (firstCreated.Asset.AssetId, firstLogicalPath),
            (secondCreated.Asset.AssetId, secondLogicalPath));

        try
        {
            _ = await InvokeHistoryAsync(
                client,
                AutomationProtocolConstants.EditorHistoryRedoMethod,
                undoneMove.Revision,
                "capture-e2e-assets-redo-move");
        }
        catch (AutomationRemoteException exception)
        {
            AutomationInvocationResult consoleResult = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.ConsoleListMethod,
                JsonSerializer.SerializeToElement(
                    new AutomationPageRequest { PageSize = 500 },
                    AutomationJsonContext.Default.AutomationPageRequest));
            AutomationConsoleListResponse console = consoleResult.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationConsoleListResponse)
                ?? throw new InvalidOperationException("Redo 失败后 Console 未返回诊断。", exception);
            string diagnostic = string.Join(
                Environment.NewLine,
                console.Items
                    .Where(static entry => entry.Source == "automation-undo-cleanup")
                    .Select(static entry => entry.Text + Environment.NewLine + entry.Details));
            throw new InvalidOperationException(
                $"Asset move Redo 失败，本地 Console 诊断：{diagnostic}",
                exception);
        }
        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(movedPath));
        await AssertAssetsAsync(
            client,
            (firstCreated.Asset.AssetId, movedLogicalPath),
            (secondCreated.Asset.AssetId, secondLogicalPath));

        string originalContents = File.ReadAllText(movedPath);
        string importDirectory = Path.Combine(
            Path.GetDirectoryName(projectRoot)
                ?? throw new InvalidOperationException("E2E project root 缺少父目录。"),
            "imports");
        _ = Directory.CreateDirectory(importDirectory);
        string replacementSource = Path.Combine(importDirectory, "replacement.json");
        const string ReplacementContents = "{\"automation\":\"replaced\"}\n";
        File.WriteAllText(replacementSource, ReplacementContents);
        AutomationInvocationResult beforeReplaceList = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = 500 },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationInvocationResult replacedResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetReplaceMethod,
            JsonSerializer.SerializeToElement(
                new AutomationAssetReplaceRequest
                {
                    AssetId = firstCreated.Asset.AssetId,
                    SourcePath = replacementSource,
                },
                AutomationJsonContext.Default.AutomationAssetReplaceRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(beforeReplaceList.Revision),
                IdempotencyKey = "capture-e2e-assets-replace",
            });
        AutomationAssetMutationResult replaced = replacedResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationAssetMutationResult)
            ?? throw new InvalidOperationException("project.asset.replace 未返回结果。");
        Assert.Equal(firstCreated.Asset.AssetId, replaced.Asset?.AssetId);
        Assert.Equal(ReplacementContents, File.ReadAllText(movedPath));

        AutomationInvocationResult replaceNoChange = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetReplaceMethod,
            JsonSerializer.SerializeToElement(
                new AutomationAssetReplaceRequest
                {
                    AssetId = firstCreated.Asset.AssetId,
                    SourcePath = replacementSource,
                },
                AutomationJsonContext.Default.AutomationAssetReplaceRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(replacedResult.Revision),
                IdempotencyKey = "capture-e2e-assets-replace-no-change",
            });
        Assert.Equal(replacedResult.Revision?.GlobalRevision, replaceNoChange.Revision?.GlobalRevision);

        AutomationInvocationResult replaceUndone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            replaceNoChange.Revision,
            "capture-e2e-assets-replace-undo");
        Assert.Equal(originalContents, File.ReadAllText(movedPath));
        AutomationInvocationResult replaceRedone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            replaceUndone.Revision,
            "capture-e2e-assets-replace-redo");
        Assert.Equal(ReplacementContents, File.ReadAllText(movedPath));
        await AssertAssetsAsync(
            client,
            (firstCreated.Asset.AssetId, movedLogicalPath),
            (secondCreated.Asset.AssetId, secondLogicalPath));
        Assert.NotNull(replaceRedone.Revision);
        await ExerciseUiManifestAsync(client);
    }

    private static async Task ExerciseUiManifestAsync(EditorAutomationClient client)
    {
        AutomationInvocationResult assetsBefore = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest { PageSize = 500 },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationInvocationResult createdResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetCreateMethod,
            JsonSerializer.SerializeToElement(
                new AutomationAssetCreateRequest
                {
                    Path = "Content/ui/screens/automation.xhtml",
                    Kind = AutomationAssetKind.UiScreen,
                },
                AutomationJsonContext.Default.AutomationAssetCreateRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(assetsBefore.Revision),
                IdempotencyKey = "capture-e2e-ui-create",
            });
        AutomationAssetMutationResult created = createdResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationAssetMutationResult)
            ?? throw new InvalidOperationException("UI screen create 未返回结果。");
        string assetId = created.Asset?.AssetId ??
            throw new InvalidOperationException("UI screen create 缺少 stable asset ID。");

        AutomationInvocationResult getBefore = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectUiManifestGetMethod);
        AutomationInvocationResult syncedResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectUiManifestSyncMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(getBefore.Revision),
                IdempotencyKey = "capture-e2e-ui-sync",
            });
        AutomationUiManifestSnapshot synced = syncedResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationUiManifestSnapshot)
            ?? throw new InvalidOperationException("project.ui-manifest.sync 未返回结果。");
        AutomationUiManifestScreen screen = Assert.Single(
            synced.Screens,
            candidate => string.Equals(candidate.AssetId, assetId, StringComparison.Ordinal));
        Assert.True(screen.Preload);
        Assert.True(screen.FileExists);

        AutomationInvocationResult syncNoChange = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectUiManifestSyncMethod,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(syncedResult.Revision),
                IdempotencyKey = "capture-e2e-ui-sync-no-change",
            });
        Assert.Equal(syncedResult.Revision?.GlobalRevision, syncNoChange.Revision?.GlobalRevision);

        AutomationUiManifestPreloadSetRequest disable = new()
        {
            ScreenId = screen.ScreenId,
            Preload = false,
        };
        AutomationInvocationResult disabledResult = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod,
            JsonSerializer.SerializeToElement(
                disable,
                AutomationJsonContext.Default.AutomationUiManifestPreloadSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(syncNoChange.Revision),
                IdempotencyKey = "capture-e2e-ui-preload-disable",
            });
        AutomationUiManifestSnapshot disabled = disabledResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationUiManifestSnapshot)
            ?? throw new InvalidOperationException("UI preload set 未返回结果。");
        Assert.False(Assert.Single(disabled.Screens, item => item.ScreenId == screen.ScreenId).Preload);

        AutomationInvocationResult preloadNoChange = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod,
            JsonSerializer.SerializeToElement(
                disable,
                AutomationJsonContext.Default.AutomationUiManifestPreloadSetRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(disabledResult.Revision),
                IdempotencyKey = "capture-e2e-ui-preload-no-change",
            });
        Assert.Equal(disabledResult.Revision?.GlobalRevision, preloadNoChange.Revision?.GlobalRevision);

        AutomationInvocationResult undone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryUndoMethod,
            preloadNoChange.Revision,
            "capture-e2e-ui-preload-undo");
        AutomationUiManifestSnapshot afterUndo = await GetUiManifestAsync(client);
        Assert.True(Assert.Single(afterUndo.Screens, item => item.ScreenId == screen.ScreenId).Preload);
        AutomationInvocationResult redone = await InvokeHistoryAsync(
            client,
            AutomationProtocolConstants.EditorHistoryRedoMethod,
            undone.Revision,
            "capture-e2e-ui-preload-redo");
        AutomationUiManifestSnapshot afterRedo = await GetUiManifestAsync(client);
        Assert.False(Assert.Single(afterRedo.Screens, item => item.ScreenId == screen.ScreenId).Preload);
        Assert.NotNull(redone.Revision);
    }

    private static async Task<AutomationUiManifestSnapshot> GetUiManifestAsync(
        EditorAutomationClient client)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectUiManifestGetMethod);
        return result.Payload?.Deserialize(AutomationJsonContext.Default.AutomationUiManifestSnapshot)
            ?? throw new InvalidOperationException("project.ui-manifest.get 未返回结果。");
    }

    private static async Task<AutomationTransactionStagedOperationInfo> StageCreateAssetAsync(
        EditorAutomationClient client,
        AutomationTransactionInfo transaction,
        string path,
        string idempotencyKey)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetCreateMethod,
            JsonSerializer.SerializeToElement(
                new AutomationAssetCreateRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Path = path,
                    Kind = AutomationAssetKind.Json,
                },
                AutomationJsonContext.Default.AutomationAssetCreateRequest),
            new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(transaction.BaseRevision),
                IdempotencyKey = idempotencyKey,
                TransactionId = transaction.TransactionId,
            });
        return result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionStagedOperationInfo)
            ?? throw new InvalidOperationException("project.asset.create 未返回 transaction staging 回执。");
    }

    private static AutomationAssetMutationResult DeserializeAssetMutation(
        AutomationTransactionOperationResult operation)
    {
        return operation.Payload?.Deserialize(AutomationJsonContext.Default.AutomationAssetMutationResult)
            ?? throw new InvalidOperationException($"{operation.Method} 未返回 asset mutation 结果。");
    }

    private static async Task<AutomationInvocationResult> InvokeHistoryAsync(
        EditorAutomationClient client,
        string method,
        AutomationRevisionSnapshot? revision,
        string idempotencyKey)
    {
        return await client.InvokeDetailedAsync(
            method,
            payload: null,
            options: new AutomationInvocationOptions
            {
                ExpectedRevision = ToPrecondition(revision),
                IdempotencyKey = idempotencyKey,
            });
    }

    private static async Task AssertAssetsAsync(
        EditorAutomationClient client,
        params (string AssetId, string Path)[] expected)
    {
        AutomationAssetListResponse assets = await ListAssetsAsync(client);
        foreach ((string assetId, string path) in expected)
        {
            AutomationAssetInfo asset = Assert.Single(
                assets.Items,
                candidate => string.Equals(candidate.AssetId, assetId, StringComparison.Ordinal));
            Assert.Equal(path, asset.Path, ignoreCase: true);
        }
    }

    private static async Task<AutomationRevisionSnapshot> AssertAssetsAbsentAsync(
        EditorAutomationClient client,
        params string[] assetIds)
    {
        (AutomationAssetListResponse assets, AutomationRevisionSnapshot revision) =
            await ListAssetsWithRevisionAsync(client);
        foreach (string assetId in assetIds)
        {
            Assert.DoesNotContain(
                assets.Items,
                candidate => string.Equals(candidate.AssetId, assetId, StringComparison.Ordinal));
        }

        return revision;
    }

    private static async Task<AutomationAssetListResponse> ListAssetsAsync(
        EditorAutomationClient client)
    {
        (AutomationAssetListResponse response, _) = await ListAssetsWithRevisionAsync(client);
        return response;
    }

    private static async Task<(AutomationAssetListResponse Response, AutomationRevisionSnapshot Revision)>
        ListAssetsWithRevisionAsync(EditorAutomationClient client)
    {
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.ProjectAssetListMethod,
            JsonSerializer.SerializeToElement(
                new AutomationPageRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    PageSize = 500,
                },
                AutomationJsonContext.Default.AutomationPageRequest));
        AutomationAssetListResponse response = result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationAssetListResponse)
            ?? throw new InvalidOperationException("project.assets.list 未返回结果。");
        AutomationRevisionSnapshot revision = result.Revision
            ?? throw new InvalidOperationException("project.assets.list 未返回 revision。");
        return (response, revision);
    }

    private static AutomationRevisionPrecondition ToPrecondition(AutomationRevisionSnapshot? revision)
    {
        AutomationRevisionSnapshot actual = revision ??
            throw new InvalidOperationException("Automation response 缺少 revision。");
        return new AutomationRevisionPrecondition
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = actual.GlobalRevision,
            Resources =
            [
                .. actual.Resources.Select(static resource => new AutomationExpectedResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = resource.ResourceId,
                    Revision = resource.Revision,
                }),
            ],
        };
    }

    private static AutomationRevisionPrecondition ToResourcePrecondition(
        AutomationRevisionSnapshot? revision)
    {
        AutomationRevisionSnapshot actual = revision ??
            throw new InvalidOperationException("Automation response 缺少 revision。");
        return new AutomationRevisionPrecondition
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = null,
            Resources =
            [
                .. actual.Resources.Select(static resource => new AutomationExpectedResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = resource.ResourceId,
                    Revision = resource.Revision,
                }),
            ],
        };
    }

    private static void AssertMatchingResourceRevision(
        AutomationRevisionSnapshot? expected,
        AutomationRevisionSnapshot actual,
        string resourceId)
    {
        AutomationRevisionSnapshot source = expected
            ?? throw new InvalidOperationException("Automation response 缺少 revision。");
        long expectedRevision = Assert.Single(
            source.Resources,
            resource => string.Equals(resource.ResourceId, resourceId, StringComparison.Ordinal)).Revision;
        long actualRevision = Assert.Single(
            actual.Resources,
            resource => string.Equals(resource.ResourceId, resourceId, StringComparison.Ordinal)).Revision;
        Assert.Equal(expectedRevision, actualRevision);
    }

    private static void ValidateArtifact(
        AutomationArtifactReference artifact,
        string artifactRoot,
        bool requireVisiblePixel)
    {
        string path = Path.GetFullPath(artifact.Path);
        string root = Path.GetFullPath(artifactRoot);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        Assert.StartsWith(rootPrefix, path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), path);
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(artifact.ByteLength, bytes.LongLength);
        Assert.Equal(artifact.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.True(bytes.Length > 54);
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
        Assert.Equal(artifact.Width, width);
        Assert.Equal(artifact.Height, height);
        Assert.InRange(width, 1, 4096);
        Assert.InRange(height, 1, 4096);
        if (requireVisiblePixel)
        {
            Assert.Contains(bytes.AsSpan(54).ToArray(), static value => value != 0);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void CopyDemoProject(string destinationRoot)
    {
        string sourceRoot = FindRepositoryDirectory(
            Path.Combine("demo", "PixelEngine.Demo"),
            "project.pixelproj");
        foreach (string source in Directory.EnumerateFiles(
            sourceRoot,
            "*",
            SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceRoot, source);
            string[] segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(static segment =>
                    string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)) ||
                relative.StartsWith(
                    Path.Combine(".pixelengine", "automation-"),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destination = Path.Combine(destinationRoot, relative);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static string FindRepositoryDirectory(string relativeDirectory, string markerFile)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, relativeDirectory);
            if (File.Exists(Path.Combine(candidate, markerFile)))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"无法从测试输出定位仓库目录：{relativeDirectory}");
    }
}
