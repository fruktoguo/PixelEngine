using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal static class EditorAssetAutomationReferenceScanner
{
    private const int MaximumReferenceDocuments = 8192;

    internal static EditorAssetDeletePreflight Scan(
        EditorAssetAutomationReferencePlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReferenceDocumentFile[] before = CaptureReferenceDocuments(
            plan.ReferenceDocumentRoot,
            cancellationToken);
        List<string> locations = [];
        int referencedDocuments = 0;
        bool sharedRoot = PathsEqual(plan.ContentRoot, plan.ReferenceDocumentRoot);
        for (int i = 0; i < before.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReferenceDocumentFile file = before[i];
            string logicalPath = Path.GetRelativePath(plan.ReferenceDocumentRoot, file.FullPath)
                .Replace('\\', '/');
            if (sharedRoot &&
                string.Equals(logicalPath, plan.Asset.LogicalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(file.FullPath);
            EnsureUnchanged(file);
            int locationCountBefore = locations.Count;
            EditorAssetManifestStore.CollectReferenceLocations(
                EditorSceneModel.FromDocument(document),
                logicalPath,
                plan.Asset,
                locations);
            if (locations.Count != locationCountBefore)
            {
                referencedDocuments++;
            }
        }

        int activeBefore = locations.Count;
        EditorAssetManifestStore.CollectReferenceLocations(
            EditorSceneModel.FromDocument(plan.ActiveScene),
            "active scene",
            plan.Asset,
            locations);
        bool activeSceneHasReferences = locations.Count != activeBefore;
        ReferenceDocumentFile[] after = CaptureReferenceDocuments(
            plan.ReferenceDocumentRoot,
            cancellationToken);
        _ = before.SequenceEqual(after)
            ? true
            : throw new IOException("资产引用文档集合在扫描期间发生变化。");

        return new EditorAssetDeletePreflight(
            plan.Asset,
            locations.Count,
            referencedDocuments,
            activeSceneHasReferences,
            locations);
    }

    private static ReferenceDocumentFile[] CaptureReferenceDocuments(
        string root,
        CancellationToken cancellationToken)
    {
        string[] paths = EditorAssetFileTraversal.EnumerateFiles(
            root,
            EditorAssetFileTraversalSelection.ReferenceDocuments,
            MaximumReferenceDocuments,
            "Automation 资产引用扫描");
        ReferenceDocumentFile[] files = new ReferenceDocumentFile[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info = new(paths[i]);
            info.Refresh();
            if (!info.Exists)
            {
                throw new IOException($"资产引用文档在扫描前已消失：{paths[i]}");
            }

            files[i] = new ReferenceDocumentFile(
                Path.GetFullPath(paths[i]),
                info.Length,
                info.LastWriteTimeUtc);
        }

        return files;
    }

    private static void EnsureUnchanged(ReferenceDocumentFile expected)
    {
        FileInfo current = new(expected.FullPath);
        current.Refresh();
        if (!current.Exists ||
            current.Length != expected.Length ||
            current.LastWriteTimeUtc != expected.LastWriteTimeUtc)
        {
            throw new IOException($"资产引用文档在解析期间发生变化：{expected.FullPath}");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private readonly record struct ReferenceDocumentFile(
        string FullPath,
        long Length,
        DateTime LastWriteTimeUtc);
}
