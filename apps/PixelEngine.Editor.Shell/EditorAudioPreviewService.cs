using PixelEngine.Audio;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 将 Project Window 音频试听接到当前项目会话的真实音频系统。
/// </summary>
internal sealed class EditorAudioPreviewService(
    AudioSystem audio,
    AudioClipCache clips) : IAudioPreviewService
{
    private const string AudioDirectory = "audio/";
    private readonly AudioSystem _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    private readonly AudioClipCache _clips = clips ?? throw new ArgumentNullException(nameof(clips));

    /// <inheritdoc />
    public bool TryPlayPreview(string assetPath)
    {
        if (!TryResolveWaveAssetId(assetPath, out string assetId))
        {
            return false;
        }

        try
        {
            if (!_clips.TryGetLoaded(assetId, out AudioClip? clip) || clip is null)
            {
                // 这是显式用户操作，不在稳态帧热路径；允许按需补载本次会话中新导入的 WAV。
                clip = _clips.LoadAsync(assetId).AsTask().GetAwaiter().GetResult();
            }

            return _audio.PlayUi(clip);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException)
        {
            return false;
        }
    }

    internal static bool TryResolveWaveAssetId(string? assetPath, out string assetId)
    {
        assetId = string.Empty;
        if (!EditorRootedBrowserPath.TryParse(assetPath, out EditorAssetPath path, out _) ||
            path.Root != EditorAssetRootKind.Content ||
            !path.RelativePath.StartsWith(AudioDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relativeToAudioRoot = path.RelativePath[AudioDirectory.Length..];
        if (string.IsNullOrWhiteSpace(relativeToAudioRoot) ||
            !string.Equals(Path.GetExtension(relativeToAudioRoot), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        assetId = relativeToAudioRoot;
        return true;
    }
}
