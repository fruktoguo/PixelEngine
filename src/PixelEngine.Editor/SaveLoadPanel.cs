using Hexa.NET.ImGui;
using PixelEngine.Serialization;
using PixelEngine.World;

namespace PixelEngine.Editor;

/// <summary>Editor 世界存档 slot 的稳定 ID 与安全路径规范。</summary>
public static class SaveSlotPath
{
    private const int MaximumSlotIdLength = 96;
    private static readonly HashSet<string> WindowsReservedNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>把用户 slot 文本规范为单个、不可越界的文件名。</summary>
    /// <param name="slotId">用户输入；空白时生成 UTC 时间戳 ID。</param>
    /// <returns>长度有界的 canonical slot ID。</returns>
    public static string Normalize(string? slotId)
    {
        string source = string.IsNullOrWhiteSpace(slotId)
            ? DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture)
            : slotId.Trim();
        Span<char> buffer = source.Length <= MaximumSlotIdLength
            ? stackalloc char[source.Length]
            : stackalloc char[MaximumSlotIdLength];
        int written = 0;
        bool previousDash = false;
        for (int i = 0; i < source.Length && written < buffer.Length; i++)
        {
            char value = source[i];
            bool allowed = char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.';
            char normalized = allowed ? value : '-';
            if (normalized == '-' && previousDash)
            {
                continue;
            }

            buffer[written++] = normalized;
            previousDash = normalized == '-';
        }

        string candidate = new string(buffer[..written]).Trim('-', '.');
        if (candidate.Length == 0)
        {
            candidate = "slot";
        }

        string baseName = candidate.Split('.', 2)[0];
        return WindowsReservedNames.Contains(baseName) ? $"{candidate}-slot" : candidate;
    }

    /// <summary>解析根内 slot 目录，并拒绝任何既有 reparse-point 路径段。</summary>
    /// <param name="saveRoot">权威存档根。</param>
    /// <param name="slotId">已规范或原始 slot ID。</param>
    /// <returns>位于 saveRoot 直属子目录的 canonical path。</returns>
    public static string Resolve(string saveRoot, string? slotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveRoot));
        string target = Path.GetFullPath(Path.Combine(root, Normalize(slotId)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"存档 slot 越过 save root：{target}");
        }

        EnsureNoReparsePoint(root, target, comparison);
        return target;
    }

    /// <summary>验证 save root 下的既有目录不是 reparse point。</summary>
    /// <param name="saveRoot">权威存档根。</param>
    /// <param name="directory">待验证目录。</param>
    public static void ValidateExistingDirectory(string saveRoot, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveRoot));
        string target = Path.GetFullPath(directory);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"存档目录越过 save root：{target}");
        }

        EnsureNoReparsePoint(root, target, comparison);
    }

    private static void EnsureNoReparsePoint(
        string root,
        string target,
        StringComparison comparison)
    {
        string? current = target;
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"存档路径包含 reparse point：{current}");
            }

            if (string.Equals(current, root, comparison))
            {
                return;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("存档路径无法回溯到 save root。");
    }
}

/// <summary>
/// Editor 存读档服务。
/// </summary>
public interface ISaveLoadService
{
    /// <summary>
    /// 列出当前存档点。
    /// </summary>
    /// <returns>存档点快照。</returns>
    IReadOnlyList<SaveSlotInfo> ListSaveSlots();

    /// <summary>
    /// 保存到指定存档点。
    /// </summary>
    /// <param name="slotId">存档点 id。</param>
    /// <returns>保存结果。</returns>
    SaveLoadOperationResult Save(string slotId);

    /// <summary>
    /// 读取指定存档点。
    /// </summary>
    /// <param name="slotId">存档点 id。</param>
    /// <returns>读档结果。</returns>
    SaveLoadOperationResult Load(string slotId);
}

/// <summary>
/// 运行时向 WorldSaveLoadPanelService 提供一致快照上下文。
/// </summary>
public interface IWorldSaveLoadRuntime
{
    /// <summary>
    /// 创建帧边界存档上下文。
    /// </summary>
    /// <returns>存档上下文。</returns>
    WorldSaveContext CreateSaveContext();

    /// <summary>
    /// 创建读档上下文。
    /// </summary>
    /// <returns>读档上下文。</returns>
    WorldLoadContext CreateLoadContext();

    /// <summary>
    /// 当前全局状态快照来源。
    /// </summary>
    IWorldStateSnapshotSource StateSource { get; }

    /// <summary>
    /// 当前全局状态恢复目标。
    /// </summary>
    IWorldStateSnapshotSink StateSink { get; }
}

