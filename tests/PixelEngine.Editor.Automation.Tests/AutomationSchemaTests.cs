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
