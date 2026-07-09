using System.Text.Json;
using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Editor.Shell.Build;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Project Window 工程级资产模型测试。
/// 不变式：工程资产树与磁盘同步、重命名/移动保持引用完整性。
/// </summary>
public sealed class EditorProjectAssetModelTests
{
    /// <summary>
    /// 验证 manifest 生成 stable asset id、重载后保持稳定，并能被 Project Window 数据源消费。
    /// </summary>
    [Fact]
    public void ManifestGeneratesStableIdsAndProjectBrowserConsumesLogicalAssets()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "textures"));
            File.WriteAllBytes(Path.Combine(contentRoot, "textures", "sand.png"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(contentRoot, "materials.json"), "{}\n");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);

            // Assert：验证预期结果
            EditorAssetRecord texture = Assert.Single(
                manifest.Refresh(),
                asset => asset.LogicalPath == "textures/sand.png");
            string stableId = texture.Id;
            EditorAssetRecord reloaded = Assert.Single(
                new EditorAssetManifestStore(projectRoot, contentRoot).Refresh(),
                asset => asset.LogicalPath == "textures/sand.png");
            EditorAssetRecord script = manifest.CreateAsset("scripts/DemoBehaviour.cs", EditorAssetType.Script);
            EditorAssetBrowserDataSource source = new(manifest);

            IReadOnlyList<AssetBrowserItem> browserItems = source.ListAssets();

            Assert.StartsWith("asset_", stableId, StringComparison.Ordinal);
            Assert.Equal(stableId, reloaded.Id);
            Assert.Equal(EditorAssetType.Texture, texture.AssetType);
            Assert.True(File.Exists(Path.Combine(contentRoot, "scripts", "DemoBehaviour.cs")));
            AssetBrowserItem scriptItem = Assert.Single(browserItems, item => item.Path == "scripts/DemoBehaviour.cs");
            Assert.Equal(AssetBrowserItemKind.Script, scriptItem.Kind);
            Assert.Equal(script.Id, scriptItem.AssetId);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 Project Window 创建常见资产时落盘默认模板、登记 stable asset id 并刷新浏览器数据。
    /// </summary>
    [Fact]
    public void ProjectBrowserCreatesCommonAssetsWithTemplatesAndStableIds()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            _ = Directory.CreateDirectory(contentRoot);
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetBrowserDataSource source = new(manifest);

            AssetBrowserCreateResult folder = source.CreateAsset(new AssetBrowserCreateRequest("levels", AssetBrowserItemKind.Folder));
            AssetBrowserCreateResult material = source.CreateAsset(new AssetBrowserCreateRequest("materials.json", AssetBrowserItemKind.Material));
            AssetBrowserCreateResult scene = source.CreateAsset(new AssetBrowserCreateRequest("scenes/NewScene.scene", AssetBrowserItemKind.Scene));
            AssetBrowserCreateResult prefab = source.CreateAsset(new AssetBrowserCreateRequest("prefabs/NewPrefab.prefab", AssetBrowserItemKind.Prefab));
            AssetBrowserCreateResult script = source.CreateAsset(new AssetBrowserCreateRequest("scripts/NewBehaviour.cs", AssetBrowserItemKind.Script));
            AssetBrowserCreateResult uiScreen = source.CreateAsset(new AssetBrowserCreateRequest("ui/screens/NewScreen.xhtml", AssetBrowserItemKind.UiScreen));
            AssetBrowserCreateResult json = source.CreateAsset(new AssetBrowserCreateRequest("data/NewAsset.json", AssetBrowserItemKind.Json));
            AssetBrowserCreateResult texture = source.CreateAsset(new AssetBrowserCreateRequest("textures/generated.png", AssetBrowserItemKind.Texture));

            IReadOnlyList<AssetBrowserItem> assets = source.ListAssets();
            IReadOnlyList<AssetBrowserFolderItem> folders = source.ListFolders();

            // Assert：验证预期结果
            Assert.True(folder.Succeeded);
            Assert.True(material.Succeeded);
            Assert.True(scene.Succeeded);
            Assert.True(prefab.Succeeded);
            Assert.True(script.Succeeded);
            Assert.True(uiScreen.Succeeded);
            Assert.True(json.Succeeded);
            Assert.False(texture.Succeeded);
            Assert.Null(folder.AssetId);
            Assert.Equal("levels", folder.Path);
            Assert.True(Directory.Exists(Path.Combine(contentRoot, "levels")));
            Assert.Contains(folders, item => item.Path == "levels" && item.AssetCount == 0);
            Assert.DoesNotContain(assets, item => item.Path == "levels");
            Assert.StartsWith("asset_", material.AssetId, StringComparison.Ordinal);
            Assert.Equal(AssetBrowserItemKind.Material, Find(assets, "materials.json").Kind);
            Assert.StartsWith("asset_", scene.AssetId, StringComparison.Ordinal);
            Assert.Equal(AssetBrowserItemKind.Scene, Find(assets, "scenes/NewScene.scene").Kind);
            Assert.Equal(AssetBrowserItemKind.Prefab, Find(assets, "prefabs/NewPrefab.prefab").Kind);
            Assert.Equal(AssetBrowserItemKind.Script, Find(assets, "scripts/NewBehaviour.cs").Kind);
            Assert.Equal(AssetBrowserItemKind.UiScreen, Find(assets, "ui/screens/NewScreen.xhtml").Kind);
            Assert.Equal(AssetBrowserItemKind.Json, Find(assets, "data/NewAsset.json").Kind);

            EngineSceneDocument sceneDocument = EngineSceneDocumentLoader.LoadDocument(Path.Combine(contentRoot, "scenes", "NewScene.scene"));
            EngineSceneDocument prefabDocument = EngineSceneDocumentLoader.LoadDocument(Path.Combine(contentRoot, "prefabs", "NewPrefab.prefab"));
            Assert.Equal("{\"materials\":[]}" + Environment.NewLine, File.ReadAllText(Path.Combine(contentRoot, "materials.json")));
            Assert.StartsWith("材质定义：0 项", Find(assets, "materials.json").PreviewSummary, StringComparison.Ordinal);
            string uiScreenText = File.ReadAllText(Path.Combine(contentRoot, "ui", "screens", "NewScreen.xhtml"));
            Assert.Contains("<rml title=\"NewScreen\"", uiScreenText, StringComparison.Ordinal);
            Assert.Contains("data-screen=\"new-screen\"", uiScreenText, StringComparison.Ordinal);
            Assert.Contains("data-contract=\"editor.ui-screen/v1\"", uiScreenText, StringComparison.Ordinal);
            Assert.StartsWith("UI Screen：NewScreen，", Find(assets, "ui/screens/NewScreen.xhtml").PreviewSummary, StringComparison.Ordinal);
            using JsonDocument uiManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(contentRoot, "ui", "ui-manifest.json")));
            JsonElement screen = Assert.Single(uiManifest.RootElement.GetProperty("screens").EnumerateArray());
            Assert.Equal("new-screen", screen.GetProperty("id").GetString());
            Assert.Equal("screens/NewScreen.xhtml", screen.GetProperty("path").GetString());
            Assert.True(screen.GetProperty("preload").GetBoolean());
            Assert.Equal("NewScene", sceneDocument.Name);
            Assert.Empty(sceneDocument.Entities ?? []);
            Assert.Equal("NewPrefab", prefabDocument.Name);
            EngineSceneEntityDocument prefabRoot = Assert.Single(prefabDocument.Entities ?? []);
            Assert.Equal("NewPrefab", prefabRoot.Name);
            Assert.Contains("public sealed class NewBehaviour : Behaviour", File.ReadAllText(Path.Combine(contentRoot, "scripts", "NewBehaviour.cs")), StringComparison.Ordinal);
            Assert.Equal("{}" + Environment.NewLine, File.ReadAllText(Path.Combine(contentRoot, "data", "NewAsset.json")));
            Assert.Contains("暂不支持", texture.Diagnostic, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 Project Window 创建文件夹拒绝越界、重复目录和与文件同名的目标。
    /// </summary>
    [Fact]
    public void ProjectBrowserCreateFolderRejectsUnsafeAndConflictingPaths()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            _ = Directory.CreateDirectory(contentRoot);
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetBrowserDataSource source = new(manifest);

            AssetBrowserCreateResult created = source.CreateAsset(new AssetBrowserCreateRequest("levels", AssetBrowserItemKind.Folder));
            AssetBrowserCreateResult duplicate = source.CreateAsset(new AssetBrowserCreateRequest("levels", AssetBrowserItemKind.Folder));
            AssetBrowserCreateResult escaped = source.CreateAsset(new AssetBrowserCreateRequest("../outside", AssetBrowserItemKind.Folder));
            AssetBrowserCreateResult material = source.CreateAsset(new AssetBrowserCreateRequest("materials.json", AssetBrowserItemKind.Material));
            AssetBrowserCreateResult fileConflict = source.CreateAsset(new AssetBrowserCreateRequest("materials.json", AssetBrowserItemKind.Folder));

            // Assert：验证预期结果
            Assert.True(created.Succeeded);
            Assert.False(duplicate.Succeeded);
            Assert.False(escaped.Succeeded);
            Assert.True(material.Succeeded);
            Assert.False(fileConflict.Succeeded);
            Assert.Contains("已存在", duplicate.Diagnostic, StringComparison.Ordinal);
            Assert.Contains("content 根目录", escaped.Diagnostic, StringComparison.Ordinal);
            Assert.Contains("同名资产文件", fileConflict.Diagnostic, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(projectRoot, "outside")));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证移动 prefab 时 stable asset id 不变，并重写场景 / 活动 authoring 模型中的 prefab 引用。
    /// </summary>
    [Fact]
    public void MovePrefabKeepsAssetIdAndRewritesScenePrefabReferences()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            SavePrefabDocument(contentRoot, "prefabs/rock.prefab", "Rock");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord original = manifest.EnsureAsset("prefabs/rock.prefab");
            string scenePath = Path.Combine(contentRoot, "scenes", "main.scene");
            EngineSceneDocument sceneDocument = new()
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = "main",
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 10,
                        Name = "Rock Instance",
                        Transform = new EngineSceneTransformDocument(),
                        Prefab = new EngineScenePrefabDocument
                        {
                            AssetId = original.Id,
                            AssetPath = "prefabs/rock.prefab",
                            SourceStableId = "1",
                        },
                    },
                ],
            };
            EngineSceneDocumentLoader.SaveDocument(sceneDocument, scenePath);
            EditorSceneModel activeScene = EditorSceneModel.FromDocument(sceneDocument);

            EditorAssetMoveResult result = manifest.MoveAsset(
                "prefabs/rock.prefab",
                "prefabs/moved/rock.prefab",
                activeScene);

            // Assert：验证预期结果
            Assert.Equal(original.Id, result.Asset.Id);
            Assert.Equal("prefabs/moved/rock.prefab", result.Asset.LogicalPath);
            Assert.True(result.UpdatedActiveScene);
            Assert.Equal(1, result.UpdatedReferenceDocuments);
            EditorPrefabLink activeLink = activeScene.Get(10).PrefabLink!;
            Assert.Equal(original.Id, activeLink.AssetId);
            Assert.Equal("prefabs/moved/rock.prefab", activeLink.AssetPath);
            EngineScenePrefabDocument savedLink = EngineSceneDocumentLoader.LoadDocument(scenePath).Entities![0].Prefab!;
            Assert.Equal(original.Id, savedLink.AssetId);
            Assert.Equal("prefabs/moved/rock.prefab", savedLink.AssetPath);

            EditorSceneModel staleScene = EditorSceneModel.FromDocument(sceneDocument);
            EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
            prefabs.RefreshPrefabInstances(staleScene);

            EditorPrefabLink resolved = staleScene.Get(10).PrefabLink!;
            Assert.Equal(original.Id, resolved.AssetId);
            Assert.Equal("prefabs/moved/rock.prefab", resolved.AssetPath);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证移动 Scene 资产时同步工程、Project/Player/Build Settings 与玩家 startup 引用。
    /// </summary>
    [Fact]
    public void MoveSceneAssetSynchronizesProjectSettingsAndBuildProfile()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            EditorProject project = EditorProject.CreateNew(projectRoot, "Scene Move Project");
            string contentRoot = project.ContentRootPath;
            string oldScene = "scenes/main.scene";
            string newScene = "scenes/moved/lava.scene";
            EngineProjectSettingsStore.SaveProjectSettings(
                projectRoot,
                ProjectSettingsDto.CreateDefault(project.Name) with
                {
                    StartScene = oldScene,
                });
            EngineProjectSettingsStore.SavePlayerSettings(
                projectRoot,
                PlayerSettingsDto.CreateDefault(project.Name) with
                {
                    StartupScene = oldScene,
                });
            EngineProjectSettingsStore.SaveStartupSettings(
                contentRoot,
                EngineProjectStartupSettings.CreateDefault() with
                {
                    StartScene = oldScene,
                });
            BuildProfileDto buildProfile = new()
            {
                OutputDirectory = "artifacts/player",
                ProductName = "Scene Move Project",
                Version = "0.1.0",
                PackageWholeContent = false,
                Scenes =
                [
                    new BuildProfileSceneDto
                    {
                        SceneName = "main",
                        Source = oldScene,
                        Included = true,
                        IsStartup = true,
                        SourceKind = SceneSourceKind.SceneFile,
                    },
                ],
            };
            EngineProjectSettingsStore.SaveBuildProfile(projectRoot, buildProfile);
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            _ = manifest.EnsureAsset(oldScene);
            EditorAssetBrowserDataSource source = new(
                manifest,
                sceneAssetMoveService: new EditorProjectSceneAssetMoveService(project, manifest));

            EditorAssetBrowserMoveResult result = source.MoveAsset(oldScene, newScene);

            // Assert：验证预期结果
            Assert.True(result.Succeeded);
            Assert.Equal(newScene, result.Asset.LogicalPath);
            Assert.Contains("project=1", result.Diagnostic, StringComparison.Ordinal);
            Assert.Contains("buildSettings=1", result.Diagnostic, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(contentRoot, oldScene.Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(File.Exists(Path.Combine(contentRoot, newScene.Replace('/', Path.DirectorySeparatorChar))));

            EditorProject reloadedProject = EditorProject.Load(projectRoot);
            Assert.Equal(newScene, reloadedProject.StartScene);
            Assert.DoesNotContain(reloadedProject.Scenes, scene => string.Equals(scene.Path, oldScene, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(reloadedProject.Scenes, scene => string.Equals(scene.Path, newScene, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(newScene, EngineProjectSettingsStore.LoadProjectSettings(projectRoot).StartScene);
            Assert.Equal(newScene, EngineProjectSettingsStore.LoadPlayerSettings(projectRoot).StartupScene);
            Assert.Equal(newScene, EngineProjectSettingsStore.LoadStartupSettings(contentRoot).StartScene);

            BuildProfileDto movedBuildProfile = new BuildSettingsStore(project).Load();
            BuildRequest request = movedBuildProfile.ToRequest();
            Assert.Equal(newScene, request.StartScene);
            Assert.Equal([newScene], request.IncludedScenes);
            Assert.DoesNotContain(movedBuildProfile.Scenes, scene => string.Equals(scene.Source, oldScene, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 Project Window move request 会回查 stable id，并通过 Shell 数据源重写活动场景与磁盘文档中的 typed asset reference。
    /// </summary>
    [Fact]
    public void ProjectBrowserMoveRequestRechecksStableIdAndRewritesTypedAssetReferences()
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
            EditorAssetBrowserDataSource source = new(manifest);

            AssetBrowserMoveResult stale = source.MoveAsset(
                new AssetBrowserMoveRequest(
                    "textures/sand.png",
                    "asset_missing",
                    AssetBrowserItemKind.Texture,
                    "textures/moved/sand.png"),
                activeScene);
            AssetBrowserMoveResult moved = source.MoveAsset(
                new AssetBrowserMoveRequest(
                    "textures/sand.png",
                    texture.Id,
                    AssetBrowserItemKind.Texture,
                    "textures/moved/sand.png"),
                activeScene);

            string newReference = EditorAssetReferenceCodec.Encode(texture.Id, "textures/moved/sand.png", texture.AssetType);
            // Assert：验证预期结果
            Assert.False(stale.Succeeded);
            Assert.Contains("stable asset id", stale.Diagnostic, StringComparison.Ordinal);
            Assert.True(moved.Succeeded);
            Assert.Contains("重写引用", moved.Diagnostic, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(contentRoot, "textures", "sand.png")));
            Assert.True(File.Exists(Path.Combine(contentRoot, "textures", "moved", "sand.png")));
            Assert.True(manifest.TryResolveAssetId(texture.Id, out EditorAssetRecord resolved));
            Assert.Equal("textures/moved/sand.png", resolved.LogicalPath);
            Assert.Equal(newReference, activeScene.Get(10).Components[0].SerializedFields["Texture"]);
            EngineSceneDocument saved = EngineSceneDocumentLoader.LoadDocument(scenePath);
            Assert.Equal(newReference, saved.Entities![0].Behaviours![0].SerializedFields!["Texture"]);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 Project Window 文件夹递归移动会保留子资产 stable id，并重写活动场景与磁盘文档中的 typed asset reference。
    /// </summary>
    [Fact]
    public void ProjectBrowserMoveFolderKeepsAssetIdsAndRewritesTypedAssetReferences()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord texture = manifest.CreateAsset("levels/textures/sand.png", EditorAssetType.Texture, textContents: "texture");
            EditorAssetRecord script = manifest.CreateAsset("levels/scripts/DemoBehaviour.cs", EditorAssetType.Script);
            string oldReference = EditorAssetReferenceCodec.Encode(texture.Id, texture.LogicalPath, texture.AssetType);
            EngineSceneDocument document = CreateSceneWithAssetReference(oldReference);
            string scenePath = Path.Combine(contentRoot, "scenes", "main.scene");
            EngineSceneDocumentLoader.SaveDocument(document, scenePath);
            EditorSceneModel activeScene = EditorSceneModel.FromDocument(document);
            EditorAssetBrowserDataSource source = new(manifest);

            AssetBrowserFolderMoveResult result = source.MoveFolder(
                new AssetBrowserFolderMoveRequest("levels", "world/levels"),
                activeScene);

            string newReference = EditorAssetReferenceCodec.Encode(texture.Id, "world/levels/textures/sand.png", texture.AssetType);
            // Assert：验证预期结果
            Assert.True(result.Succeeded);
            Assert.Contains("assets=2", result.Diagnostic, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(contentRoot, "levels")));
            Assert.True(File.Exists(Path.Combine(contentRoot, "world", "levels", "textures", "sand.png")));
            Assert.True(File.Exists(Path.Combine(contentRoot, "world", "levels", "scripts", "DemoBehaviour.cs")));
            Assert.True(manifest.TryResolveAssetId(texture.Id, out EditorAssetRecord movedTexture));
            Assert.True(manifest.TryResolveAssetId(script.Id, out EditorAssetRecord movedScript));
            Assert.Equal("world/levels/textures/sand.png", movedTexture.LogicalPath);
            Assert.Equal("world/levels/scripts/DemoBehaviour.cs", movedScript.LogicalPath);
            Assert.Equal(newReference, activeScene.Get(10).Components[0].SerializedFields["Texture"]);
            EngineSceneDocument saved = EngineSceneDocumentLoader.LoadDocument(scenePath);
            Assert.Equal(newReference, saved.Entities![0].Behaviours![0].SerializedFields!["Texture"]);
            Assert.Contains(source.ListFolders(), folder => folder.Path == "world/levels" && folder.AssetCount == 2);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证移动 UI Screen 文件夹时同步 ui-manifest screen path，并拒绝会改变资产类型的目录移动。
    /// </summary>
    [Fact]
    public void ProjectBrowserMoveFolderUpdatesUiManifestAndRejectsAssetTypeChanges()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetBrowserDataSource source = new(manifest);
            AssetBrowserCreateResult created = source.CreateAsset(new AssetBrowserCreateRequest("ui/screens/menu/Hud.xhtml", AssetBrowserItemKind.UiScreen));

            AssetBrowserFolderMoveResult moved = source.MoveFolder(new AssetBrowserFolderMoveRequest("ui/screens/menu", "ui/screens/hud"));
            AssetBrowserFolderMoveResult rejected = source.MoveFolder(new AssetBrowserFolderMoveRequest("ui/screens/hud", "ui/moved"));

            // Assert：验证预期结果
            Assert.True(created.Succeeded);
            Assert.True(moved.Succeeded);
            Assert.False(rejected.Succeeded);
            Assert.Contains("改变资产类型", rejected.Diagnostic, StringComparison.Ordinal);
            Assert.True(manifest.TryResolveAssetId(created.AssetId!, out EditorAssetRecord screen));
            Assert.Equal("ui/screens/hud/Hud.xhtml", screen.LogicalPath);
            using JsonDocument uiManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(contentRoot, "ui", "ui-manifest.json")));
            JsonElement screenEntry = Assert.Single(uiManifest.RootElement.GetProperty("screens").EnumerateArray());
            Assert.Equal("hud", screenEntry.GetProperty("id").GetString());
            Assert.Equal("screens/hud/Hud.xhtml", screenEntry.GetProperty("path").GetString());
            Assert.True(screenEntry.GetProperty("preload").GetBoolean());
            Assert.True(File.Exists(Path.Combine(contentRoot, "ui", "screens", "hud", "Hud.xhtml")));
            Assert.False(Directory.Exists(Path.Combine(contentRoot, "ui", "moved")));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证移动资产会先解析全部引用文档，损坏的 scene/prefab 不会导致文件已移动但引用未重写的半提交状态。
    /// </summary>
    [Fact]
    public void MoveAssetPreflightsReferenceDocumentsBeforeMovingFile()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord texture = manifest.CreateAsset("textures/sand.png", EditorAssetType.Texture, textContents: "texture");
            string brokenScene = Path.Combine(contentRoot, "scenes", "broken.scene");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(brokenScene)!);
            File.WriteAllText(brokenScene, "not json");
            _ = manifest.EnsureAsset("scenes/broken.scene");

            // Assert：验证预期结果
            _ = Assert.ThrowsAny<Exception>(() => manifest.MoveAsset("textures/sand.png", "textures/moved/sand.png"));

            Assert.True(File.Exists(Path.Combine(contentRoot, "textures", "sand.png")));
            Assert.False(File.Exists(Path.Combine(contentRoot, "textures", "moved", "sand.png")));
            Assert.True(manifest.TryResolveAssetId(texture.Id, out EditorAssetRecord resolved));
            Assert.Equal("textures/sand.png", resolved.LogicalPath);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证引用文档保存到一半失败时，已写出的引用文档会恢复到旧路径，活动 authoring 场景不会被污染。
    /// </summary>
    [Fact]
    public void MoveAssetRestoresWrittenReferenceDocumentsWhenLaterSaveFails()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord texture = manifest.CreateAsset("textures/sand.png", EditorAssetType.Texture, textContents: "texture");
            string oldReference = EditorAssetReferenceCodec.Encode(texture.Id, texture.LogicalPath, texture.AssetType);
            string firstScene = Path.Combine(contentRoot, "scenes", "a-first.scene");
            string secondScene = Path.Combine(contentRoot, "scenes", "z-readonly.scene");
            EngineSceneDocumentLoader.SaveDocument(CreateSceneWithAssetReference(oldReference), firstScene);
            EngineSceneDocumentLoader.SaveDocument(CreateSceneWithAssetReference(oldReference), secondScene);
            EditorSceneModel activeScene = EditorSceneModel.FromDocument(CreateSceneWithAssetReference(oldReference));
            _ = manifest.EnsureAsset("scenes/a-first.scene");
            _ = manifest.EnsureAsset("scenes/z-readonly.scene");
            File.SetAttributes(secondScene, FileAttributes.ReadOnly);

            // Assert：验证预期结果
            _ = Assert.ThrowsAny<Exception>(() => manifest.MoveAsset("textures/sand.png", "textures/moved/sand.png", activeScene));

            File.SetAttributes(secondScene, FileAttributes.Normal);
            Assert.True(File.Exists(Path.Combine(contentRoot, "textures", "sand.png")));
            Assert.False(File.Exists(Path.Combine(contentRoot, "textures", "moved", "sand.png")));
            Assert.True(manifest.TryResolveAssetId(texture.Id, out EditorAssetRecord resolved));
            Assert.Equal("textures/sand.png", resolved.LogicalPath);
            Assert.Equal(oldReference, EngineSceneDocumentLoader.LoadDocument(firstScene).Entities![0].Behaviours![0].SerializedFields!["Texture"]);
            Assert.Equal(oldReference, EngineSceneDocumentLoader.LoadDocument(secondScene).Entities![0].Behaviours![0].SerializedFields!["Texture"]);
            Assert.Equal(oldReference, activeScene.Get(10).Components[0].SerializedFields["Texture"]);
        }
        finally
        {
            ClearReadOnlyAttributes(projectRoot);
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证删除预检会阻止仍被 Scene / 活动 authoring 模型引用的资产。
    /// </summary>
    [Fact]
    public void DeleteAssetPreflightBlocksReferencedAssetsAndReportsLocations()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord texture = manifest.CreateAsset("textures/sand.png", EditorAssetType.Texture, textContents: "texture");
            string reference = EditorAssetReferenceCodec.Encode(texture.Id, texture.LogicalPath, texture.AssetType);
            EngineSceneDocument document = CreateSceneWithAssetReference(reference);
            string scenePath = Path.Combine(contentRoot, "scenes", "main.scene");
            EngineSceneDocumentLoader.SaveDocument(document, scenePath);
            EditorSceneModel activeScene = EditorSceneModel.FromDocument(document);

            EditorAssetDeletePreflight preflight = manifest.PreflightDeleteAsset("textures/sand.png", activeScene);
            EditorAssetDeleteResult delete = manifest.DeleteAsset("textures/sand.png", activeScene, confirmed: true);

            // Assert：验证预期结果
            Assert.False(preflight.CanDelete);
            Assert.Equal(2, preflight.ReferenceCount);
            Assert.Equal(1, preflight.ReferenceDocuments);
            Assert.True(preflight.ActiveSceneHasReferences);
            Assert.Contains(preflight.ReferenceLocations, location => location.StartsWith("scenes/main.scene:", StringComparison.Ordinal));
            Assert.Contains(preflight.ReferenceLocations, location => location.StartsWith("active scene:", StringComparison.Ordinal));
            Assert.False(delete.Deleted);
            Assert.False(delete.RequiresConfirmation);
            Assert.Contains("仍被 2 处引用", delete.Diagnostic, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(contentRoot, "textures", "sand.png")));
            Assert.True(manifest.TryResolveAssetId(texture.Id, out _));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证未引用资产必须先确认，确认后才会从磁盘和 manifest 移除。
    /// </summary>
    [Fact]
    public void DeleteAssetRequiresConfirmationAndRemovesUnreferencedManifestRecord()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord texture = manifest.CreateAsset("textures/unused.png", EditorAssetType.Texture, textContents: "texture");

            EditorAssetDeleteResult request = manifest.DeleteAsset("textures/unused.png");
            EditorAssetDeleteResult confirmed = manifest.DeleteAsset("textures/unused.png", confirmed: true);

            // Assert：验证预期结果
            Assert.False(request.Deleted);
            Assert.True(request.RequiresConfirmation);
            Assert.Contains("需要确认", request.Diagnostic, StringComparison.Ordinal);
            Assert.True(confirmed.Deleted);
            Assert.False(confirmed.RequiresConfirmation);
            Assert.False(File.Exists(Path.Combine(contentRoot, "textures", "unused.png")));
            Assert.False(manifest.TryResolveAssetId(texture.Id, out _));
            Assert.DoesNotContain(manifest.Refresh(), asset => asset.Id == texture.Id);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证 Project Window 数据源为纹理、音频、材质、场景、Prefab 与脚本提供只读预览摘要。
    /// </summary>
    [Fact]
    public void ProjectBrowserBuildsPreviewSummariesForCommonAssetTypes()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "textures"));
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "audio"));
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "scripts"));
            File.WriteAllBytes(Path.Combine(contentRoot, "textures", "sand.png"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(contentRoot, "audio", "hit.wav"), [4, 5]);
            File.WriteAllText(Path.Combine(contentRoot, "materials.json"), /*lang=json,strict*/ "{\"materials\":[{\"name\":\"sand\"},{\"name\":\"water\"}]}\n");
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "ui", "screens"));
            File.WriteAllText(Path.Combine(contentRoot, "ui", "screens", "hud.xhtml"), "<rml title=\"HUD\"><body /></rml>\n");
            File.WriteAllText(Path.Combine(contentRoot, "scripts", "DemoBehaviour.cs"), "using PixelEngine.Scripting; public sealed class DemoBehaviour : Behaviour { }\n");
            SavePrefabDocument(contentRoot, "prefabs/rock.prefab", "Rock");
            string scenePath = Path.Combine(contentRoot, "scenes", "main.scene");
            EngineSceneDocumentLoader.SaveDocument(
                new EngineSceneDocument
                {
                    FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                    Name = "main",
                    Entities =
                    [
                        new EngineSceneEntityDocument
                        {
                            StableId = 1,
                            Name = "Root",
                            Transform = new EngineSceneTransformDocument(),
                            Behaviours =
                            [
                                new EngineSceneBehaviourDocument { TypeName = "PixelEngine.Tests.DemoBehaviour" },
                            ],
                        },
                        new EngineSceneEntityDocument
                        {
                            StableId = 2,
                            ParentId = 1,
                            Name = "Child",
                            Transform = new EngineSceneTransformDocument(),
                        },
                    ],
                },
                scenePath);
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetBrowserDataSource source = new(manifest, new FixedThumbnailProvider("textures/sand.png", new AssetThumbnail(99, 32, 16)));

            IReadOnlyList<AssetBrowserItem> assets = source.ListAssets();

            // Assert：验证预期结果
            Assert.Equal("纹理：32×16，3 B", Find(assets, "textures/sand.png").PreviewSummary);
            Assert.Equal("音频：2 B", Find(assets, "audio/hit.wav").PreviewSummary);
            Assert.StartsWith("材质定义：2 项", Find(assets, "materials.json").PreviewSummary, StringComparison.Ordinal);
            Assert.StartsWith("UI Screen：HUD，", Find(assets, "ui/screens/hud.xhtml").PreviewSummary, StringComparison.Ordinal);
            Assert.Equal("场景：2 个 GameObject，1 个根，1 个 Behaviour", Find(assets, "scenes/main.scene").PreviewSummary);
            Assert.Equal("Prefab：1 个 GameObject，1 个根，0 个 Behaviour", Find(assets, "prefabs/rock.prefab").PreviewSummary);
            Assert.StartsWith("脚本：DemoBehaviour，", Find(assets, "scripts/DemoBehaviour.cs").PreviewSummary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    /// <summary>
    /// 验证资产模型拒绝越过 content 根目录的创建和移动路径。
    /// </summary>
    [Fact]
    public void AssetModelRejectsPathsOutsideContentRoot()
    {
        // Arrange：准备输入与初始状态
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            SavePrefabDocument(contentRoot, "prefabs/rock.prefab", "Rock");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            _ = manifest.EnsureAsset("prefabs/rock.prefab");

            // Assert：验证预期结果
            _ = Assert.Throws<InvalidOperationException>(() => manifest.CreateAsset("../outside.prefab", EditorAssetType.Prefab));
            _ = Assert.Throws<InvalidOperationException>(() => manifest.MoveAsset("prefabs/rock.prefab", "../outside.prefab"));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    private static string CreateTempProjectRoot()
    {
        return Path.Combine(Path.GetTempPath(), "pixelengine-project-assets-" + Guid.NewGuid().ToString("N"));
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
                            TypeName = "PixelEngine.Tests.AssetReferenceProbe",
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

    private static AssetBrowserItem Find(IReadOnlyList<AssetBrowserItem> assets, string path)
    {
        return Assert.Single(assets, item => item.Path == path);
    }

    private sealed class FixedThumbnailProvider(string path, AssetThumbnail thumbnail) : ITextureThumbnailProvider
    {
        public bool TryGetThumbnail(string assetPath, out AssetThumbnail resolved)
        {
            if (string.Equals(assetPath, path, StringComparison.OrdinalIgnoreCase))
            {
                resolved = thumbnail;
                return true;
            }

            resolved = default;
            return false;
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
