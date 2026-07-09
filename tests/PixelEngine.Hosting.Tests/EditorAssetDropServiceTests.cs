using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// EditorShell 资产拖拽语义测试。
/// 不变式：拖拽语义映射到正确资产类型、非法路径拒绝且不改工程模型。
/// </summary>
public sealed class EditorAssetDropServiceTests
{
    /// <summary>
    /// 验证 prefab 拖拽到 Hierarchy 会创建可撤销的 prefab 实例，且非 prefab 资产不会静默修改场景。
    /// </summary>
    [Fact]
    public void DropPrefabOnHierarchyCreatesUndoableInstanceAndRejectsOtherAssets()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            SavePrefabDocument(contentRoot, "prefabs/rock.prefab", "Rock");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord prefab = manifest.EnsureAsset("prefabs/rock.prefab");
            EditorAssetRecord texture = manifest.CreateAsset("textures/stone.png", EditorAssetType.Texture, textContents: "texture");
            EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
            EditorSceneModel scene = EditorSceneModel.Empty("drop-hierarchy");
            EditorGameObject parent = scene.Create("Parent");
            EditorUndoStack undo = new();

            EditorAssetDropResult invalid = EditorAssetDropService.DropOnHierarchy(
                scene,
                undo,
                prefabs,
                EditorAssetDropPayload.FromAsset(texture),
                parent.StableId);

            // Assert：验证预期结果
            Assert.False(invalid.Succeeded);
            Assert.Contains("仅 prefab", invalid.Diagnostic, StringComparison.Ordinal);
            Assert.Equal(1, scene.Count);

            EditorAssetDropResult valid = EditorAssetDropService.DropOnHierarchy(
                scene,
                undo,
                prefabs,
                EditorAssetDropPayload.FromAsset(prefab),
                parent.StableId);

            Assert.True(valid.Succeeded);
            int instanceId = Assert.IsType<int>(valid.StableId);
            EditorGameObject instance = scene.Get(instanceId);
            Assert.Equal(parent.StableId, instance.ParentId);
            Assert.Equal(prefab.Id, instance.PrefabLink?.AssetId);
            Assert.Equal("prefabs/rock.prefab", instance.PrefabLink?.AssetPath);
            Assert.True(undo.Undo(scene));
            Assert.Equal(1, scene.Count);
            Assert.True(undo.Redo(scene));
            Assert.Equal(2, scene.Count);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 prefab 拖拽到 Scene View 会按落点 Transform 放置，且同样拒绝非 prefab 资产。
    /// </summary>
    [Fact]
    public void DropPrefabOnSceneViewAppliesWorldTransformAndRejectsOtherAssets()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            SavePrefabDocument(contentRoot, "prefabs/rock.prefab", "Rock");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord prefab = manifest.EnsureAsset("prefabs/rock.prefab");
            EditorAssetRecord audio = manifest.CreateAsset("audio/hit.wav", EditorAssetType.Audio, textContents: "audio");
            EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
            EditorSceneModel scene = EditorSceneModel.Empty("drop-scene-view");
            EditorUndoStack undo = new();
            EditorSceneTransform transform = new()
            {
                X = 12,
                Y = 34,
                RotationRadians = 0.5f,
                ScaleX = 2,
                ScaleY = 3,
            };

            EditorAssetDropResult invalid = EditorAssetDropService.DropOnSceneView(
                scene,
                undo,
                prefabs,
                EditorAssetDropPayload.FromAsset(audio),
                transform);

            // Assert：验证预期结果
            Assert.False(invalid.Succeeded);
            Assert.Contains("仅 prefab", invalid.Diagnostic, StringComparison.Ordinal);
            Assert.Equal(0, scene.Count);

            EditorAssetDropResult valid = EditorAssetDropService.DropOnSceneView(
                scene,
                undo,
                prefabs,
                EditorAssetDropPayload.FromAsset(prefab),
                transform);

