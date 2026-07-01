using System.Text.Json;
using PixelEngine.Content;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

#pragma warning disable IDE0290, IDE0031

/// <summary>
/// 材质/反应编辑器消费的内容服务。
/// </summary>
public interface IMaterialReactionContentService
{
    /// <summary>
    /// 加载当前 materials.json / reactions.json 编辑文档。
    /// </summary>
    MaterialReactionEditorDocument Load();

    /// <summary>
    /// 校验并预览编辑文档的展开结果。
    /// </summary>
    MaterialReactionPreviewResult Preview(MaterialReactionEditorDocument document);

    /// <summary>
    /// 写回 JSON，并触发运行时材质/反应稳定热重载。
    /// </summary>
    MaterialReactionApplyResult Apply(MaterialReactionEditorDocument document);
}

/// <summary>
/// 材质资产重载通知。
/// </summary>
public interface IMaterialAssetReloadSink
{
    /// <summary>
    /// 重新加载变更材质关联的纹理与音效资产。
    /// </summary>
    void ReloadMaterialAssets(IReadOnlyList<MaterialAssetReloadRequest> requests);
}

/// <summary>
/// 单个材质资产重载请求。
/// </summary>
public readonly record struct MaterialAssetReloadRequest(
    string MaterialName,
    ushort RuntimeId,
    bool TextureChanged,
    bool AudioChanged);

/// <summary>
/// 反应展开预览结果。
/// </summary>
public readonly record struct MaterialReactionPreviewResult(
    int MaterialCount,
    int SourceReactionCount,
    int PackedReactionCount,
    string Message);

/// <summary>
/// 材质/反应热重载结果。
/// </summary>
public sealed class MaterialReactionApplyResult
{
    /// <summary>
    /// 创建材质/反应热重载结果。
    /// </summary>
    public MaterialReactionApplyResult(
        MaterialReloadResult materialReload,
        int liveGridFallbackReplacementCount,
        int packedReactionCount,
        IReadOnlyList<MaterialAssetReloadRequest> assetReloads)
    {
        MaterialReload = materialReload;
        LiveGridFallbackReplacementCount = liveGridFallbackReplacementCount;
        PackedReactionCount = packedReactionCount;
        AssetReloads = assetReloads;
        DiagnosticMessage = $"重载后用 fallback 替换了 {liveGridFallbackReplacementCount} 个被删材质的活 cell";
    }

    /// <summary>
    /// MaterialTable 稳定热重载结果。
    /// </summary>
    public MaterialReloadResult MaterialReload { get; }

    /// <summary>
    /// live 网格实际替换到 fallback 的 cell 数量。
    /// </summary>
    public int LiveGridFallbackReplacementCount { get; }

    /// <summary>
    /// packed reaction 数量。
    /// </summary>
    public int PackedReactionCount { get; }

    /// <summary>
    /// 纹理/音效资产重载请求。
    /// </summary>
    public IReadOnlyList<MaterialAssetReloadRequest> AssetReloads { get; }

    /// <summary>
    /// 面板与控制台显示的诊断消息。
    /// </summary>
    public string DiagnosticMessage { get; }
}

/// <summary>
/// 基于本地 materials.json / reactions.json 的完整热重载服务。
/// </summary>
public sealed class FileMaterialReactionContentService : IMaterialReactionContentService
{
    private readonly string _materialsPath;
    private readonly string _reactionsPath;
    private readonly MaterialTable _runtimeMaterials;
    private readonly IChunkSource _residentChunks;
    private readonly ushort _fallbackMaterialId;
    private readonly Action<MaterialHotTable>? _applyMaterialHotTable;
    private readonly Action<ReactionTable> _applyReactions;
    private readonly IMaterialAssetReloadSink? _assetReloadSink;
    private readonly EngineCounters? _counters;

