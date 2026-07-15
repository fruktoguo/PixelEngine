using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// 发布 JSON Schema 与 source-generated wire 名称一致性测试。
/// </summary>
public sealed class AutomationSchemaTests
{
    /// <summary>验证 schema 包含 envelope、descriptor 与保留 transport。</summary>
    [Fact]
    public void PublishedSchemaDefinesEnvelopeDescriptorAndReservedTransport()
    {
        string schemaPath = FindRepositoryFile("schema/editor-automation-protocol.v1.schema.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(schemaPath));
        JsonElement definitions = document.RootElement.GetProperty("$defs");

        Assert.True(definitions.TryGetProperty("envelope", out _));
        Assert.True(definitions.TryGetProperty("instanceDescriptor", out _));
        Assert.True(definitions.TryGetProperty("helloRequest", out _));
        Assert.True(definitions.TryGetProperty("helloChallenge", out _));
        Assert.True(definitions.TryGetProperty("authenticateRequest", out _));
        Assert.True(definitions.TryGetProperty("sessionInfo", out _));
        Assert.True(definitions.TryGetProperty("cancelRequest", out _));
        Assert.True(definitions.TryGetProperty("pingResponse", out _));
        Assert.True(definitions.TryGetProperty("internalErrorCause", out _));
        Assert.True(definitions.TryGetProperty("internalErrorDetails", out _));
        string[] stateDefinitions =
        [
            "revisionPrecondition",
            "revisionSnapshot",
            "revisionConflictDetails",
            "artifactReference",
            "transactionBeginRequest",
            "transactionRequest",
            "transactionInfo",
            "transactionStagedOperationInfo",
            "transactionOperationResult",
            "transactionCommitResult",
            "transactionFailureDetails",
            "eventRecord",
            "eventSubscribeRequest",
            "subscriptionInfo",
            "eventAckRequest",
            "eventSubscriptionRequest",
            "eventResyncDetails",
            "stateChangedEvent",
        ];
        Assert.All(stateDefinitions, name => Assert.True(definitions.TryGetProperty(name, out _), name));
        string[] editorDefinitions =
        [
            "emptyRequest",
            "pageRequest",
            "pageInfo",
            "capabilityDescriptor",
            "capabilityListResponse",
            "workspaceSnapshot",
            "commandResult",
            "transitionResolveRequest",
            "transitionResult",
            "windowSnapshot",
            "windowState",
            "windowResizeRequest",
            "windowSetRequest",
            "panelInfo",
            "panelListResponse",
            "panelSetRequest",
            "panelDockPlacement",
            "panelDockRequest",
            "panelLayoutEntry",
            "dockLayoutSnapshot",
            "dockLayoutSetRequest",
            "sceneSnapshot",
            "scenePathRequest",
            "sceneSaveAsRequest",
            "hierarchyItem",
            "hierarchyListResponse",
            "gameObjectRequest",
            "inspectorGetRequest",
            "transformValue",
            "inspectorField",
            "componentSnapshot",
            "webCanvasValue",
            "webCanvasSnapshot",
            "canvasScalerValue",
            "gameObjectSnapshot",
            "selectionSnapshot",
            "selectionSetRequest",
            "gameObjectCreateRequest",
            "gameObjectRenameRequest",
            "gameObjectBoolRequest",
            "boolValueRequest",
            "gameObjectReparentRequest",
            "transformSetRequest",
            "componentAddRequest",
            "componentRemoveRequest",
            "componentEnabledSetRequest",
            "componentMoveRequest",
            "componentFieldSetRequest",
            "builtInCanvasSetRequest",
            "canvasPrimarySetRequest",
            "historySnapshot",
            "prefabCreateRequest",
            "prefabCreateResult",
            "prefabInstantiateRequest",
            "brushSettings",
            "sceneToolSnapshot",
            "sceneToolSetRequest",
            "sceneFrameRequest",
            "brushApplyRequest",
            "brushApplyResult",
            "worldPoint",
            "brushStrokeRequest",
            "brushStrokeResult",
            "runtimeTransformSetRequest",
            "runtimeComponentFieldSetRequest",
            "runtimeBody",
            "runtimeBodyListResponse",
            "runtimeBodyRequest",
            "runtimeSimulationSnapshot",
            "runtimeSimulationSetRequest",
            "materialRequest",
            "materialDefinition",
            "materialListResponse",
            "materialEditorRow",
            "materialTagRepresentative",
            "materialReactionRow",
            "materialEditorDocument",
            "materialRuntimeBinding",
            "materialEditorSnapshot",
            "materialEditorSetRequest",
            "materialEditorPreviewResult",
            "materialEditorAssetReload",
            "materialEditorApplyResult",
            "runtimeCellInspectRequest",
            "dirtyRectSnapshot",
            "runtimeCellInspection",
            "worldInspectorSnapshot",
            "worldInspectorSetRequest",
            "runtimePhysicsSnapshot",
            "runtimePhysicsSetRequest",
            "runtimeParticlesSnapshot",
            "runtimeParticlesSetRequest",
            "runtimeLightingSnapshot",
            "runtimeLightingSetRequest",
            "assetReplaceRequest",
            "assetRefreshResult",
            "uiManifestScreen",
            "uiManifestSnapshot",
            "uiManifestPreloadSetRequest",
            "consoleOptions",
            "consoleEntryRequest",
            "consoleSelectionSnapshot",
            "consoleSelectionSetRequest",
            "consoleCopyResult",
            "consoleOpenSourceResult",
            "profilerHistorySample",
            "profilerStatistics",
            "profilerVSyncSetRequest",
        ];
        Assert.All(editorDefinitions, name => Assert.True(definitions.TryGetProperty(name, out _), name));
        Assert.Contains(
            "schemaVersion",
            definitions.GetProperty("envelope").GetProperty("required")
                .EnumerateArray().Select(static item => item.GetString()));
        JsonElement transports = definitions
            .GetProperty("endpoint")
            .GetProperty("properties")
            .GetProperty("kind")
            .GetProperty("enum");
        Assert.Contains("WindowsNamedPipe", transports.EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains("UnixDomainSocket", transports.EnumerateArray().Select(static item => item.GetString()));
        JsonElement envelopeProperties = definitions.GetProperty("envelope").GetProperty("properties");
        Assert.True(envelopeProperties.TryGetProperty("expectedRevision", out _));
        Assert.True(envelopeProperties.TryGetProperty("revision", out _));
        Assert.True(envelopeProperties.TryGetProperty("idempotencyKey", out _));
        Assert.True(envelopeProperties.TryGetProperty("transactionId", out _));
        Assert.True(definitions.GetProperty("helloChallenge").GetProperty("properties")
            .TryGetProperty("serverProof", out _));
    }

    /// <summary>验证 source-generated descriptor 使用 schema 的 camelCase 名称。</summary>
    [Fact]
    public void SourceGeneratedDescriptorMatchesPublishedPropertyNames()
    {
        AutomationInstanceDescriptor descriptor = new()
        {
            Schema = AutomationProtocolConstants.InstanceDescriptorSchema,
            InstanceId = "instance",
            ProcessId = 123,
            ProcessStartUtc = DateTimeOffset.UnixEpoch,
            PublishedAtUtc = DateTimeOffset.UnixEpoch,
            EditorVersion = "1.0",
            ProtocolVersions = [AutomationProtocolConstants.CurrentVersion],
            Endpoint = new AutomationEndpointDescriptor
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Kind = AutomationTransportKind.WindowsNamedPipe,
                Address = "pipe",
            },
            CredentialPath = "credential.token",
            CapabilityDigest = new string('0', 64),
            LivenessMode = "processIdentity",
        };

        JsonElement json = JsonSerializer.SerializeToElement(
            descriptor,
            AutomationJsonContext.Default.AutomationInstanceDescriptor);

        Assert.Equal(AutomationProtocolConstants.InstanceDescriptorSchema, json.GetProperty("schema").GetString());
        Assert.Equal(
            AutomationProtocolConstants.WireSchemaVersion,
            json.GetProperty("endpoint").GetProperty("schemaVersion").GetInt32());
        Assert.Equal("WindowsNamedPipe", json.GetProperty("endpoint").GetProperty("kind").GetString());
        Assert.Equal(1, json.GetProperty("protocolVersions")[0].GetProperty("major").GetInt32());
    }

    /// <summary>
    /// 新增 editor DTO 必须保持 camelCase、显式 schemaVersion 与 strict unknown-member 拒绝。
    /// </summary>
    [Fact]
    public void EditorDtoSourceGenerationIsVersionedAndStrict()
    {
        AutomationGameObjectCreateRequest request = new()
        {
            Name = "Player",
            ParentStableId = 7,
            SiblingIndex = 2,
        };
        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationGameObjectCreateRequest);

        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Player", json.GetProperty("name").GetString());
        Assert.Equal(7, json.GetProperty("parentStableId").GetInt32());
        Assert.False(json.TryGetProperty("Name", out _));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"name":"Player","unexpected":true}
            """,
            AutomationJsonContext.Default.AutomationGameObjectCreateRequest));
    }

    /// <summary>Asset replace 必须用 stable ID 与获准 canonical source，且拒绝未知成员。</summary>
    [Fact]
    public void AssetReplaceDtoRoundTripsStrictly()
    {
        AutomationAssetReplaceRequest request = new()
        {
            AssetId = "asset-stable-id",
            SourcePath = @"C:\imports\replacement.cs",
        };
        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationAssetReplaceRequest);
        Assert.Equal(request.AssetId, json.GetProperty("assetId").GetString());
        Assert.Equal(
            request,
            json.Deserialize(AutomationJsonContext.Default.AutomationAssetReplaceRequest));
        AutomationAssetRefreshResult refresh = new()
        {
            AssetCount = 12,
            FolderCount = 4,
            StateChanged = true,
            Diagnostic = "refreshed",
        };
        Assert.Equal(
            refresh,
            JsonSerializer.SerializeToElement(
                refresh,
                AutomationJsonContext.Default.AutomationAssetRefreshResult)
                .Deserialize(AutomationJsonContext.Default.AutomationAssetRefreshResult));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"assetId":"asset","sourcePath":"C:\\imports\\x.cs","overwrite":true}
            """,
            AutomationJsonContext.Default.AutomationAssetReplaceRequest));
    }

    /// <summary>Project Window 视图快照与部分更新必须使用字符串枚举、显式 null 和 strict JSON。</summary>
    [Fact]
    public void ProjectWindowDtosRoundTripStrictly()
    {
        AutomationProjectWindowSnapshot snapshot = new()
        {
            Search = "script",
            KindFilter = AutomationAssetKind.Script,
            SortMode = AutomationProjectSortMode.SizeDescending,
            ViewMode = AutomationProjectViewMode.Grid,
            ThumbnailSize = 96f,
            MinimumThumbnailSize = 48f,
            MaximumThumbnailSize = 128f,
            ActiveFolderPath = "ScriptSource",
        };
        JsonElement snapshotJson = JsonSerializer.SerializeToElement(
            snapshot,
            AutomationJsonContext.Default.AutomationProjectWindowSnapshot);
        Assert.Equal("Script", snapshotJson.GetProperty("kindFilter").GetString());
        Assert.Equal("SizeDescending", snapshotJson.GetProperty("sortMode").GetString());
        Assert.Equal(
            snapshot,
            snapshotJson.Deserialize(AutomationJsonContext.Default.AutomationProjectWindowSnapshot));

        AutomationProjectWindowSetRequest request = new()
        {
            Search = string.Empty,
            ClearKindFilter = true,
            ViewMode = AutomationProjectViewMode.List,
            ThumbnailSize = 64f,
        };
        JsonElement requestJson = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationProjectWindowSetRequest);
        Assert.False(requestJson.TryGetProperty("kindFilter", out _));
        Assert.True(requestJson.GetProperty("clearKindFilter").GetBoolean());
        Assert.Equal(
            request,
            requestJson.Deserialize(AutomationJsonContext.Default.AutomationProjectWindowSetRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"search":null,"kindFilter":null,"clearKindFilter":false,"sortMode":null,"viewMode":null,"thumbnailSize":null,"unknown":true}
            """,
            AutomationJsonContext.Default.AutomationProjectWindowSetRequest));
    }

    /// <summary>C# workspace 打开结果必须完整表达 IDE、目标、生成状态并拒绝未知成员。</summary>
    [Fact]
    public void CodeProjectOpenResultRoundTripsStrictly()
    {
        AutomationCodeProjectOpenResult result = new()
        {
            Succeeded = true,
            EditorKind = AutomationCodeEditorKind.VsCode,
            ProjectPath = @"C:\project\Game.Scripts.csproj",
            SolutionPath = @"C:\project\Game.Scripts.sln",
            OpenedTarget = @"C:\project",
            ProjectGenerated = true,
            SolutionGenerated = true,
            FilesChanged = true,
            Diagnostic = "opened",
        };
        JsonElement json = JsonSerializer.SerializeToElement(
            result,
            AutomationJsonContext.Default.AutomationCodeProjectOpenResult);
        Assert.Equal("VsCode", json.GetProperty("editorKind").GetString());
        Assert.Equal(
            result,
            json.Deserialize(AutomationJsonContext.Default.AutomationCodeProjectOpenResult));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"succeeded":false,"projectGenerated":false,"solutionGenerated":false,"filesChanged":false,"diagnostic":"failed","pid":42}
            """,
            AutomationJsonContext.Default.AutomationCodeProjectOpenResult));
    }

    /// <summary>Project create/open 转场请求必须使用显式路径字段并拒绝未知成员。</summary>
    [Fact]
    public void WorkspaceProjectTransitionRequestsRoundTripStrictly()
    {
        AutomationProjectCreateRequest create = new()
        {
            LocationPath = @"C:\projects",
            Name = "Game",
        };
        Assert.Equal(
            create,
            JsonSerializer.SerializeToElement(
                create,
                AutomationJsonContext.Default.AutomationProjectCreateRequest)
                .Deserialize(AutomationJsonContext.Default.AutomationProjectCreateRequest));
        AutomationProjectOpenRequest open = new()
        {
            Path = @"C:\projects\Game\project.pixelproj",
        };
        Assert.Equal(
            open,
            JsonSerializer.SerializeToElement(
                open,
                AutomationJsonContext.Default.AutomationProjectOpenRequest)
                .Deserialize(AutomationJsonContext.Default.AutomationProjectOpenRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"path":"C:\\projects\\Game","createIfMissing":true}
            """,
            AutomationJsonContext.Default.AutomationProjectOpenRequest));
    }

    /// <summary>Recent 与 shortcut DTO 必须使用稳定 ID、结构化按键并拒绝未知成员。</summary>
    [Fact]
    public void RecentProjectAndShortcutDtosRoundTripStrictly()
    {
        const string projectId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        AutomationRecentProjectInfo recent = new()
        {
            ProjectId = projectId,
            Name = "Game",
            RootPath = @"C:\projects\Game",
            LastOpenedUtc = new DateTimeOffset(2026, 7, 16, 8, 30, 0, TimeSpan.Zero),
            Favorite = true,
            ProjectFileExists = true,
        };
        JsonElement recentJson = JsonSerializer.SerializeToElement(
            recent,
            AutomationJsonContext.Default.AutomationRecentProjectInfo);
        Assert.Equal(projectId, recentJson.GetProperty("projectId").GetString());
        Assert.Equal(
            recent,
            recentJson.Deserialize(AutomationJsonContext.Default.AutomationRecentProjectInfo));

        AutomationRecentProjectFavoriteSetRequest favorite = new()
        {
            ProjectId = projectId,
            Favorite = false,
        };
        Assert.Equal(
            favorite,
            JsonSerializer.SerializeToElement(
                favorite,
                AutomationJsonContext.Default.AutomationRecentProjectFavoriteSetRequest)
                .Deserialize(AutomationJsonContext.Default.AutomationRecentProjectFavoriteSetRequest));
        AutomationRecentProjectMutationResult removed = new()
        {
            ProjectId = projectId,
            Removed = true,
        };
        JsonElement removedJson = JsonSerializer.SerializeToElement(
            removed,
            AutomationJsonContext.Default.AutomationRecentProjectMutationResult);
        Assert.False(removedJson.TryGetProperty("favorite", out _));

        AutomationShortcutInfo shortcut = new()
        {
            UiCommandId = "shortcut.ctrl-shift-s",
            Action = "Save Scene As",
            Key = "S",
            Control = true,
            Shift = true,
            Alt = false,
            Super = false,
            DisplayText = "Ctrl+Shift+S",
        };
        Assert.Equal(
            shortcut,
            JsonSerializer.SerializeToElement(
                shortcut,
                AutomationJsonContext.Default.AutomationShortcutInfo)
                .Deserialize(AutomationJsonContext.Default.AutomationShortcutInfo));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            $$"""
            {"schemaVersion":1,"projectId":"{{projectId}}","favorite":true,"path":"C:\\projects\\Game"}
            """,
            AutomationJsonContext.Default.AutomationRecentProjectFavoriteSetRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"uiCommandId":"shortcut.ctrl-s","action":"Save Scene","key":"S","control":true,"shift":false,"alt":false,"super":false,"displayText":"Ctrl+S","keyChord":123}
            """,
            AutomationJsonContext.Default.AutomationShortcutInfo));
    }

    /// <summary>UI Manifest screen 与 preload 请求必须使用稳定 ID 且 strict roundtrip。</summary>
    [Fact]
    public void UiManifestDtosRoundTripStrictly()
    {
        AutomationUiManifestSnapshot snapshot = new()
        {
            Screens =
            [
                new AutomationUiManifestScreen
                {
                    ScreenId = "main-menu",
                    Path = "screens/main-menu.xhtml",
                    Preload = true,
                    FileExists = true,
                    AssetId = "asset-screen",
                    LogicalPath = "Content/ui/screens/main-menu.xhtml",
                },
            ],
            MissingFileCount = 0,
            UnregisteredAssetCount = 0,
            Diagnostic = "ok",
        };
        JsonElement json = JsonSerializer.SerializeToElement(
            snapshot,
            AutomationJsonContext.Default.AutomationUiManifestSnapshot);
        Assert.Equal("main-menu", json.GetProperty("screens")[0].GetProperty("screenId").GetString());
        AutomationUiManifestSnapshot roundTrip = json.Deserialize(
            AutomationJsonContext.Default.AutomationUiManifestSnapshot)
            ?? throw new InvalidOperationException("UI manifest DTO roundtrip 返回 null。");
        Assert.Equal(snapshot.Diagnostic, roundTrip.Diagnostic);
        Assert.Equal(snapshot.MissingFileCount, roundTrip.MissingFileCount);
        Assert.Equal(snapshot.UnregisteredAssetCount, roundTrip.UnregisteredAssetCount);
        Assert.Equal(Assert.Single(snapshot.Screens), Assert.Single(roundTrip.Screens));

        AutomationUiManifestPreloadSetRequest request = new()
        {
            ScreenId = "main-menu",
            Preload = false,
        };
        Assert.Equal(
            request,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationUiManifestPreloadSetRequest)
                .Deserialize(AutomationJsonContext.Default.AutomationUiManifestPreloadSetRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"screenId":"main-menu","preload":true,"path":"screens/main.xhtml"}
            """,
            AutomationJsonContext.Default.AutomationUiManifestPreloadSetRequest));
    }

    /// <summary>Runtime 临时写请求保留 stable ID、有限 Transform 字段并拒绝未知成员。</summary>
    [Fact]
    public void RuntimeMutationDtosRoundTripStrictStableIds()
    {
        AutomationRuntimeTransformSetRequest transform = new()
        {
            EntityId = "play:session-1:entity:42",
            X = 1.5f,
            Y = -2f,
            RotationRadians = 0.25f,
            ScaleX = 2f,
            ScaleY = 3f,
        };
        JsonElement transformJson = JsonSerializer.SerializeToElement(
            transform,
            AutomationJsonContext.Default.AutomationRuntimeTransformSetRequest);
        Assert.Equal(1, transformJson.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(transform.EntityId, transformJson.GetProperty("entityId").GetString());
        Assert.Equal(
            transform,
            transformJson.Deserialize(AutomationJsonContext.Default.AutomationRuntimeTransformSetRequest));

        AutomationRuntimeComponentFieldSetRequest field = new()
        {
            EntityId = transform.EntityId,
            ComponentId = transform.EntityId + ":component:" + new string('a', 64),
            FieldName = "Speed",
            Value = "12.5",
        };
        JsonElement fieldJson = JsonSerializer.SerializeToElement(
            field,
            AutomationJsonContext.Default.AutomationRuntimeComponentFieldSetRequest);
        Assert.Equal(field, fieldJson.Deserialize(
            AutomationJsonContext.Default.AutomationRuntimeComponentFieldSetRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"entityId":"play:s:entity:1","componentId":"component","fieldName":"Speed","value":"1","componentIndex":0}
            """,
            AutomationJsonContext.Default.AutomationRuntimeComponentFieldSetRequest));

        AutomationRuntimeBody body = new()
        {
            BodyId = "play:session-1:body:7",
            BodyKey = 7,
            PositionX = 12f,
            PositionY = -3f,
            RotationSin = 0f,
            RotationCos = 1f,
            LinearVelocityX = 4f,
            LinearVelocityY = 5f,
            AngularVelocityRadiansPerSecond = 0.5f,
            MaskWidth = 8,
            MaskHeight = 9,
            SolidPixelCount = 42,
        };
        JsonElement bodyJson = JsonSerializer.SerializeToElement(
            body,
            AutomationJsonContext.Default.AutomationRuntimeBody);
        Assert.Equal(
            body,
            bodyJson.Deserialize(AutomationJsonContext.Default.AutomationRuntimeBody));

        AutomationRuntimeSimulationSetRequest simulation = new() { SimulationHz = 30 };
        JsonElement simulationJson = JsonSerializer.SerializeToElement(
            simulation,
            AutomationJsonContext.Default.AutomationRuntimeSimulationSetRequest);
        Assert.Equal(30, simulationJson.GetProperty("simulationHz").GetDouble());
    }

    /// <summary>Runtime 诊断与调参 DTO 必须完整、严格并使用稳定 enum 名。</summary>
    [Fact]
    public void RuntimeDiagnosticsAndTuningDtosRoundTripStrictly()
    {
        AutomationRuntimeCellInspectRequest cell = new() { WorldX = -65, WorldY = 128 };
        JsonElement cellJson = JsonSerializer.SerializeToElement(
            cell,
            AutomationJsonContext.Default.AutomationRuntimeCellInspectRequest);
        Assert.Equal(-65, cellJson.GetProperty("worldX").GetInt32());
        Assert.Equal(
            cell,
            cellJson.Deserialize(AutomationJsonContext.Default.AutomationRuntimeCellInspectRequest));

        AutomationWorldInspectorSetRequest worldInspector = new()
        {
            FollowSelection = false,
            LockedWorldX = -129,
            LockedWorldY = 257,
        };
        JsonElement worldInspectorJson = JsonSerializer.SerializeToElement(
            worldInspector,
            AutomationJsonContext.Default.AutomationWorldInspectorSetRequest);
        Assert.Equal(-129, worldInspectorJson.GetProperty("lockedWorldX").GetInt32());
        Assert.Equal(
            worldInspector,
            worldInspectorJson.Deserialize(AutomationJsonContext.Default.AutomationWorldInspectorSetRequest));
        AutomationWorldInspectorSnapshot worldInspectorSnapshot = new()
        {
            FollowSelection = true,
            LockedWorldX = 4,
            LockedWorldY = 5,
        };
        Assert.Equal(
            worldInspectorSnapshot,
            JsonSerializer.SerializeToElement(
                worldInspectorSnapshot,
                AutomationJsonContext.Default.AutomationWorldInspectorSnapshot)
                .Deserialize(AutomationJsonContext.Default.AutomationWorldInspectorSnapshot));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"followSelection":false,"lockedWorldX":1,"lockedWorldY":2,"unknown":0}
            """,
            AutomationJsonContext.Default.AutomationWorldInspectorSetRequest));

        AutomationRuntimePhysicsSetRequest physics = new()
        {
            SubStepCount = 4,
            FragmentPixelThreshold = 16,
            GravityX = 0f,
            GravityY = 9.8f,
        };
        Assert.Equal(
            physics,
            JsonSerializer.SerializeToElement(
                physics,
                AutomationJsonContext.Default.AutomationRuntimePhysicsSetRequest)
                .Deserialize(AutomationJsonContext.Default.AutomationRuntimePhysicsSetRequest));

        AutomationRuntimeParticlesSetRequest particles = new()
        {
            MaxCount = 1024,
            GravityPerTick = 0.1f,
            MaxLifetimeTicks = 120,
            DepositSpeedEpsilon = 0.05f,
            EjectionImpulseScale = 1.25f,
            MaxEjectionPerTick = 64,
        };
        Assert.Equal(
            particles,
            JsonSerializer.SerializeToElement(
                particles,
                AutomationJsonContext.Default.AutomationRuntimeParticlesSetRequest)
                .Deserialize(AutomationJsonContext.Default.AutomationRuntimeParticlesSetRequest));

        AutomationRuntimeLightingSetRequest lighting = new()
        {
            Quality = AutomationLightingQuality.BloomDisabled,
            BloomEnabled = false,
            BloomThreshold = 0.75f,
            BloomIntensity = 0.8f,
            FogOfWarEnabled = true,
            DitherEnabled = false,
            Gamma = 2.2f,
            RadianceCascadesEnabled = false,
        };
        JsonElement lightingJson = JsonSerializer.SerializeToElement(
            lighting,
            AutomationJsonContext.Default.AutomationRuntimeLightingSetRequest);
        Assert.Equal("BloomDisabled", lightingJson.GetProperty("quality").GetString());
        Assert.Equal(
            lighting,
            lightingJson.Deserialize(AutomationJsonContext.Default.AutomationRuntimeLightingSetRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"quality":"Full","bloomEnabled":true,"bloomThreshold":1,"bloomIntensity":1,"fogOfWarEnabled":true,"ditherEnabled":true,"gamma":2.2,"radianceCascadesEnabled":false,"unknown":0}
            """,
            AutomationJsonContext.Default.AutomationRuntimeLightingSetRequest));
    }

    /// <summary>材质 catalog 与画刷必须以稳定 name 为身份并保留 nullable 相变字段。</summary>
    [Fact]
    public void MaterialCatalogAndBrushDtosRoundTripStableNamesStrictly()
    {
        AutomationMaterialDefinition material = new()
        {
            ResourceId = "editor:runtime:materials:" + new string('a', 64),
            Name = "water",
            RuntimeId = 7,
            DisplayName = "Water",
            CellType = "Liquid",
            Density = 100,
            Dispersion = 5,
            MeltPoint = null,
            MeltTargetName = null,
            FreezePoint = 0f,
            FreezeTargetName = "ice",
            BoilPoint = 100f,
            BoilTargetName = "steam",
            HeatCapacity = 4.2f,
            BaseColorBgra = uint.MaxValue,
            RenderStyle = "Liquid",
            LegendCategory = "Liquid",
            LegendVisible = true,
            PropertyFlags = "None",
            BlocksCharacter = false,
        };
        JsonElement materialJson = JsonSerializer.SerializeToElement(
            material,
            AutomationJsonContext.Default.AutomationMaterialDefinition);
        Assert.Equal("water", materialJson.GetProperty("name").GetString());
        Assert.Equal(uint.MaxValue, materialJson.GetProperty("baseColorBgra").GetUInt32());
        Assert.False(materialJson.TryGetProperty("meltPoint", out _));
        Assert.Equal(
            material,
            materialJson.Deserialize(AutomationJsonContext.Default.AutomationMaterialDefinition));

        AutomationMaterialListResponse list = new()
        {
            Items = [material],
            Page = new AutomationPageInfo { Returned = 1, Total = 1 },
        };
        AutomationMaterialListResponse listRoundTrip = JsonSerializer.SerializeToElement(
                list,
                AutomationJsonContext.Default.AutomationMaterialListResponse)
            .Deserialize(AutomationJsonContext.Default.AutomationMaterialListResponse)
            ?? throw new InvalidOperationException("Material list DTO roundtrip 返回 null。");
        Assert.Equal(material, Assert.Single(listRoundTrip.Items));

        AutomationBrushSettings brush = new()
        {
            Tool = "Paint",
            Shape = "Circle",
            MaterialName = "water",
            MaterialId = null,
            Radius = 8,
            Probability = 0.75f,
            TemperatureMode = "Target",
            TemperatureCelsius = 25f,
        };
        JsonElement brushJson = JsonSerializer.SerializeToElement(
            brush,
            AutomationJsonContext.Default.AutomationBrushSettings);
        Assert.Equal("water", brushJson.GetProperty("materialName").GetString());
        Assert.False(brushJson.TryGetProperty("materialId", out _));
        Assert.Equal(
            brush,
            brushJson.Deserialize(AutomationJsonContext.Default.AutomationBrushSettings));

        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"name":"water","runtimeId":7}
            """,
            AutomationJsonContext.Default.AutomationMaterialRequest));
    }

    /// <summary>Materials/Reactions 草稿必须覆盖全部可编辑字段，runtime ID 只能出现在只读 binding。</summary>
    [Fact]
    public void MaterialEditorDtosRoundTripCompleteDraftStrictly()
    {
        AutomationMaterialEditorDocument draft = new()
        {
            Materials =
            [
                new AutomationMaterialEditorRow
                {
                    Name = "water",
                    Type = "Liquid",
                    Density = 100,
                    FlowRate = 5,
                    LiquidStatic = true,
                    LiquidSand = false,
                    Flammability = 1,
                    AutoIgnitionTemp = 300,
                    FireHp = 8,
                    TemperatureOfFire = 200,
                    GeneratesSmoke = 3,
                    MeltPoint = -10,
                    MeltTarget = "ice",
                    FreezePoint = 0,
                    FreezeTarget = "ice",
                    BoilPoint = 100,
                    BoilTarget = "steam",
                    HeatConduct = 64,
                    HeatCapacity = 4.2f,
                    DefaultLifetime = 0,
                    Durability = 2,
                    MaxIntegrity = 6,
                    RubbleTarget = "empty",
                    DebrisCount = 1,
                    MineYield = 2,
                    TextureId = 7,
                    BaseColorBgra = 0xFF112233,
                    ColorNoise = 4,
                    RenderStyle = "Liquid",
                    LegendCategory = "Liquid",
                    OutlineColorBgra = 0xFF010203,
                    Alpha = 220,
                    FlowTintBgra = 0xFF445566,
                    DisplayName = "Water",
                    LegendVisible = true,
                    Tags = "cold, liquid",
                    ImpactCue = 1,
                    FireCue = 2,
                    SplashCue = 3,
                    ExplosionCue = 4,
                    ShatterCue = 5,
                    AmbientCue = 6,
                },
            ],
            TagRepresentatives =
            [
                new AutomationMaterialTagRepresentative { Tag = "Cold", Material = "water" },
            ],
            Reactions =
            [
                new AutomationMaterialReactionRow
                {
                    InputA = "water",
                    InputB = "fire",
                    OutputA = "steam",
                    OutputB = "empty",
                    Probability = 75,
                    Flags = "Fast",
                },
            ],
        };
        AutomationMaterialEditorSetRequest request = new() { Document = draft };
        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationMaterialEditorSetRequest);
        Assert.Equal("water", json.GetProperty("document").GetProperty("materials")[0]
            .GetProperty("name").GetString());
        Assert.False(json.GetProperty("document").GetProperty("materials")[0]
            .TryGetProperty("runtimeId", out _));
        AutomationMaterialEditorSetRequest roundTrip = json.Deserialize(
            AutomationJsonContext.Default.AutomationMaterialEditorSetRequest)
            ?? throw new InvalidOperationException("Material editor DTO roundtrip 返回 null。");
        AutomationMaterialEditorRow material = Assert.Single(roundTrip.Document.Materials);
        Assert.Equal(draft.Materials[0], material);
        Assert.Equal(draft.TagRepresentatives[0], Assert.Single(roundTrip.Document.TagRepresentatives));
        Assert.Equal(draft.Reactions[0], Assert.Single(roundTrip.Document.Reactions));

        AutomationMaterialEditorSnapshot snapshot = new()
        {
            Document = draft,
            RuntimeBindings =
            [
                new AutomationMaterialRuntimeBinding { Name = "water", RuntimeId = 7 },
            ],
            Status = "ready",
        };
        JsonElement snapshotJson = JsonSerializer.SerializeToElement(
            snapshot,
            AutomationJsonContext.Default.AutomationMaterialEditorSnapshot);
        Assert.Equal(7, snapshotJson.GetProperty("runtimeBindings")[0]
            .GetProperty("runtimeId").GetInt32());

        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"document":{"materials":[],"tagRepresentatives":[],"reactions":[]},"runtimeId":7}
            """,
            AutomationJsonContext.Default.AutomationMaterialEditorSetRequest));
    }

    /// <summary>Console 行为选项与 Profiler VSync 请求必须完整、严格且带 schemaVersion。</summary>
    [Fact]
    public void ConsoleAndProfilerControlDtosRoundTripStrictly()
    {
        AutomationConsoleOptions options = new()
        {
            Search = "runtime",
            Collapse = true,
            ClearOnPlay = false,
            ErrorPause = true,
            ShowLogs = false,
            ShowWarnings = true,
            ShowErrors = true,
            AutoScroll = false,
        };
        JsonElement optionsJson = JsonSerializer.SerializeToElement(
            options,
            AutomationJsonContext.Default.AutomationConsoleOptions);
        Assert.Equal(1, optionsJson.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            options,
            optionsJson.Deserialize(AutomationJsonContext.Default.AutomationConsoleOptions));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"search":"","collapse":false,"clearOnPlay":true,"errorPause":false,"showLogs":true,"showWarnings":true,"showErrors":true,"autoScroll":true,"unknown":0}
            """,
            AutomationJsonContext.Default.AutomationConsoleOptions));

        AutomationConsoleEntry entry = new()
        {
            EntryId = "console:7",
            Sequence = 7,
            Timestamp = DateTimeOffset.UnixEpoch,
            Category = AutomationConsoleCategory.Script,
            Severity = AutomationConsoleSeverity.Error,
            Source = "compiler",
            Text = "CS1000",
            Details = "details",
            FilePath = "ScriptSource/Player.cs",
            Line = 12,
            Column = 3,
            FrameIndex = -1,
        };
        AutomationConsoleSelectionSnapshot selection = new()
        {
            EntryId = entry.EntryId,
            Entry = entry,
        };
        Assert.Equal(
            selection,
            JsonSerializer.SerializeToElement(
                selection,
                AutomationJsonContext.Default.AutomationConsoleSelectionSnapshot)
                .Deserialize(AutomationJsonContext.Default.AutomationConsoleSelectionSnapshot));
        AutomationConsoleSelectionSetRequest selectionRequest = new() { EntryId = entry.EntryId };
        Assert.Equal(
            selectionRequest,
            JsonSerializer.SerializeToElement(
                selectionRequest,
                AutomationJsonContext.Default.AutomationConsoleSelectionSetRequest)
                .Deserialize(AutomationJsonContext.Default.AutomationConsoleSelectionSetRequest));
        AutomationConsoleCopyResult copy = new() { Entry = entry, Text = "CS1000" };
        Assert.Equal(
            copy,
            JsonSerializer.SerializeToElement(
                copy,
                AutomationJsonContext.Default.AutomationConsoleCopyResult)
                .Deserialize(AutomationJsonContext.Default.AutomationConsoleCopyResult));
        AutomationConsoleOpenSourceResult openSource = new()
        {
            Entry = entry,
            Succeeded = true,
            Diagnostic = "opened",
        };
        Assert.Equal(
            openSource,
            JsonSerializer.SerializeToElement(
                openSource,
                AutomationJsonContext.Default.AutomationConsoleOpenSourceResult)
                .Deserialize(AutomationJsonContext.Default.AutomationConsoleOpenSourceResult));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"entryId":"console:7","unknown":0}
            """,
            AutomationJsonContext.Default.AutomationConsoleEntryRequest));

        AutomationProfilerVSyncSetRequest request = new() { Enabled = true };
        JsonElement requestJson = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationProfilerVSyncSetRequest);
        Assert.True(requestJson.GetProperty("enabled").GetBoolean());
        Assert.Equal(
            request,
            requestJson.Deserialize(AutomationJsonContext.Default.AutomationProfilerVSyncSetRequest));
        AutomationProfilerHistorySample history = new()
        {
            FrameIndex = 42,
            FrameMilliseconds = 16.6,
            CaMilliseconds = 2,
            PhysicsMilliseconds = 1,
            RenderMilliseconds = 3,
            AudioMilliseconds = 0.1,
            CpuMilliseconds = 7,
            GpuMilliseconds = 4,
            WaitMilliseconds = 9,
            EffectiveMilliseconds = 7.6,
            VariableWorkMilliseconds = 5,
            FixedOverheadMilliseconds = 2,
            ActiveChunks = 12,
            ActiveCells = 4096,
            FreeParticles = 8,
            RigidBodies = 3,
            DestructionEvents = 2,
            CustomMetricValue = 99,
            SimulationHz = 60,
        };
        Assert.Equal(
            history,
            JsonSerializer.SerializeToElement(
                history,
                AutomationJsonContext.Default.AutomationProfilerHistorySample)
                .Deserialize(AutomationJsonContext.Default.AutomationProfilerHistorySample));
        AutomationProfilerStatistics statistics = new()
        {
            SampleCount = 120,
            AverageMilliseconds = 8,
            P50Milliseconds = 7,
            P95Milliseconds = 11,
            P99Milliseconds = 14,
            MaxMilliseconds = 18,
            IsSteady = true,
            IsSpike = false,
        };
        Assert.Equal(
            statistics,
            JsonSerializer.SerializeToElement(
                statistics,
                AutomationJsonContext.Default.AutomationProfilerStatistics)
                .Deserialize(AutomationJsonContext.Default.AutomationProfilerStatistics));
    }

    /// <summary>Canvas/CanvasScaler 的全部入盘值必须使用稳定 enum 名与 strict DTO。</summary>
    [Fact]
    public void CanvasDtoRoundTripsAllScalerModesWithoutPrimaryAmbiguity()
    {
        AutomationBuiltInCanvasSetRequest request = new()
        {
            StableId = 42,
            WebCanvas = new AutomationWebCanvasValue
            {
                ManifestAssetId = "asset:ui-main",
                ManifestPath = "ui/main.canvas.json",
                InitialScreenId = "main-menu",
                Enabled = true,
                SortingOrder = -3,
            },
            CanvasScaler = new AutomationCanvasScalerValue
            {
                ScaleMode = AutomationCanvasScaleMode.ScaleWithScreenSize,
                ScaleFactor = 1.25f,
                ReferenceWidth = 1920f,
                ReferenceHeight = 1080f,
                ScreenMatchMode = AutomationCanvasScreenMatchMode.MatchWidthOrHeight,
                MatchWidthOrHeight = 0.5f,
                PhysicalUnit = AutomationCanvasPhysicalUnit.Points,
                FallbackScreenDpi = 96f,
                DefaultSpriteDpi = 96f,
                ReferencePixelsPerUnit = 100f,
            },
        };

        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationBuiltInCanvasSetRequest);
        Assert.Equal("ScaleWithScreenSize", json.GetProperty("canvasScaler")
            .GetProperty("scaleMode").GetString());
        Assert.Equal("Points", json.GetProperty("canvasScaler")
            .GetProperty("physicalUnit").GetString());
        Assert.False(json.GetProperty("webCanvas").TryGetProperty("primary", out _));

        AutomationBuiltInCanvasSetRequest roundTrip = json.Deserialize(
            AutomationJsonContext.Default.AutomationBuiltInCanvasSetRequest)
            ?? throw new InvalidOperationException("Canvas DTO roundtrip 返回 null。");
        Assert.Equal(request, roundTrip);
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"stableId":42,"primary":true}
            """,
            AutomationJsonContext.Default.AutomationBuiltInCanvasSetRequest));
    }

    /// <summary>window.set 的状态与坐标必须使用稳定名称并拒绝未知成员。</summary>
    [Fact]
    public void WindowSetDtoRoundTripsStableStateAndPlacement()
    {
        AutomationWindowSetRequest request = new()
        {
            X = -640,
            Y = 32,
            Width = 1280,
            Height = 720,
            State = AutomationWindowState.Maximized,
            Activate = true,
        };

        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationWindowSetRequest);

        Assert.Equal("Maximized", json.GetProperty("state").GetString());
        Assert.Equal(-640, json.GetProperty("x").GetInt32());
        Assert.True(json.GetProperty("activate").GetBoolean());
        Assert.Equal(
            request,
            json.Deserialize(AutomationJsonContext.Default.AutomationWindowSetRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"state":"Normal","activate":false,"screenCoordinate":true}
            """,
            AutomationJsonContext.Default.AutomationWindowSetRequest));
    }

    /// <summary>dock layout 导入必须保留稳定 panel ID、文本摘要与严格嵌套 schema。</summary>
    [Fact]
    public void DockLayoutDtoRoundTripsStablePanelRegistryAndDigest()
    {
        const string layoutText = "[Window][Scene]\nPos=0,0\n[Docking][Data]\nDockSpace ID=0x1\n";
        AutomationDockLayoutSetRequest request = new()
        {
            Layout = new AutomationDockLayoutSnapshot
            {
                LayoutVersion = 3,
                Utf8ByteLength = System.Text.Encoding.UTF8.GetByteCount(layoutText),
                Sha256 = Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(layoutText))),
                LayoutText = layoutText,
                Panels =
                [
                    new AutomationPanelLayoutEntry { PanelId = "panel.scene", Visible = true },
                    new AutomationPanelLayoutEntry { PanelId = "panel.inspector", Visible = false },
                ],
            },
        };

        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationDockLayoutSetRequest);

        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, json.GetProperty("layout").GetProperty("schemaVersion").GetInt32());
        Assert.Equal("panel.scene", json.GetProperty("layout").GetProperty("panels")[0]
            .GetProperty("panelId").GetString());
        AutomationDockLayoutSetRequest roundTrip = json.Deserialize(
            AutomationJsonContext.Default.AutomationDockLayoutSetRequest)
            ?? throw new InvalidOperationException("Dock layout DTO roundtrip 返回 null。");
        Assert.Equal(request.Layout.LayoutVersion, roundTrip.Layout.LayoutVersion);
        Assert.Equal(request.Layout.Sha256, roundTrip.Layout.Sha256);
        Assert.Equal(request.Layout.LayoutText, roundTrip.Layout.LayoutText);
        Assert.Equal(
            request.Layout.Panels.Select(static item => (item.PanelId, item.Visible)),
            roundTrip.Layout.Panels.Select(static item => (item.PanelId, item.Visible)));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"layout":{"schemaVersion":1,"layoutVersion":3,"utf8ByteLength":0,"sha256":"0000000000000000000000000000000000000000000000000000000000000000","layoutText":"","panels":[],"nativeDockId":1}}
            """,
            AutomationJsonContext.Default.AutomationDockLayoutSetRequest));
    }

    /// <summary>Scene tool 请求必须完整保留 snap、authoring camera 与 overlay 的语义值。</summary>
    [Fact]
    public void SceneToolDtoRoundTripsSnapCameraAndOverlayState()
    {
        AutomationSceneToolSetRequest request = new()
        {
            Tool = AutomationSceneTool.Rotate,
            GizmoSpace = AutomationGizmoSpace.World,
            GridVisible = false,
            SnapEnabled = true,
            MoveSnap = 2f,
            RotationSnapDegrees = 30f,
            ScaleSnap = 0.25f,
            CameraCenterX = 12.5f,
            CameraCenterY = -4.25f,
            CameraCellsPerPixel = 0.5f,
            BrushPanelVisible = false,
            OverlayDock = AutomationSceneToolOverlayDock.Floating,
            OverlayOffsetX = 16f,
            OverlayOffsetY = 24f,
        };

        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationSceneToolSetRequest);

        Assert.Equal("Rotate", json.GetProperty("tool").GetString());
        Assert.Equal(12.5f, json.GetProperty("cameraCenterX").GetSingle());
        Assert.Equal(0.5f, json.GetProperty("cameraCellsPerPixel").GetSingle());
        Assert.Equal(
            request,
            json.Deserialize(AutomationJsonContext.Default.AutomationSceneToolSetRequest));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"cameraCenterX":0,"cameraCenterY":0,"cameraCellsPerPixel":1,"screenCoordinate":true}
            """,
            AutomationJsonContext.Default.AutomationSceneToolSetRequest));
    }

    /// <summary>Game presentation 请求必须保留完整 preset/pan/maximize 状态并拒绝未知成员。</summary>
    [Fact]
    public void GamePresentationDtoRoundTripsCustomPresetAndStableEnums()
    {
        AutomationGamePresentationSetRequest request = new()
        {
            SelectedPresetId = "custom-ci",
            ScalePercent = 125f,
            PanX = 12.5f,
            PanY = -8.25f,
            MaximizeOnPlay = true,
            Maximized = false,
            CustomPresets =
            [
                new AutomationGameViewPreset
                {
                    PresetId = "custom-ci",
                    Name = "CI Capture",
                    Kind = AutomationGameViewPresetKind.FixedResolution,
                    BuiltIn = false,
                    ValueA = 1024,
                    ValueB = 576,
                },
            ],
        };

        JsonElement json = JsonSerializer.SerializeToElement(
            request,
            AutomationJsonContext.Default.AutomationGamePresentationSetRequest);
        Assert.Equal("FixedResolution", json.GetProperty("customPresets")[0]
            .GetProperty("kind").GetString());
        Assert.Equal(12.5f, json.GetProperty("panX").GetSingle());
        AutomationGamePresentationSetRequest roundTrip = json.Deserialize(
            AutomationJsonContext.Default.AutomationGamePresentationSetRequest)
            ?? throw new InvalidOperationException("Game presentation DTO roundtrip 返回 null。");
        Assert.Equal(request.SelectedPresetId, roundTrip.SelectedPresetId);
        Assert.Equal(request.ScalePercent, roundTrip.ScalePercent);
        Assert.Equal(request.PanX, roundTrip.PanX);
        Assert.Equal(request.PanY, roundTrip.PanY);
        Assert.Equal(request.MaximizeOnPlay, roundTrip.MaximizeOnPlay);
        Assert.Equal(request.Maximized, roundTrip.Maximized);
        Assert.Equal(
            request.CustomPresets.Select(static preset =>
                (preset.PresetId, preset.Name, preset.Kind, preset.BuiltIn, preset.ValueA, preset.ValueB)),
            roundTrip.CustomPresets.Select(static preset =>
                (preset.PresetId, preset.Name, preset.Kind, preset.BuiltIn, preset.ValueA, preset.ValueB)));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """
            {"schemaVersion":1,"selectedPresetId":"player-default","scalePercent":0,"panX":0,"panY":0,"maximizeOnPlay":false,"maximized":false,"customPresets":[],"screenCoordinate":true}
            """,
            AutomationJsonContext.Default.AutomationGamePresentationSetRequest));
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件 {relativePath}。");
    }
}
