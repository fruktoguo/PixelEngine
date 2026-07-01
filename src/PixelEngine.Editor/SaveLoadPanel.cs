using Hexa.NET.ImGui;
using PixelEngine.Serialization;
using PixelEngine.World;

namespace PixelEngine.Editor;

/// <summary>
/// 存档点摘要。
/// </summary>
/// <param name="Id">存档点 id。</param>
/// <param name="Path">存档目录绝对路径。</param>
/// <param name="TimestampUtc">manifest 修改时间。</param>
/// <param name="FormatVersion">manifest 格式版本。</param>
/// <param name="WorldSeed">世界种子。</param>
/// <param name="GameTimeTicks">游戏时间 tick。</param>
/// <param name="ChunkCount">chunk 数量。</param>
public readonly record struct SaveSlotInfo(
    string Id,
    string Path,
    DateTimeOffset TimestampUtc,
    int FormatVersion,
    ulong WorldSeed,
    long GameTimeTicks,
    int ChunkCount);

/// <summary>
/// 存读档操作结果。
/// </summary>
/// <param name="Success">操作是否成功。</param>
/// <param name="Message">诊断文本。</param>
/// <param name="Slot">相关存档点。</param>
/// <param name="LoadResult">读档结果；保存操作时为 null。</param>
public readonly record struct SaveLoadOperationResult(
    bool Success,
    string Message,
    SaveSlotInfo? Slot,
    WorldLoadResult? LoadResult);

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
        string normalized = NormalizeSlotId(slotId);
        string path = SlotPath(normalized);
        _saveService.SaveAll(_runtime.CreateSaveContext(), _runtime.StateSource, path);
        SaveSlotInfo slot = ReadSlot(normalized, path);
        return new SaveLoadOperationResult(true, $"已保存 {normalized}", slot, null);
    }

    /// <inheritdoc />
    public SaveLoadOperationResult Load(string slotId)
    {
        string normalized = NormalizeSlotId(slotId);
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

/// <summary>
/// 存读档面板。
/// </summary>
/// <param name="service">存读档服务。</param>
public sealed class SaveLoadPanel(ISaveLoadService service) : IEditorPanel
{
    private readonly ISaveLoadService _service = service ?? throw new ArgumentNullException(nameof(service));
    private string _slotName = string.Empty;

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

        IReadOnlyList<SaveSlotInfo> slots = LastSlots.Count == 0 ? Refresh() : LastSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            DrawSlot(slots[i]);
        }

        ImGui.TextUnformatted(Status);
        ImGui.End();
    }

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