            Assert.True(valid.Succeeded);
            EditorGameObject instance = scene.Get(Assert.IsType<int>(valid.StableId));
            Assert.Null(instance.ParentId);
            Assert.Equal(12, instance.Transform.X);
            Assert.Equal(34, instance.Transform.Y);
            Assert.Equal(0.5f, instance.Transform.RotationRadians);
            Assert.Equal(2, instance.Transform.ScaleX);
            Assert.Equal(3, instance.Transform.ScaleY);
            Assert.Equal(prefab.Id, instance.PrefabLink?.AssetId);
            Assert.True(undo.Undo(scene));
            Assert.Equal(0, scene.Count);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证非 prefab 资产拖拽到 Hierarchy / Scene View 会全部给出诊断且不修改场景。
    /// </summary>
    [Fact]
    public void DropNonPrefabAssetsOnHierarchyAndSceneViewAlwaysFailWithoutSideEffects()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorPrefabAssetStore prefabs = new(contentRoot);
            EditorSceneTransform transform = new() { X = 1, Y = 2, ScaleX = 1, ScaleY = 1 };
            EditorAssetDropPayload[] payloads =
            [
                new("asset_scene", "scenes/main.scene", EditorAssetType.Scene),
                new("asset_material", "materials.json", EditorAssetType.Material),
                new("asset_script", "scripts/Controller.cs", EditorAssetType.Script),
                new("asset_texture", "textures/sand.png", EditorAssetType.Texture),
                new("asset_audio", "audio/hit.wav", EditorAssetType.Audio),
            ];

            for (int i = 0; i < payloads.Length; i++)
            {
                EditorSceneModel scene = EditorSceneModel.Empty("drop-invalid-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                EditorUndoStack undo = new();

                EditorAssetDropResult hierarchy = EditorAssetDropService.DropOnHierarchy(scene, undo, prefabs, payloads[i], parentStableId: null);
                EditorAssetDropResult sceneView = EditorAssetDropService.DropOnSceneView(scene, undo, prefabs, payloads[i], transform);

                // Assert：验证预期结果
                Assert.False(hierarchy.Succeeded);
                Assert.False(sceneView.Succeeded);
                Assert.Contains("仅 prefab", hierarchy.Diagnostic, StringComparison.Ordinal);
                Assert.Contains("仅 prefab", sceneView.Diagnostic, StringComparison.Ordinal);
                Assert.Equal(0, scene.Count);
                Assert.False(undo.CanUndo);
            }
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 Inspector typed asset 字段只接受匹配资产类型，并以 stable asset reference 编码。
    /// </summary>
    [Fact]
    public void DropOnInspectorFieldEncodesTypedAssetReferenceAndRejectsMismatch()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel scene = EditorSceneModel.Empty("drop-inspector");
        EditorGameObject gameObject = scene.Create("Receiver");
        gameObject.Components.Add(new EditorComponentModel(typeof(AssetDropProbeBehaviour).FullName!));
        EditorUndoStack undo = new();
        EditorAssetDropPayload texture = new("asset_texture", "textures/sand.png", EditorAssetType.Texture);
        EditorAssetDropPayload audio = new("asset_audio", "audio/hit.wav", EditorAssetType.Audio);
        EditorAssetInspectorFieldTarget target = new(gameObject.StableId, 0, "Texture", EditorAssetType.Texture);

        EditorAssetDropResult mismatch = EditorAssetDropService.DropOnInspectorField(scene, undo, audio, target);

        // Assert：验证预期结果
        Assert.False(mismatch.Succeeded);
        Assert.Contains("需要 Texture", mismatch.Diagnostic, StringComparison.Ordinal);
        Assert.False(gameObject.Components[0].SerializedFields.ContainsKey("Texture"));

        EditorAssetDropResult matched = EditorAssetDropService.DropOnInspectorField(scene, undo, texture, target);

        Assert.True(matched.Succeeded);
        string encoded = gameObject.Components[0].SerializedFields["Texture"];
        Assert.True(EditorAssetReferenceCodec.TryDecode(encoded, out EditorAssetReference decoded));
        Assert.Equal("asset_texture", decoded.AssetId);
        Assert.Equal("textures/sand.png", decoded.LogicalPath);
        Assert.Equal(EditorAssetType.Texture, decoded.AssetType);
        Assert.True(undo.Undo(scene));
        Assert.False(gameObject.Components[0].SerializedFields.ContainsKey("Texture"));
    }

