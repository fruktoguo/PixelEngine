using System.Text.Json;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Editor asset manifest 增量刷新与损坏恢复契约。
/// </summary>
public sealed class EditorAssetManifestRefreshTests
{
    /// <summary>
    /// 验证扫描结果完全一致时不会重写 manifest，也不会改变 stable asset id。
    /// </summary>
    [Fact]
    public void RefreshDoesNotRewriteUnchangedManifest()
    {
        using TempDirectory temp = new();
        string contentRoot = CreateContent(temp.Path, out string assetPath);
        File.WriteAllText(assetPath, "unchanged");
        EditorAssetManifestStore store = new(temp.Path, contentRoot);
        EditorAssetRecord initial = Assert.Single(store.Refresh());
        File.SetLastWriteTimeUtc(store.ManifestPath, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        DateTime marker = File.GetLastWriteTimeUtc(store.ManifestPath);
        string before = File.ReadAllText(store.ManifestPath);

        EditorAssetRecord refreshed = Assert.Single(store.Refresh());

        Assert.Equal(marker, File.GetLastWriteTimeUtc(store.ManifestPath));
        Assert.Equal(before, File.ReadAllText(store.ManifestPath));
        Assert.Equal(initial.Id, refreshed.Id);
        Assert.Empty(store.LastDiagnostic);
        Assert.Null(store.LastCorruptManifestPath);
    }

    /// <summary>
    /// 验证磁盘资产真实变化时才重写 manifest，并继续复用原 stable asset id。
    /// </summary>
    [Fact]
    public void RefreshWritesRealChangesAndPreservesStableAssetId()
    {
        using TempDirectory temp = new();
        string contentRoot = CreateContent(temp.Path, out string assetPath);
        File.WriteAllText(assetPath, "before");
        EditorAssetManifestStore store = new(temp.Path, contentRoot);
        EditorAssetRecord initial = Assert.Single(store.Refresh());
        File.SetLastWriteTimeUtc(store.ManifestPath, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        DateTime marker = File.GetLastWriteTimeUtc(store.ManifestPath);
        File.WriteAllText(assetPath, "after-with-different-size");

        EditorAssetRecord refreshed = Assert.Single(store.Refresh());

        Assert.NotEqual(marker, File.GetLastWriteTimeUtc(store.ManifestPath));
        Assert.Equal(initial.Id, refreshed.Id);
        Assert.Equal(new FileInfo(assetPath).Length, refreshed.SizeBytes);
        Assert.Empty(store.LastDiagnostic);
    }

    /// <summary>
    /// 验证 watcher rename 信息缺失时，完整扫描仍能通过唯一文件签名延续 stable asset id。
    /// </summary>
    [Fact]
    public void RefreshPreservesStableAssetIdAcrossUnambiguousRenameWithoutWatcherHint()
    {
        using TempDirectory temp = new();
        string contentRoot = CreateContent(temp.Path, out string sourcePath);
        File.WriteAllText(sourcePath, "rename-without-watcher-hint");
        File.SetLastWriteTimeUtc(sourcePath, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        EditorAssetManifestStore store = new(temp.Path, contentRoot);
        EditorAssetRecord initial = Assert.Single(store.Refresh());
        string movedPath = Path.Combine(contentRoot, "textures", "renamed.png");

        File.Move(sourcePath, movedPath);
        EditorAssetRecord refreshed = Assert.Single(store.Refresh());

        Assert.Equal(initial.Id, refreshed.Id);
        Assert.Equal("textures/renamed.png", refreshed.LogicalPath);
        Assert.Equal(initial.AssetType, refreshed.AssetType);
        Assert.Equal(initial.SizeBytes, refreshed.SizeBytes);
        Assert.Equal(initial.LastModifiedUtc, refreshed.LastModifiedUtc);
        EditorAssetPathRewrite rewrite = Assert.Single(store.LastRefreshPathRewrites);
        Assert.Equal(initial.Id, rewrite.AssetId);
        Assert.Equal("textures/sample.png", rewrite.OldPath);
        Assert.Equal("textures/renamed.png", rewrite.NewPath);
        Assert.Equal(EditorAssetType.Texture, rewrite.AssetType);
        Assert.False(store.LastRefreshHadAmbiguousIdentityMatches);
    }

    /// <summary>
    /// 验证多个消失记录与多个新增文件共享同一签名时，不猜测 rename 配对也不错误复用旧 id。
    /// </summary>
    [Fact]
    public void RefreshAllocatesNewIdsWhenRenameIdentitySignatureIsAmbiguous()
    {
        using TempDirectory temp = new();
        string contentRoot = CreateContent(temp.Path, out string firstPath);
        string secondPath = Path.Combine(contentRoot, "textures", "second.png");
        File.WriteAllText(firstPath, "same-signature");
        File.WriteAllText(secondPath, "same-signature");
        DateTime sharedTimestamp = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(firstPath, sharedTimestamp);
        File.SetLastWriteTimeUtc(secondPath, sharedTimestamp);
        EditorAssetManifestStore store = new(temp.Path, contentRoot);
        EditorAssetRecord[] initial = [.. store.Refresh()];
        HashSet<string> oldIds = [.. initial.Select(static asset => asset.Id)];

        File.Move(firstPath, Path.Combine(contentRoot, "textures", "renamed-a.png"));
        File.Move(secondPath, Path.Combine(contentRoot, "textures", "renamed-b.png"));
        EditorAssetRecord[] refreshed = [.. store.Refresh()];

        Assert.Equal(2, refreshed.Length);
        Assert.Equal(2, refreshed.Select(static asset => asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(refreshed, asset => oldIds.Contains(asset.Id));
        Assert.Contains(refreshed, asset => asset.LogicalPath == "textures/renamed-a.png");
        Assert.Contains(refreshed, asset => asset.LogicalPath == "textures/renamed-b.png");
        Assert.Empty(store.LastRefreshPathRewrites);
        Assert.True(store.LastRefreshHadAmbiguousIdentityMatches);
    }

    /// <summary>
    /// 验证损坏 manifest 会先隔离原文件、暴露诊断，再从磁盘扫描重建。
    /// </summary>
    [Fact]
    public void RefreshQuarantinesCorruptManifestAndRebuildsWithDiagnostic()
    {
        using TempDirectory temp = new();
        string contentRoot = CreateContent(temp.Path, out string assetPath);
        File.WriteAllText(assetPath, "asset");
        EditorAssetManifestStore store = new(temp.Path, contentRoot);
        _ = store.Refresh();
        const string corruptJson = "{ this is not valid json";
        File.WriteAllText(store.ManifestPath, corruptJson);

        EditorAssetRecord recovered = Assert.Single(store.Refresh());

        string corruptPath = Assert.IsType<string>(store.LastCorruptManifestPath);
        Assert.True(File.Exists(corruptPath));
        Assert.Equal(corruptJson, File.ReadAllText(corruptPath));
        Assert.True(File.Exists(store.ManifestPath));
        Assert.Contains("已损坏", store.LastDiagnostic, StringComparison.Ordinal);
        Assert.Contains("已从 content 目录重建", store.LastDiagnostic, StringComparison.Ordinal);
        Assert.StartsWith("asset_", recovered.Id, StringComparison.Ordinal);
        using JsonDocument rebuilt = JsonDocument.Parse(File.ReadAllText(store.ManifestPath));
        Assert.Equal(EditorAssetManifestStore.CurrentFormatVersion, rebuilt.RootElement.GetProperty("formatVersion").GetInt32());
        _ = Assert.Single(rebuilt.RootElement.GetProperty("assets").EnumerateArray());

        EditorAssetRecord reloaded = Assert.Single(store.Refresh());
        Assert.Equal(recovered.Id, reloaded.Id);
        Assert.Empty(store.LastDiagnostic);
        Assert.Null(store.LastCorruptManifestPath);
    }

    /// <summary>
    /// 验证单路径增量 upsert/remove，以及已知 move 保留 stable asset id。
    /// </summary>
    [Fact]
    public void IncrementalRecordApisUpdateOnlyKnownLogicalPathsAndPreserveIdAcrossMove()
    {
        using TempDirectory temp = new();
        string contentRoot = CreateContent(temp.Path, out string sourcePath);
        File.WriteAllText(sourcePath, "source");
        EditorAssetManifestStore store = new(temp.Path, contentRoot);
        EditorAssetRecord source = Assert.Single(store.Refresh());
        string addedPath = Path.Combine(contentRoot, "textures", "added.png");
        File.WriteAllText(addedPath, "added");

        Assert.True(store.TryUpsertAssetFromDisk("textures/added.png", out EditorAssetRecord added));
        Assert.NotEqual(source.Id, added.Id);
        Assert.False(store.TryUpsertAssetFromDisk("textures/missing.png", out _));

        string movedPath = Path.Combine(contentRoot, "textures", "moved.png");
        File.Move(sourcePath, movedPath);
        Assert.True(store.TryMoveAssetRecordFromDisk(
            "textures/sample.png",
            "textures/moved.png",
            out EditorAssetRecord moved));
        Assert.Equal(source.Id, moved.Id);
        Assert.Equal("textures/moved.png", moved.LogicalPath);

        File.Delete(addedPath);
        Assert.True(store.RemoveAssetRecord("textures/added.png"));
        Assert.False(store.RemoveAssetRecord("textures/added.png"));
        EditorAssetRecord remaining = Assert.Single(store.Refresh());
        Assert.Equal(source.Id, remaining.Id);
        Assert.Equal("textures/moved.png", remaining.LogicalPath);
    }

    private static string CreateContent(string projectRoot, out string assetPath)
    {
        string contentRoot = Path.Combine(projectRoot, "content");
        string textureRoot = Path.Combine(contentRoot, "textures");
        _ = Directory.CreateDirectory(textureRoot);
        assetPath = Path.Combine(textureRoot, "sample.png");
        return contentRoot;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pixelengine-asset-manifest-refresh-" + Guid.NewGuid().ToString("N"));
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
