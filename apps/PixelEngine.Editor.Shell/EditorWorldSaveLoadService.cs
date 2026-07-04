using PixelEngine.Hosting;
using PixelEngine.Serialization;
using PixelEngine.World;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorWorldSaveLoadService(Engine engine, string saveRoot) : ISaveLoadService
{
    private const string ManifestFileName = "manifest.bin";
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly string _saveRoot = string.IsNullOrWhiteSpace(saveRoot)
        ? throw new ArgumentException("存档根目录不能为空。", nameof(saveRoot))
        : Path.GetFullPath(saveRoot);
    private readonly ManifestCodec _manifestCodec = new();

    public IReadOnlyList<SaveSlotInfo> ListSaveSlots()
    {
        if (!Directory.Exists(_saveRoot))
        {
            return [];
        }

        List<SaveSlotInfo> slots = [];
        foreach (string directory in Directory.EnumerateDirectories(_saveRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            slots.Add(ReadSlot(Path.GetFileName(directory), directory));
        }

        return slots;
    }

    public SaveLoadOperationResult Save(string slotId)
    {
        string normalized = NormalizeSlotId(slotId);
        string path = SlotPath(normalized);
        try
        {
            _engine.SaveWorldToDirectory(path);
            SaveSlotInfo slot = ReadSlot(normalized, path);
            return new SaveLoadOperationResult(true, $"已保存 {normalized}", slot, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new SaveLoadOperationResult(false, exception.Message, null, null);
        }
    }

    public SaveLoadOperationResult Load(string slotId)
    {
        string normalized = NormalizeSlotId(slotId);
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
        string manifestPath = Path.Combine(path, ManifestFileName);
        WorldManifest manifest = _manifestCodec.Decode(File.ReadAllBytes(manifestPath));
        FileInfo info = new(manifestPath);
        return new SaveSlotInfo(
            slotId,
            path,
            info.LastWriteTimeUtc,
            manifest.FormatVersion,
            manifest.WorldSeed,
            manifest.GameTimeTicks,
            manifest.ChunkIndex.Length);
    }

    private string SlotPath(string slotId)
    {
        return Path.Combine(_saveRoot, slotId);
    }

    private static string NormalizeSlotId(string slotId)
    {
        string trimmed = string.IsNullOrWhiteSpace(slotId)
            ? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
            : slotId.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '-');
        }

        return trimmed.Replace(' ', '-');
    }
}
