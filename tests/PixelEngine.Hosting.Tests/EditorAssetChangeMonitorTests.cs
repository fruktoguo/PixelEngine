using System.Diagnostics;
using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 双根 Editor 资产增量监视与事件合并测试。
/// </summary>
public sealed class EditorAssetChangeMonitorTests
{
    /// <summary>
    /// 验证 Created 吸收 Changed，而同一 drain 内临时创建后删除不会产生失效。
    /// </summary>
    [Fact]
    public void AccumulatorCoalescesCreatedChangesAndEphemeralDelete()
    {
        EditorAssetChangeAccumulator accumulator = new();

        accumulator.RecordCreated(EditorAssetRootKind.Content, @"textures\sand.png");
        accumulator.RecordChanged(EditorAssetRootKind.Content, "textures/sand.png");
        accumulator.RecordChanged(EditorAssetRootKind.Content, "textures/sand.png");
        accumulator.RecordCreated(EditorAssetRootKind.ScriptSource, "Temporary.cs");
        accumulator.RecordDeleted(EditorAssetRootKind.ScriptSource, "Temporary.cs");

        EditorAssetChangeBatch batch = accumulator.Drain();

        EditorAssetChange change = Assert.Single(batch.Changes);
        Assert.Equal(EditorAssetChangeKind.Created, change.Kind);
        Assert.Equal(EditorAssetRootKind.Content, change.Path.Root);
        Assert.Equal("textures/sand.png", change.Path.RelativePath);
        Assert.Null(change.OldPath);
        Assert.False(batch.RequiresFullRescan);
    }

    /// <summary>
    /// 验证连续 rename 合成为原路径到最终路径，rename 后删除退化为原资产删除。
    /// </summary>
    [Fact]
    public void AccumulatorComposesRenameChainsAndRenameDelete()
    {
        EditorAssetChangeAccumulator accumulator = new();
        accumulator.RecordChanged(EditorAssetRootKind.Content, "scenes/one.scene");
        accumulator.RecordRenamed(EditorAssetRootKind.Content, "scenes/one.scene", "scenes/two.scene");
        accumulator.RecordRenamed(EditorAssetRootKind.Content, "scenes/two.scene", "scenes/final.scene");
        accumulator.RecordChanged(EditorAssetRootKind.Content, "scenes/final.scene");
        accumulator.RecordCreated(EditorAssetRootKind.ScriptSource, "NewBehaviour.cs");
        accumulator.RecordRenamed(EditorAssetRootKind.ScriptSource, "NewBehaviour.cs", "PlayerBehaviour.cs");

        EditorAssetChangeBatch first = accumulator.Drain();

        Assert.Collection(
            first.Changes,
            scene =>
            {
                Assert.Equal(EditorAssetChangeKind.Renamed, scene.Kind);
                Assert.Equal("scenes/one.scene", scene.OldPath?.RelativePath);
                Assert.Equal("scenes/final.scene", scene.Path.RelativePath);
            },
            script =>
            {
                Assert.Equal(EditorAssetChangeKind.Renamed, script.Kind);
                Assert.Equal(EditorAssetRootKind.ScriptSource, script.Path.Root);
                Assert.Equal("NewBehaviour.cs", script.OldPath?.RelativePath);
                Assert.Equal("PlayerBehaviour.cs", script.Path.RelativePath);
            });

        accumulator.RecordRenamed(EditorAssetRootKind.Content, "prefabs/old.prefab", "prefabs/new.prefab");
        accumulator.RecordDeleted(EditorAssetRootKind.Content, "prefabs/new.prefab");
        EditorAssetChange deleted = Assert.Single(accumulator.Drain().Changes);
        Assert.Equal(EditorAssetChangeKind.Deleted, deleted.Kind);
        Assert.Equal("prefabs/old.prefab", deleted.Path.RelativePath);
    }

    /// <summary>
    /// 验证单根队列溢出只要求该根 full rescan，另一根的增量仍可信。
    /// </summary>
    [Fact]
    public void QueueOverflowInvalidatesOnlyAffectedRoot()
    {
        EditorAssetChangeAccumulator accumulator = new(maxPendingChangesPerRoot: 2);
        accumulator.RecordChanged(EditorAssetRootKind.ScriptSource, "Player.cs");
        accumulator.RecordChanged(EditorAssetRootKind.Content, "a.json");
        accumulator.RecordChanged(EditorAssetRootKind.Content, "b.json");
        accumulator.RecordChanged(EditorAssetRootKind.Content, "c.json");
        accumulator.RecordChanged(EditorAssetRootKind.Content, "ignored-after-overflow.json");

        EditorAssetChangeBatch batch = accumulator.Drain();

        Assert.Equal([EditorAssetRootKind.Content], batch.FullRescanRoots);
        EditorAssetChange script = Assert.Single(batch.Changes);
        Assert.Equal(EditorAssetRootKind.ScriptSource, script.Path.Root);
        Assert.Equal("Player.cs", script.Path.RelativePath);
        Assert.True(batch.RequiresFullRescan);
    }

    /// <summary>
    /// 验证 full path 只能规范化到所属根内，并统一使用正斜杠。
    /// </summary>
    [Fact]
    public void EventPathNormalizationRejectsRootEscape()
    {
        using TempAssetRoots roots = new();
        string inside = Path.Combine(roots.ContentRoot, "textures", "sand.png");
        string outside = Path.Combine(roots.Root, "outside.png");

        bool normalized = EditorAssetChangeMonitor.TryNormalizeEventPath(
            roots.ContentRoot,
            inside,
            out string relativePath);
        bool escaped = EditorAssetChangeMonitor.TryNormalizeEventPath(
            roots.ContentRoot,
            outside,
            out string escapedPath);

        Assert.True(normalized);
        Assert.Equal("textures/sand.png", relativePath);
        Assert.False(escaped);
        Assert.Equal(string.Empty, escapedPath);
        _ = Assert.Throws<InvalidOperationException>(() =>
            EditorAssetChangeMonitor.NormalizeRelativePath("../outside.png"));
        Assert.True(EditorAssetChangeMonitor.IsInternalMetadataPath(".pixelengine"));
        Assert.True(EditorAssetChangeMonitor.IsInternalMetadataPath(".PIXELENGINE/archive/file.bin"));
        Assert.False(EditorAssetChangeMonitor.IsInternalMetadataPath("assets/.pixelengine.png"));
    }

