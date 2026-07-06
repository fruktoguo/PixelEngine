using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Project Window 工程级资产模型测试。
/// </summary>
public sealed class EditorProjectAssetModelTests
{
    /// <summary>
    /// 验证 manifest 生成 stable asset id、重载后保持稳定，并能被 Project Window 数据源消费。
    /// </summary>
    [Fact]
    public void ManifestGeneratesStableIdsAndProjectBrowserConsumesLogicalAssets()
    {
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "textures"));
            File.WriteAllBytes(Path.Combine(contentRoot, "textures", "sand.png"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(contentRoot, "materials.json"), "{}\n");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);

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
    /// 验证移动 prefab 时 stable asset id 不变，并重写场景 / 活动 authoring 模型中的 prefab 引用。
    /// </summary>
    [Fact]
    public void MovePrefabKeepsAssetIdAndRewritesScenePrefabReferences()
    {
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
    /// 验证资产模型拒绝越过 content 根目录的创建和移动路径。
    /// </summary>
    [Fact]
    public void AssetModelRejectsPathsOutsideContentRoot()
    {
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            SavePrefabDocument(contentRoot, "prefabs/rock.prefab", "Rock");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            _ = manifest.EnsureAsset("prefabs/rock.prefab");

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

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