    /// <summary>
    /// 创建文件内容服务。
    /// </summary>
    public FileMaterialReactionContentService(
        string materialsPath,
        string reactionsPath,
        MaterialTable runtimeMaterials,
        IChunkSource residentChunks,
        ushort fallbackMaterialId,
        Action<ReactionTable> applyReactions,
        Action<MaterialHotTable>? applyMaterialHotTable = null,
        IMaterialAssetReloadSink? assetReloadSink = null,
        EngineCounters? counters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reactionsPath);
        ArgumentNullException.ThrowIfNull(runtimeMaterials);
        ArgumentNullException.ThrowIfNull(residentChunks);
        ArgumentNullException.ThrowIfNull(applyReactions);
        _ = runtimeMaterials.GetName(fallbackMaterialId);
        _materialsPath = materialsPath;
        _reactionsPath = reactionsPath;
        _runtimeMaterials = runtimeMaterials;
        _residentChunks = residentChunks;
        _fallbackMaterialId = fallbackMaterialId;
        _applyMaterialHotTable = applyMaterialHotTable;
        _applyReactions = applyReactions;
        _assetReloadSink = assetReloadSink;
        _counters = counters;
    }

    /// <inheritdoc />
    public MaterialReactionEditorDocument Load()
    {
        MaterialDocumentJson materials = ReadMaterialDocument();
        ReactionDocumentJson reactions = ReadReactionDocument();
        return MaterialReactionEditorDocument.FromContent(materials, reactions, _runtimeMaterials);
    }

    /// <inheritdoc />
    public MaterialReactionPreviewResult Preview(MaterialReactionEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        MaterialDocumentJson materials = document.ToMaterialDocument();
        ReactionDocumentJson reactions = document.ToReactionDocument();
        MaterialContentStableReloadResult stable = MaterialContentLoader.BuildStableReload(materials, reactions, _runtimeMaterials);
        int packedCount = CountPackedReactions(stable.Definitions);
        int sourceCount = reactions.Reactions?.Length ?? 0;
        return new MaterialReactionPreviewResult(
            stable.Definitions.Length,
            sourceCount,
            packedCount,
            $"源规则 {sourceCount} 条，展开后 packed reaction {packedCount} 条");
    }

    /// <inheritdoc />
    public MaterialReactionApplyResult Apply(MaterialReactionEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        MaterialDocumentJson materials = document.ToMaterialDocument();
        ReactionDocumentJson reactions = document.ToReactionDocument();
        MaterialContentStableReloadResult stable = MaterialContentLoader.BuildStableReload(materials, reactions, _runtimeMaterials);
        IReadOnlyList<MaterialAssetReloadRequest> assetReloads = DetectAssetReloads(stable.Definitions);
        int[] liveCounts = MaterialLiveGridRemapper.CountResidentCellsByMaterial(_residentChunks, _runtimeMaterials.Count);
        MaterialReloadResult reload = _runtimeMaterials.ReloadStable(stable.Definitions, liveCounts, _fallbackMaterialId);
        int replaced = MaterialLiveGridRemapper.ReplaceResidentMaterials(_residentChunks, reload.TombstoneIds, _fallbackMaterialId);
        _applyMaterialHotTable?.Invoke(_runtimeMaterials.Hot);
        _applyReactions(stable.Reactions);
        WriteMaterialDocument(materials);
        WriteReactionDocument(reactions);
        _assetReloadSink?.ReloadMaterialAssets(assetReloads);
        if (_counters is not null)
        {
            _counters.MaterialRemapFallbackHits = replaced;
        }

        return new MaterialReactionApplyResult(
            reload,
            replaced,
            CountPackedReactions(stable.Definitions),
            assetReloads);
    }

    private MaterialDocumentJson ReadMaterialDocument()
    {
        string json = File.ReadAllText(_materialsPath);
        try
        {
            if (JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.MaterialDocumentJson) is { } document)
            {
                return document;
            }
        }
        catch (JsonException)
        {
        }

        MaterialJson[]? array = JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.MaterialJsonArray);
        return array is null
            ? throw new JsonException("materials.json 为空或格式无效。")
            : new MaterialDocumentJson { Materials = array };
    }

    private ReactionDocumentJson ReadReactionDocument()
    {
        string json = File.ReadAllText(_reactionsPath);
        try
        {
            if (JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.ReactionDocumentJson) is { } document)
            {
                return document;
            }
        }
        catch (JsonException)
        {
        }

        ReactionJson[]? array = JsonSerializer.Deserialize(json, MaterialContentJsonContext.Default.ReactionJsonArray);
        return array is null
            ? throw new JsonException("reactions.json 为空或格式无效。")
            : new ReactionDocumentJson { Reactions = array };
    }

    private void WriteMaterialDocument(MaterialDocumentJson document)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_materialsPath))!);
        string json = JsonSerializer.Serialize(document, MaterialContentJsonContext.Default.MaterialDocumentJson);
        File.WriteAllText(_materialsPath, json);
    }

    private void WriteReactionDocument(ReactionDocumentJson document)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_reactionsPath))!);
        string json = JsonSerializer.Serialize(document, MaterialContentJsonContext.Default.ReactionDocumentJson);
        File.WriteAllText(_reactionsPath, json);
    }

    private static int CountPackedReactions(ReadOnlySpan<MaterialDef> definitions)
    {
        int count = 0;
        for (int i = 0; i < definitions.Length; i++)
        {
            count += definitions[i].ReactionCount;
        }

        return count;
    }

    private IReadOnlyList<MaterialAssetReloadRequest> DetectAssetReloads(ReadOnlySpan<MaterialDef> newDefinitions)
    {
        List<MaterialAssetReloadRequest> requests = [];
        for (int i = 0; i < newDefinitions.Length; i++)
        {
            MaterialDef next = newDefinitions[i];
            if (!_runtimeMaterials.TryGetId(next.Name, out ushort id))
            {
                continue;
            }

            ref readonly MaterialDef previous = ref _runtimeMaterials.Get(id);
            bool textureChanged = previous.TextureId != next.TextureId;
            bool audioChanged = previous.AudioCues != next.AudioCues;
            if (textureChanged || audioChanged)
            {
                requests.Add(new MaterialAssetReloadRequest(next.Name, id, textureChanged, audioChanged));
            }
        }

        return requests;
    }
}

#pragma warning restore IDE0290, IDE0031
