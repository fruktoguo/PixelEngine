using PixelEngine.Hosting;
using PixelEngine.Serialization;
using PixelEngine.World;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 编辑器内 Simulation world 存档的保存与加载。
/// </summary>
internal sealed class EditorWorldSaveLoadService(Engine engine, string saveRoot) : ISaveLoadService
{
    private const string ManifestFileName = "manifest.bin";
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    internal string SaveRoot { get; } = string.IsNullOrWhiteSpace(saveRoot)
        ? throw new ArgumentException("存档根目录不能为空。", nameof(saveRoot))
        : Path.GetFullPath(saveRoot);
    private readonly ManifestCodec _manifestCodec = new();

    public IReadOnlyList<SaveSlotInfo> ListSaveSlots()
    {
        return ListSaveSlots(SaveRoot, CancellationToken.None);
    }

    internal static SaveSlotInfo[] ListSaveSlots(
        string saveRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        string root = Path.GetFullPath(saveRoot);
        if (!Directory.Exists(root))
        {
            return [];
        }

        List<SaveSlotInfo> slots = [];
        ManifestCodec manifestCodec = new();
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveSlotPath.ValidateExistingDirectory(root, directory);
            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            slots.Add(ReadSlot(manifestCodec, Path.GetFileName(directory), directory));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return [.. slots];
    }

    public SaveLoadOperationResult Save(string slotId)
    {
        string normalized = SaveSlotPath.Normalize(slotId);
        string path = SlotPath(normalized);
        try
        {
            WorldSaveWriteResult write = _engine.SaveWorldToDirectory(path);
            SaveSlotInfo slot = ReadSlot(normalized, path);
            string message = write.CleanupPending
                ? $"已保存 {normalized}；旧存档 journal 清理待处理：{write.RetainedJournalPath} ({write.CleanupError})"
                : $"已保存 {normalized}";
            return new SaveLoadOperationResult(true, message, slot, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new SaveLoadOperationResult(false, exception.Message, null, null);
        }
    }

    public SaveLoadOperationResult Load(string slotId)
    {
        string normalized = SaveSlotPath.Normalize(slotId);
        string path = SlotPath(normalized);
        if (!File.Exists(Path.Combine(path, ManifestFileName)))
        {
            return new SaveLoadOperationResult(false, $"存档点不存在：{normalized}", null, null);
        }

        try
        {
            WorldLoadResult result = _engine.LoadWorldFromDirectory(path);
            SaveSlotInfo slot = ReadSlot(normalized, path);
            return new SaveLoadOperationResult(
                true,
                $"已加载 {normalized}；fallback {result.MaterialFallbackHitCount}",
                slot,
                result);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new SaveLoadOperationResult(false, exception.Message, null, null);
        }
    }

    private SaveSlotInfo ReadSlot(string slotId, string path)
    {
        return ReadSlot(_manifestCodec, slotId, path);
    }

    private static SaveSlotInfo ReadSlot(ManifestCodec manifestCodec, string slotId, string path)
    {
        string manifestPath = Path.Combine(path, ManifestFileName);
        FileInfo before = new(manifestPath);
        if (!before.Exists || before.Length > 16L * 1024 * 1024)
        {
            throw new InvalidDataException($"存档 manifest 大小无效：{manifestPath}");
        }

        WorldManifest manifest = manifestCodec.Decode(File.ReadAllBytes(manifestPath));
        FileInfo info = new(manifestPath);
        return info.Exists && info.Length == before.Length &&
            info.LastWriteTimeUtc == before.LastWriteTimeUtc
                ? new SaveSlotInfo(
                    slotId,
                    path,
                    info.LastWriteTimeUtc,
                    manifest.FormatVersion,
                    manifest.WorldSeed,
                    manifest.GameTimeTicks,
                    manifest.ChunkIndex.Length)
                : throw new IOException($"读取存档 manifest 时文件发生变化：{manifestPath}");
    }

    private string SlotPath(string slotId)
    {
        return SaveSlotPath.Resolve(SaveRoot, slotId);
    }
}