    /// <summary>
    /// 验证 Inspector typed asset 字段可承载 prefab/scene/material/script/texture/audio 的 stable reference。
    /// </summary>
    [Fact]
    public void DropOnInspectorFieldAcceptsAllTypedAssetReferenceKinds()
    {
        // Arrange：准备输入与初始状态
        (EditorAssetType Type, string Path)[] cases =
        [
            (EditorAssetType.Prefab, "prefabs/rock.prefab"),
            (EditorAssetType.Scene, "scenes/main.scene"),
            (EditorAssetType.Material, "materials.json"),
            (EditorAssetType.Script, "scripts/Controller.cs"),
            (EditorAssetType.Texture, "textures/sand.png"),
            (EditorAssetType.Audio, "audio/hit.wav"),
        ];

        for (int i = 0; i < cases.Length; i++)
        {
            EditorSceneModel scene = EditorSceneModel.Empty("drop-inspector-kind-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            EditorGameObject gameObject = scene.Create("Receiver");
            gameObject.Components.Add(new EditorComponentModel(typeof(AssetDropProbeBehaviour).FullName!));
            EditorUndoStack undo = new();
            string fieldName = "Asset" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            EditorAssetDropPayload payload = new("asset_" + cases[i].Type.ToString().ToLowerInvariant(), cases[i].Path, cases[i].Type);
            EditorAssetInspectorFieldTarget target = new(gameObject.StableId, 0, fieldName, cases[i].Type);

            EditorAssetDropResult result = EditorAssetDropService.DropOnInspectorField(scene, undo, payload, target);

            // Assert：验证预期结果
            Assert.True(result.Succeeded);
            Assert.True(EditorAssetReferenceCodec.TryDecode(gameObject.Components[0].SerializedFields[fieldName], out EditorAssetReference decoded));
            Assert.Equal(cases[i].Type, decoded.AssetType);
            Assert.Equal(cases[i].Path, decoded.LogicalPath);
            Assert.Equal(payload.AssetId, decoded.AssetId);
        }
    }

    /// <summary>
    /// 验证 Inspector 面板可从 ScriptFieldDescriptor 声明生成 typed asset field target 并写入 stable reference。
    /// </summary>
    [Fact]
    public void InspectorPanelAppliesDescriptorDeclaredTypedAssetFieldDrop()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel scene = EditorSceneModel.Empty("drop-inspector-descriptor");
        EditorGameObject gameObject = scene.Create("Receiver");
        gameObject.Components.Add(new EditorComponentModel(typeof(AssetDropProbeBehaviour).FullName!));
        EditorUndoStack undo = new();
        ScriptAssemblyRegistry scripts = new();
        scripts.Register(typeof(AssetDropProbeBehaviour).Assembly);
        GameObjectInspectorPanel panel = new(scene, undo, scripts);
        ScriptFieldDescriptor field = ScriptInspector
            .InspectFields(new AssetDropProbeBehaviour())
            .Single(item => item.Name == nameof(AssetDropProbeBehaviour.TextureReference));
        EditorAssetDropPayload texture = new("asset_texture", "textures/sand.png", EditorAssetType.Texture);
        EditorAssetDropPayload audio = new("asset_audio", "audio/hit.wav", EditorAssetType.Audio);

        EditorAssetDropResult mismatch = panel.ApplyAssetDropPayloadToField(gameObject.StableId, 0, field, audio);

        // Assert：验证预期结果
        Assert.False(mismatch.Succeeded);
        Assert.False(gameObject.Components[0].SerializedFields.ContainsKey(nameof(AssetDropProbeBehaviour.TextureReference)));

        EditorAssetDropResult matched = panel.ApplyAssetDropPayloadToField(gameObject.StableId, 0, field, texture);

        Assert.True(matched.Succeeded);
        string encoded = gameObject.Components[0].SerializedFields[nameof(AssetDropProbeBehaviour.TextureReference)];
        Assert.Equal("textures/sand.png [asset_texture]", GameObjectInspectorPanel.FormatAssetReferenceDisplay(field, encoded));
        Assert.True(EditorAssetReferenceCodec.TryDecode(encoded, out EditorAssetReference decoded));
        Assert.Equal(EditorAssetType.Texture, decoded.AssetType);
        Assert.Equal("asset_texture", decoded.AssetId);
        Assert.Equal("textures/sand.png", decoded.LogicalPath);
    }