    /// <summary>
    /// 验证多线程 watcher 风格写入可安全合并并由一次 drain 原子取走。
    /// </summary>
    [Fact]
    public void AccumulatorSupportsConcurrentRecordAndAtomicDrain()
    {
        EditorAssetChangeAccumulator accumulator = new();

        _ = Parallel.For(0, 128, index =>
        {
            EditorAssetRootKind root = index % 2 == 0
                ? EditorAssetRootKind.Content
                : EditorAssetRootKind.ScriptSource;
            accumulator.RecordChanged(root, $"shared/{index % 8}.asset");
        });

        EditorAssetChangeBatch first = accumulator.Drain();
        EditorAssetChangeBatch second = accumulator.Drain();

        Assert.Equal(8, first.Changes.Length);
        Assert.Equal(8, first.Changes.Select(static change => change.Path).Distinct().Count());
        Assert.True(second.IsEmpty);
    }

    /// <summary>
    /// 验证真实 FileSystemWatcher 会把两个物理根映射成各自的 logical root。
    /// </summary>
    [Fact]
    public void RealWatcherPublishesChangesFromBothRoots()
    {
        using TempAssetRoots roots = new();
        using EditorAssetChangeMonitor monitor = new(roots.ContentRoot, roots.ScriptRoot);
        string contentFile = Path.Combine(roots.ContentRoot, "materials.json");
        string scriptFile = Path.Combine(roots.ScriptRoot, "Player.cs");

        File.WriteAllText(contentFile, "{}\n");
        File.WriteAllText(scriptFile, "public sealed class Player {}\n");

        List<EditorAssetChange> observed = DrainUntil(
            monitor,
            changes => ContainsPath(changes, EditorAssetRootKind.Content, "materials.json") &&
                ContainsPath(changes, EditorAssetRootKind.ScriptSource, "Player.cs"));

        Assert.Contains(observed, change =>
            change.Path.Root == EditorAssetRootKind.Content &&
            change.Path.RelativePath == "materials.json");
        Assert.Contains(observed, change =>
            change.Path.Root == EditorAssetRootKind.ScriptSource &&
            change.Path.RelativePath == "Player.cs");
    }

    /// <summary>验证内部 manifest/archive 写入不会反向进入用户资产失效队列。</summary>
    [Fact]
    public void RealWatcherIgnoresPixelEngineMetadataTrees()
    {
        using TempAssetRoots roots = new();
        using EditorAssetChangeMonitor monitor = new(roots.ContentRoot, roots.ScriptRoot);
        string contentMetadata = Path.Combine(roots.ContentRoot, ".pixelengine", "automation", "archive.bin");
        string scriptMetadata = Path.Combine(roots.ScriptRoot, ".pixelengine", "script-assets.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(contentMetadata)!);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(scriptMetadata)!);
        File.WriteAllBytes(contentMetadata, [1, 2, 3]);
        File.WriteAllText(scriptMetadata, "{}\n");

        Thread.Sleep(250);
        EditorAssetChangeBatch batch = monitor.Drain();

        Assert.DoesNotContain(batch.Changes, static change =>
            EditorAssetChangeMonitor.IsInternalMetadataPath(change.Path.RelativePath));
        Assert.Empty(batch.FullRescanRoots);
    }

    /// <summary>
    /// 验证 Dispose 幂等，且释放后不允许继续 drain。
    /// </summary>
    [Fact]
    public void DisposeIsIdempotentAndDrainRejectsDisposedMonitor()
    {
        using TempAssetRoots roots = new();
        EditorAssetChangeMonitor monitor = new(roots.ContentRoot, roots.ScriptRoot);

        monitor.Dispose();
        monitor.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(monitor.Drain);
    }

    private static List<EditorAssetChange> DrainUntil(
        EditorAssetChangeMonitor monitor,
        Func<IReadOnlyList<EditorAssetChange>, bool> completed)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<EditorAssetChange> observed = [];
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            EditorAssetChangeBatch batch = monitor.Drain();
            Assert.False(batch.RequiresFullRescan);
            observed.AddRange(batch.Changes);
            if (completed(observed))
            {
                return observed;
            }

            Thread.Sleep(20);
        }

        return observed;
    }

    private static bool ContainsPath(
        IReadOnlyList<EditorAssetChange> changes,
        EditorAssetRootKind root,
        string relativePath)
    {
        for (int i = 0; i < changes.Count; i++)
        {
            if (changes[i].Path.Root == root &&
                string.Equals(changes[i].Path.RelativePath, relativePath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TempAssetRoots : IDisposable
    {
        public TempAssetRoots()
        {
            Root = Path.Combine(Path.GetTempPath(), "pixelengine-asset-monitor-" + Guid.NewGuid().ToString("N"));
            ContentRoot = Path.Combine(Root, "content");
            ScriptRoot = Path.Combine(Root, "scripts");
            _ = Directory.CreateDirectory(ContentRoot);
            _ = Directory.CreateDirectory(ScriptRoot);
        }

        public string Root { get; }

        public string ContentRoot { get; }

        public string ScriptRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
