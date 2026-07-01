using System.Text.Json;
using PixelEngine.Audio;

namespace PixelEngine.Hosting;

internal static class AudioCueManifest
{
    private const string FileName = "cues.json";

    /// <summary>
    /// 从 audio 目录读取 cue 句柄到音频资产路径的映射。
    /// </summary>
    /// <param name="audioRoot">audio 内容目录。</param>
    /// <returns>cue 句柄映射表。</returns>
    public static IReadOnlyDictionary<int, string> Load(string audioRoot)
    {
        string path = Path.Combine(audioRoot, FileName);
        if (!File.Exists(path))
        {
            return new Dictionary<int, string>();
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (!document.RootElement.TryGetProperty("cues", out JsonElement cues) ||
            cues.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("audio/cues.json 必须包含 cues 数组。");
        }

        Dictionary<int, string> result = [];
        foreach (JsonElement cue in cues.EnumerateArray())
        {
            int handle = cue.GetProperty("handle").GetInt32();
            string asset = cue.GetProperty("asset").GetString() ??
                throw new InvalidDataException("audio/cues.json cue 缺少 asset。");
            if (handle <= 0)
            {
                throw new InvalidDataException($"audio/cues.json cue handle 必须为正数：{handle}。");
            }

            asset = asset.Replace('\\', '/');
            if (Path.IsPathRooted(asset) ||
                asset.Split('/').Any(static part => part is "" or "." or ".."))
            {
                throw new InvalidDataException($"audio/cues.json cue asset 必须是 audio 目录内的相对路径：{asset}。");
            }

            if (!result.TryAdd(handle, asset))
            {
                throw new InvalidDataException($"audio/cues.json cue handle 重复：{handle}。");
            }
        }

        return result;
    }
}

internal sealed class AudioCueClipResolver(AudioClipCache cache, IReadOnlyDictionary<int, string> assetIds) : IAudioCueBufferResolver
{
    private readonly AudioClipCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IReadOnlyDictionary<int, string> _assetIds = assetIds ?? throw new ArgumentNullException(nameof(assetIds));

    /// <summary>
    /// 尝试把内容侧 cue 句柄解析为已加载的音频 buffer。
    /// </summary>
    /// <param name="cueHandle">内容侧 cue 句柄。</param>
    /// <param name="buffer">解析成功时返回后端 buffer 句柄。</param>
    /// <returns>cue 已加载时返回 true。</returns>
    public bool TryResolveBuffer(int cueHandle, out uint buffer)
    {
        if (_assetIds.TryGetValue(cueHandle, out string? assetId) &&
            _cache.TryGetLoaded(assetId, out AudioClip? clip) &&
            clip is not null)
        {
            buffer = clip.Buffer.Handle;
            return true;
        }

        buffer = 0;
        return false;
    }
}