    /// <summary>
    /// 验证 Inspector 面板接入口会消费 Project Window typed browser payload，写入字段并同步状态/Console 诊断。
    /// </summary>
    [Fact]
    public void InspectorPanelAcceptsProjectWindowBrowserPayloadAndRecordsDiagnostics()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel scene = EditorSceneModel.Empty("drop-inspector-browser-payload");
        EditorGameObject gameObject = scene.Create("Receiver");
        gameObject.Components.Add(new EditorComponentModel(typeof(AssetDropProbeBehaviour).FullName!));
        EditorUndoStack undo = new();
        ScriptAssemblyRegistry scripts = new();
        scripts.Register(typeof(AssetDropProbeBehaviour).Assembly);
        EditorConsoleStore console = new();
        GameObjectInspectorPanel panel = new(scene, undo, scripts, console);
        ScriptFieldDescriptor field = ScriptInspector
            .InspectFields(new AssetDropProbeBehaviour())
            .Single(item => item.Name == nameof(AssetDropProbeBehaviour.TextureReference));

        EditorAssetDropResult invalid = panel.AcceptAssetBrowserDragPayloadToField(
            gameObject.StableId,
            componentIndex: 0,
            field,
            new AssetBrowserDragPayload(string.Empty, "textures/sand.png", AssetBrowserItemKind.Texture));

        // Assert：验证预期结果
        Assert.False(invalid.Succeeded);
        Assert.Contains("stable asset id", panel.Status, StringComparison.Ordinal);
        Assert.Empty(gameObject.Components[0].SerializedFields);

        EditorAssetDropResult valid = panel.AcceptAssetBrowserDragPayloadToField(
            gameObject.StableId,
            componentIndex: 0,
            field,
            new AssetBrowserDragPayload("asset_texture", "textures/sand.png", AssetBrowserItemKind.Texture));

