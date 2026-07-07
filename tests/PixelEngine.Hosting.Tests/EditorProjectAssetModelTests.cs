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
    /// 验证删除预检会阻止仍被 Scene / 活动 authoring 模型引用的资产。
    /// </summary>
    [Fact]
    public void DeleteAssetPreflightBlocksReferencedAssetsAndReportsLocations()
    {
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
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            EditorAssetManifestStore manifest = new(projectRoot, contentRoot);
            EditorAssetRecord texture = manifest.CreateAsset("textures/unused.png", EditorAssetType.Texture, textContents: "texture");

            EditorAssetDeleteResult request = manifest.DeleteAsset("textures/unused.png");
            EditorAssetDeleteResult confirmed = manifest.DeleteAsset("textures/unused.png", confirmed: true);

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
        string projectRoot = CreateTempProjectRoot();
        try
        {
            string contentRoot = Path.Combine(projectRoot, "content");
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "textures"));
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "audio"));
            _ = Directory.CreateDirectory(Path.Combine(contentRoot, "scripts"));
            File.WriteAllBytes(Path.Combine(contentRoot, "textures", "sand.png"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(contentRoot, "audio", "hit.wav"), [4, 5]);
            File.WriteAllText(Path.Combine(contentRoot, "materials.json"), "{\"materials\":[{\"name\":\"sand\"},{\"name\":\"water\"}]}\n");
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

            Assert.Equal("纹理：32×16，3 B", Find(assets, "textures/sand.png").PreviewSummary);
            Assert.Equal("音频：2 B", Find(assets, "audio/hit.wav").PreviewSummary);
            Assert.StartsWith("材质定义：2 项", Find(assets, "materials.json").PreviewSummary, StringComparison.Ordinal);
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
}
