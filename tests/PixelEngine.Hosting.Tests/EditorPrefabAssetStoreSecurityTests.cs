using PixelEngine.Editor.Automation.Server;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Prefab automation 的路径边界、manifest 与精确 Undo 回归。
/// </summary>
public sealed class EditorPrefabAssetStoreSecurityTests
{
    /// <summary>Prefab logical path 必须是无歧义、可跨平台持久化的 content-root-relative path。</summary>
    [Theory]
    [InlineData("../outside.prefab")]
    [InlineData("/absolute.prefab")]
    [InlineData("C:/absolute.prefab")]
    [InlineData("prefabs//empty.prefab")]
    [InlineData("prefabs/./current.prefab")]
    [InlineData("prefabs/../escape.prefab")]
    [InlineData("prefabs./trailing-dot.prefab")]
    [InlineData("prefabs /trailing-space.prefab")]
    [InlineData("prefabs/asset:stream.prefab")]
    [InlineData("prefabs/CON.prefab")]
    [InlineData("prefabs/CONIN$.prefab")]
    [InlineData("prefabs/LPT9.prefab")]
    [InlineData("prefabs/COM¹.prefab")]
    [InlineData("prefabs/not-a-prefab.scene")]
    public void NormalizeAssetPathRejectsAmbiguousOrUnsafePaths(string assetPath)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => EditorPrefabAssetStore.NormalizeAssetPath(assetPath));

        Assert.Contains("prefab", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Windows separator 只作输入兼容，协议与 manifest 始终返回 slash canonical path。</summary>
    [Fact]
    public void NormalizeAssetPathCanonicalizesDirectorySeparators()
    {
        Assert.Equal(
            "prefabs/characters/player.prefab",
            EditorPrefabAssetStore.NormalizeAssetPath(
                " prefabs\\characters\\player.prefab "));
    }

    /// <summary>即使 lexical path 留在 content 内，也不得通过 symlink/reparse point 逃逸。</summary>
    [Fact]
    public void PrefabReadRejectsReparsePointEscape()
    {
        using TempDirectory temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        string prefabRoot = Path.Combine(contentRoot, "prefabs");
        string outsidePath = Path.Combine(temp.Path, "outside.prefab");
        string linkPath = Path.Combine(prefabRoot, "escape.prefab");
        _ = Directory.CreateDirectory(prefabRoot);
        File.WriteAllText(outsidePath, "{}");
        _ = File.CreateSymbolicLink(linkPath, outsidePath);
        try
        {
            EditorPrefabAssetStore store = new(contentRoot);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => store.TryReadAsset("prefabs/escape.prefab", out _));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("{}", File.ReadAllText(outsidePath));
        }
        finally
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }
        }
    }

    /// <summary>content root 自身也属于信任边界，不能用 directory symlink 指向工程外。</summary>
    [Fact]
    public void PrefabReadRejectsReparsePointContentRoot()
    {
        using TempDirectory temp = new();
        string outsideRoot = Path.Combine(temp.Path, "outside-content");
        string prefabRoot = Path.Combine(outsideRoot, "prefabs");
        string contentRootLink = Path.Combine(temp.Path, "content-link");
        _ = Directory.CreateDirectory(prefabRoot);
        File.WriteAllText(Path.Combine(prefabRoot, "escape.prefab"), "{}");
        _ = Directory.CreateSymbolicLink(contentRootLink, outsideRoot);
        try
        {
            EditorPrefabAssetStore store = new(contentRootLink);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => store.TryReadAsset("prefabs/escape.prefab", out _));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(contentRootLink))
            {
                Directory.Delete(contentRootLink);
            }
        }
    }

    /// <summary>新建 prefab 的 Undo 必须同时恢复 links、选择、文件与 manifest，Redo 仍可完整重放。</summary>
    [Fact]
    public void CreatePrefabUndoRemovesFileAndManifestWithoutChangingSelection()
    {
        using TempDirectory temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        _ = Directory.CreateDirectory(contentRoot);
        EditorAssetManifestStore manifest = new(temp.Path, contentRoot);
        EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
        EditorSceneModel scene = EditorSceneModel.Empty("prefab-undo");
        EditorGameObject root = scene.Create("Root");
        EditorGameObject child = scene.Create("Child", root.StableId);
        EditorGameObject selected = scene.Create("Selected Elsewhere");
        scene.Select(selected.StableId);
        EditorUndoStack undo = new();
        const string AssetPath = "prefabs/root.prefab";
        string fullPath = Path.Combine(contentRoot, "prefabs", "root.prefab");

        undo.Execute(scene, new CreatePrefabAssetCommand(prefabs, root.StableId, AssetPath));

        Assert.True(File.Exists(fullPath));
        Assert.True(manifest.TryResolveLogicalPath(AssetPath, out EditorAssetRecord created));
        Assert.StartsWith("asset_", created.Id, StringComparison.Ordinal);
        Assert.NotNull(root.PrefabLink);
        Assert.NotNull(child.PrefabLink);
        Assert.Equal(selected.StableId, scene.SelectedStableId);

        Assert.True(undo.Undo(scene));
        Assert.False(File.Exists(fullPath));
        Assert.False(manifest.TryResolveLogicalPath(AssetPath, out _));
        Assert.Null(root.PrefabLink);
        Assert.Null(child.PrefabLink);
        Assert.Equal(selected.StableId, scene.SelectedStableId);

        Assert.True(undo.Redo(scene));
        Assert.True(File.Exists(fullPath));
        Assert.True(manifest.TryResolveLogicalPath(AssetPath, out _));
        Assert.NotNull(root.PrefabLink);
        Assert.NotNull(child.PrefabLink);
        Assert.Equal(selected.StableId, scene.SelectedStableId);
    }

    /// <summary>覆盖既有 prefab 后，Undo 必须恢复原字节与原 stable asset id。</summary>
    [Fact]
    public void CreatePrefabUndoRestoresExistingAssetBytesAndIdentity()
    {
        using TempDirectory temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        _ = Directory.CreateDirectory(contentRoot);
        EditorAssetManifestStore manifest = new(temp.Path, contentRoot);
        EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
        const string AssetPath = "prefabs/shared.prefab";
        string fullPath = Path.Combine(contentRoot, "prefabs", "shared.prefab");
        EditorSceneModel baselineScene = EditorSceneModel.Empty("baseline");
        EditorGameObject baseline = baselineScene.Create("Baseline");
        prefabs.CreatePrefabFromSubtree(baselineScene, baseline.StableId, AssetPath);
        byte[] baselineBytes = File.ReadAllBytes(fullPath);
        Assert.True(manifest.TryResolveLogicalPath(AssetPath, out EditorAssetRecord baselineRecord));

        EditorSceneModel replacementScene = EditorSceneModel.Empty("replacement");
        EditorGameObject replacement = replacementScene.Create("Replacement");
        EditorGameObject selected = replacementScene.Create("Selected Elsewhere");
        replacementScene.Select(selected.StableId);
        EditorUndoStack undo = new();
        undo.Execute(
            replacementScene,
            new CreatePrefabAssetCommand(prefabs, replacement.StableId, AssetPath));
        Assert.False(baselineBytes.AsSpan().SequenceEqual(File.ReadAllBytes(fullPath)));

        Assert.True(undo.Undo(replacementScene));

        Assert.Equal(baselineBytes, File.ReadAllBytes(fullPath));
        Assert.True(manifest.TryResolveLogicalPath(AssetPath, out EditorAssetRecord restored));
        Assert.Equal(baselineRecord.Id, restored.Id);
        Assert.Null(replacement.PrefabLink);
        Assert.Equal(selected.StableId, replacementScene.SelectedStableId);
    }

    /// <summary>manifest 持久化失败时，命令回滚只清理已写文件，不触碰未成功更新的 manifest 或场景。</summary>
    [Fact]
    public void CreatePrefabFailureRollsBackOnlyCompletedSideEffects()
    {
        using TempDirectory temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        _ = Directory.CreateDirectory(contentRoot);
        EditorAssetManifestStore manifest = new(
            temp.Path,
            contentRoot,
            "manifest-destination");
        _ = Directory.CreateDirectory(manifest.ManifestPath);
        EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
        EditorSceneModel scene = EditorSceneModel.Empty("prefab-failure");
        EditorGameObject source = scene.Create("Source");
        EditorGameObject selected = scene.Create("Selected Elsewhere");
        scene.Select(selected.StableId);
        CreatePrefabAssetCommand command = new(
            prefabs,
            source.StableId,
            "prefabs/failure.prefab");

        Exception exception = Assert.ThrowsAny<Exception>(() => command.Execute(scene));
        command.Undo(scene);

        Assert.True(exception is IOException or UnauthorizedAccessException, exception.ToString());
        Assert.False(File.Exists(Path.Combine(contentRoot, "prefabs", "failure.prefab")));
        Assert.True(Directory.Exists(manifest.ManifestPath));
        Assert.Null(source.PrefabLink);
        Assert.Equal(selected.StableId, scene.SelectedStableId);
    }

    /// <summary>损坏 prefab 必须在 manifest 写入前失败，不得留下不可实例化的幽灵资产。</summary>
    [Fact]
    public void InstantiateInvalidPrefabDoesNotCreateManifestRecord()
    {
        using TempDirectory temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        string prefabDirectory = Path.Combine(contentRoot, "prefabs");
        _ = Directory.CreateDirectory(prefabDirectory);
        const string AssetPath = "prefabs/corrupt.prefab";
        File.WriteAllText(Path.Combine(prefabDirectory, "corrupt.prefab"), "{ not-json }");
        EditorAssetManifestStore manifest = new(temp.Path, contentRoot);
        EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
        EditorSceneModel scene = EditorSceneModel.Empty("invalid-prefab");

        _ = Assert.ThrowsAny<Exception>(() => prefabs.InstantiatePrefab(scene, AssetPath, parentId: null));

        Assert.False(File.Exists(manifest.ManifestPath));
        Assert.Equal(0, scene.Count);
    }

    /// <summary>目标 parent 必须在任何 prefab 加载或 manifest 登记前验证，失败不改变 Scene before-image。</summary>
    [Fact]
    public void InstantiatePrefabWithMissingParentDoesNotMutateSceneOrManifest()
    {
        using TempDirectory temp = new();
        string contentRoot = Path.Combine(temp.Path, "content");
        _ = Directory.CreateDirectory(contentRoot);
        const string AssetPath = "prefabs/valid.prefab";
        EditorSceneModel sourceScene = EditorSceneModel.Empty("source");
        EditorGameObject source = sourceScene.Create("Source");
        new EditorPrefabAssetStore(contentRoot).CreatePrefabFromSubtree(
            sourceScene,
            source.StableId,
            AssetPath);
        EditorAssetManifestStore manifest = new(temp.Path, contentRoot);
        EditorPrefabAssetStore prefabs = new(contentRoot, manifest);
        EditorSceneModel targetScene = EditorSceneModel.Empty("target");
        EditorGameObject selected = targetScene.Create("Selected");
        targetScene.MarkSaved();
        int beforeVersion = targetScene.Version;

        _ = Assert.Throws<KeyNotFoundException>(() =>
            prefabs.InstantiatePrefab(targetScene, AssetPath, parentId: int.MaxValue));

        Assert.Equal(1, targetScene.Count);
        Assert.Equal(selected.StableId, targetScene.SelectedStableId);
        Assert.False(targetScene.IsDirty);
        Assert.Equal(beforeVersion, targetScene.Version);
        Assert.False(File.Exists(manifest.ManifestPath));
    }

    /// <summary>重父循环必须在命令执行前拒绝，原层级保持不变。</summary>
    [Fact]
    public void AutomationReparentRejectsDescendantCyclesBeforeMutation()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("reparent-cycle");
        EditorGameObject root = scene.Create("Root");
        EditorGameObject child = scene.Create("Child", root.StableId);
        EditorGameObject grandchild = scene.Create("Grandchild", child.StableId);
        EditorGameObject sibling = scene.Create("Sibling");

        AutomationRequestException descendant = Assert.Throws<AutomationRequestException>(() =>
            EditorAutomationAuthoringApi.EnsureAcyclicReparent(
                scene,
                root.StableId,
                grandchild.StableId));
        AutomationRequestException self = Assert.Throws<AutomationRequestException>(() =>
            EditorAutomationAuthoringApi.EnsureAcyclicReparent(
                scene,
                child.StableId,
                child.StableId));

        Assert.Contains("后代", descendant.Error.Message, StringComparison.Ordinal);
        Assert.Contains("自己", self.Error.Message, StringComparison.Ordinal);
        Assert.Null(root.ParentId);
        Assert.Equal(root.StableId, child.ParentId);
        Assert.Equal(child.StableId, grandchild.ParentId);
        EditorAutomationAuthoringApi.EnsureAcyclicReparent(
            scene,
            grandchild.StableId,
            sibling.StableId);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PixelEngine",
                "PrefabSecurityTests",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