        Assert.True(valid.Succeeded);
        Assert.Contains("textures/sand.png", panel.Status, StringComparison.Ordinal);
        string encoded = gameObject.Components[0].SerializedFields[nameof(AssetDropProbeBehaviour.TextureReference)];
        Assert.True(EditorAssetReferenceCodec.TryDecode(encoded, out EditorAssetReference decoded));
        Assert.Equal(EditorAssetType.Texture, decoded.AssetType);
        EditorConsoleEntry[] entries = console.Snapshot(new EditorConsoleFilter { Category = EditorConsoleCategory.Asset });
        Assert.Equal(2, entries.Length);
        Assert.Equal(EditorConsoleSeverity.Warning, entries[0].Severity);
        Assert.Equal(EditorConsoleSeverity.Info, entries[1].Severity);
        Assert.All(entries, entry => Assert.Equal("inspector-asset-drop", entry.Source));
    }

    /// <summary>
    /// 验证资产移动会重写 Inspector 字段中的 stable asset reference，而不是只重写 prefab link。
    /// </summary>
    [Fact]
    public void MoveAssetRewritesInspectorAssetReferencesInActiveSceneAndDocuments()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord texture = manifest.CreateAsset("textures/sand.png", EditorAssetType.Texture, textContents: "texture");
            string oldReference = EditorAssetReferenceCodec.Encode(texture.Id, texture.LogicalPath, texture.AssetType);
            EngineSceneDocument document = CreateSceneWithAssetReference(oldReference);
            string scenePath = Path.Combine(contentRoot, "scenes", "main.scene");
            EngineSceneDocumentLoader.SaveDocument(document, scenePath);
            EditorSceneModel activeScene = EditorSceneModel.FromDocument(document);

            EditorAssetMoveResult result = manifest.MoveAsset("textures/sand.png", "textures/moved/sand.png", activeScene);

            // Assert：验证预期结果
            Assert.Equal(texture.Id, result.Asset.Id);
            Assert.True(result.UpdatedActiveScene);
            Assert.Equal(1, result.UpdatedReferenceDocuments);
            string activeValue = activeScene.Get(10).Components[0].SerializedFields["Texture"];
            Assert.True(EditorAssetReferenceCodec.TryDecode(activeValue, out EditorAssetReference activeReference));
            Assert.Equal(texture.Id, activeReference.AssetId);
            Assert.Equal("textures/moved/sand.png", activeReference.LogicalPath);
            string savedValue = EngineSceneDocumentLoader.LoadDocument(scenePath).Entities![0].Behaviours![0].SerializedFields!["Texture"];
            Assert.True(EditorAssetReferenceCodec.TryDecode(savedValue, out EditorAssetReference savedReference));
            Assert.Equal(texture.Id, savedReference.AssetId);
            Assert.Equal("textures/moved/sand.png", savedReference.LogicalPath);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 script 资产可拖拽到组件列表添加 Behaviour，非 script 或无法解析的脚本会给出诊断且无副作用。
    /// </summary>
    [Fact]
    public void DropScriptOnComponentListAddsBehaviourAndRejectsInvalidScripts()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel scene = EditorSceneModel.Empty("drop-script");
        EditorGameObject gameObject = scene.Create("Receiver");
        ScriptAssemblyRegistry scripts = new();
        scripts.Register(typeof(AssetDropProbeBehaviour).Assembly);
        EditorUndoStack undo = new();
        EditorAssetDropPayload texture = new("asset_texture", "textures/sand.png", EditorAssetType.Texture);
        EditorAssetDropPayload missingScript = new("asset_missing_script", "scripts/MissingBehaviour.cs", EditorAssetType.Script);
        EditorAssetDropPayload script = new("asset_script", "scripts/AssetDropProbeBehaviour.cs", EditorAssetType.Script);

        EditorAssetDropResult wrongType = EditorAssetDropService.DropScriptOnComponentList(scene, undo, scripts, texture, gameObject.StableId);
        EditorAssetDropResult unresolved = EditorAssetDropService.DropScriptOnComponentList(scene, undo, scripts, missingScript, gameObject.StableId);

        // Assert：验证预期结果
        Assert.False(wrongType.Succeeded);
        Assert.False(unresolved.Succeeded);
        Assert.Empty(gameObject.Components);

        EditorAssetDropResult added = EditorAssetDropService.DropScriptOnComponentList(scene, undo, scripts, script, gameObject.StableId);

        Assert.True(added.Succeeded);
        EditorComponentModel component = Assert.Single(gameObject.Components);
        Assert.Equal(typeof(AssetDropProbeBehaviour).FullName, component.TypeName);
        Assert.True(undo.Undo(scene));
        Assert.Empty(gameObject.Components);
    }

    private static EngineSceneDocument CreateSceneWithAssetReference(string reference)
    {
        return new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "asset-reference",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 10,
                    Name = "Receiver",
                    Transform = new EngineSceneTransformDocument(),
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = typeof(AssetDropProbeBehaviour).FullName!,
                            SerializedFields = new Dictionary<string, string>
                            {
                                ["Texture"] = reference,
                            },
                        },
                    ],
                },
            ],
        };
    }

    private static void SavePrefabDocument(string contentRoot, string logicalPath, string name)
    {
        string fullPath = Path.Combine(contentRoot, logicalPath.Replace('/', Path.DirectorySeparatorChar));
        EngineSceneDocumentLoader.SaveDocument(
            new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = name,
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 1,
                        Name = name,
                        Transform = new EngineSceneTransformDocument(),
                    },
                ],
            },
            fullPath);
    }

    private static string CreateTempProjectRoot()
    {
        return Path.Combine(Path.GetTempPath(), "pixelengine-asset-drop-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// 资产拖拽测试用 Behaviour。
    /// </summary>
    public sealed class AssetDropProbeBehaviour : Behaviour
    {
        /// <summary>
        /// 测试用 texture asset reference 字段。
        /// </summary>
        public string Texture { get; set; } = string.Empty;

        /// <summary>
        /// 测试用强类型 texture asset reference 字段。
        /// </summary>
        [AssetField(ScriptAssetKind.Texture)]
        public ScriptAssetReference TextureReference;
    }
}
