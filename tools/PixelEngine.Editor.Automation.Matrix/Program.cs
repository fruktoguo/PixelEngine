using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Editor.Shell;

return await MatrixApplication.RunAsync(args);

internal static class MatrixApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["--output" or "--check", var path] || string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine(
                "usage: PixelEngine.Editor.Automation.Matrix --output|--check <capability-matrix.json>");
            return 2;
        }

        string fullPath = Path.GetFullPath(path);
        byte[] canonical = await CaptureCanonicalMatrixAsync().ConfigureAwait(false);
        if (string.Equals(args[0], "--check", StringComparison.Ordinal))
        {
            if (!File.Exists(fullPath) || !File.ReadAllBytes(fullPath).AsSpan().SequenceEqual(canonical))
            {
                Console.Error.WriteLine(
                    $"Capability matrix 已漂移；运行 --output 更新：{fullPath}");
                return 1;
            }

            Console.WriteLine(fullPath);
            return 0;
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Capability matrix 输出路径缺少父目录。");
        }

        _ = Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, canonical);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        Console.WriteLine(fullPath);
        return 0;
    }

    private static async Task<byte[]> CaptureCanonicalMatrixAsync()
    {
        string artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "PixelEngine",
            "AutomationMatrix",
            Guid.NewGuid().ToString("N"));
        try
        {
            EditorShellApp app = EditorShellApp.CreateForTests();
            await using AutomationArtifactStore artifacts = new(new AutomationArtifactStoreOptions
            {
                RootPath = artifactRoot,
            });
            using EditorAutomationAuthoringApi api = new(app, artifacts, []);
            using AutomationMainThreadScheduler scheduler = new(
                api.CreateRegistrations(),
                new AutomationRevisionStore(),
                new NoopUndoSink(),
                new NoopTransactionParticipant(),
                uiCommands: EditorUiCommandCatalog.CreateRegistrations());
            AutomationCapabilityMatrixSnapshot matrix = scheduler.CaptureCapabilityMatrix();
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                matrix,
                AutomationJsonContext.Default.AutomationCapabilityMatrixSnapshot);
            byte[] canonical = new byte[json.Length + 1];
            json.CopyTo(canonical, 0);
            canonical[^1] = (byte)'\n';
            return canonical;
        }
        finally
        {
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
    }

    private sealed class NoopUndoSink : IAutomationUndoSink
    {
        public void RecordExecuted(IAutomationUndoAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            throw new InvalidOperationException("Capability matrix 导出不得执行 semantic write。");
        }
    }

    private sealed class NoopTransactionParticipant : IAutomationTransactionParticipant
    {
        public object CaptureState()
        {
            throw new InvalidOperationException("Capability matrix 导出不得启动 transaction。");
        }

        public void RestoreState(object state)
        {
            ArgumentNullException.ThrowIfNull(state);
            throw new InvalidOperationException("Capability matrix 导出不得恢复 transaction state。");
        }
    }
}