/// <summary>
/// 基于 plan/07 WorldSaveService 的 Editor 存读档服务。
/// </summary>
/// <param name="saveRoot">存档根目录。</param>
/// <param name="runtime">运行时快照上下文提供器。</param>
/// <param name="saveService">世界存读档服务。</param>
/// <param name="manifestCodec">manifest 编解码器。</param>
public sealed class WorldSaveLoadPanelService(
    string saveRoot,
    IWorldSaveLoadRuntime runtime,
    WorldSaveService? saveService = null,
    ManifestCodec? manifestCodec = null) : ISaveLoadService
{
    private const string ManifestFileName = "manifest.bin";
    private readonly string _saveRoot = string.IsNullOrWhiteSpace(saveRoot)
        ? throw new ArgumentException("存档根目录不能为空。", nameof(saveRoot))
        : Path.GetFullPath(saveRoot);
    private readonly IWorldSaveLoadRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly WorldSaveService _saveService = saveService ?? new WorldSaveService();
    private readonly ManifestCodec _manifestCodec = manifestCodec ?? new ManifestCodec();

    /// <inheritdoc />
    public IReadOnlyList<SaveSlotInfo> ListSaveSlots()
    {
        if (!Directory.Exists(_saveRoot))
        {
            return [];
        }

        List<SaveSlotInfo> slots = [];
        foreach (string directory in Directory.EnumerateDirectories(_saveRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            SaveSlotPath.ValidateExistingDirectory(_saveRoot, directory);
            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            WorldManifest manifest = _manifestCodec.Decode(File.ReadAllBytes(manifestPath));
            FileInfo info = new(manifestPath);
            slots.Add(new SaveSlotInfo(
                Path.GetFileName(directory),
                directory,
                info.LastWriteTimeUtc,
                manifest.FormatVersion,
                manifest.WorldSeed,
                manifest.GameTimeTicks,
                manifest.ChunkIndex.Length));
        }

        return slots;
    }

    /// <inheritdoc />
    public SaveLoadOperationResult Save(string slotId)
    {
        string normalized = SaveSlotPath.Normalize(slotId);
        string path = SlotPath(normalized);
        WorldSaveWriteResult write = _saveService.SaveAll(
            _runtime.CreateSaveContext(),
            _runtime.StateSource,
            path);
        SaveSlotInfo slot = ReadSlot(normalized, path);
        string message = write.CleanupPending
            ? $"已保存 {normalized}；旧存档 journal 清理待处理：{write.RetainedJournalPath} ({write.CleanupError})"
            : $"已保存 {normalized}";
        return new SaveLoadOperationResult(true, message, slot, null);
    }

    /// <inheritdoc />
    public SaveLoadOperationResult Load(string slotId)
    {
        string normalized = SaveSlotPath.Normalize(slotId);
        string path = SlotPath(normalized);
        if (!File.Exists(Path.Combine(path, ManifestFileName)))
        {
            return new SaveLoadOperationResult(false, $"存档点不存在：{normalized}", null, null);
        }

        WorldLoadResult result = _saveService.LoadAll(path, _runtime.CreateLoadContext(), _runtime.StateSink);
        SaveSlotInfo slot = ReadSlot(normalized, path);
        return new SaveLoadOperationResult(
            true,
            $"已加载 {normalized}；fallback {result.MaterialFallbackHitCount}",
            slot,
            result);
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
        return SaveSlotPath.Resolve(_saveRoot, slotId);
    }
}

/// <summary>
/// 存读档面板。
/// </summary>
/// <param name="service">存读档服务。</param>
[EditorUiSurface("editor.panel.save-load")]
public sealed class SaveLoadPanel(ISaveLoadService service) : IEditorPanel
{
    private readonly ISaveLoadService _service = service ?? throw new ArgumentNullException(nameof(service));
    private string _slotName = string.Empty;
    private bool _hasRefreshed;

    /// <inheritdoc />
    public string Title => EditorDockSpace.SaveLoadWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次存档点列表。
    /// </summary>
    public IReadOnlyList<SaveSlotInfo> LastSlots { get; private set; } = [];

    /// <summary>
    /// 最近一次状态文本。
    /// </summary>
    public string Status { get; private set; } = "就绪";

    /// <summary>
    /// 刷新存档点列表。
    /// </summary>
    /// <returns>存档点列表。</returns>
    public IReadOnlyList<SaveSlotInfo> Refresh()
    {
        LastSlots = _service.ListSaveSlots();
        _hasRefreshed = true;
        return LastSlots;
    }

    /// <summary>
    /// 保存指定存档点。
    /// </summary>
    /// <param name="slotId">存档点 id。</param>
    /// <returns>操作结果。</returns>
    public SaveLoadOperationResult Save(string slotId)
    {
        SaveLoadOperationResult result = _service.Save(slotId);
        Status = result.Message;
        _ = Refresh();
        return result;
    }

    /// <summary>
    /// 读取指定存档点。
    /// </summary>
    /// <param name="slotId">存档点 id。</param>
    /// <returns>操作结果。</returns>
    public SaveLoadOperationResult Load(string slotId)
    {
        SaveLoadOperationResult result = _service.Load(slotId);
        Status = result.Message;
        _ = Refresh();
        return result;
    }

    /// <inheritdoc />
    [EditorUiCommands(
        "panel.save-load.slots",
        "panel.save-load.save",
        "panel.save-load.refresh")]
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        _ = ImGui.InputText("存档点", ref _slotName, 96);
        ImGui.SameLine();
        if (ImGui.Button("保存"))
        {
            _ = Save(_slotName);
        }

        ImGui.SameLine();
        if (ImGui.Button("刷新"))
        {
            _ = Refresh();
        }

        IReadOnlyList<SaveSlotInfo> slots = _hasRefreshed ? LastSlots : Refresh();
        for (int i = 0; i < slots.Count; i++)
        {
            DrawSlot(slots[i]);
        }

        ImGui.TextUnformatted(Status);
        ImGui.End();
    }

    [EditorUiCommands("panel.save-load.load")]
    private void DrawSlot(SaveSlotInfo slot)
    {
        ImGui.TextUnformatted($"{slot.Id}  v{slot.FormatVersion}  chunks={slot.ChunkCount}  seed={slot.WorldSeed}  ticks={slot.GameTimeTicks}");
        ImGui.SameLine();
        if (ImGui.Button($"加载##{slot.Id}"))
        {
            _ = Load(slot.Id);
        }
    }
}
