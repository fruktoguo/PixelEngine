namespace PixelEngine.Audio;

/// <summary>
/// 从内容目录读取音频资产字节，asset id 使用相对路径或文件名。
/// </summary>
/// <param name="rootDirectory">音频资产根目录。</param>
public sealed class DirectoryAudioAssetStore(string rootDirectory) : IAudioAssetStore
{
    private readonly string _rootDirectory = NormalizeRoot(rootDirectory);

    /// <inheritdoc />
    public async ValueTask<byte[]> LoadBytesAsync(string assetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        string path = ResolveAssetPath(assetId);
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveAssetPath(string assetId)
    {
        if (Path.IsPathRooted(assetId))
        {
            throw new ArgumentException("音频 asset id 必须是相对路径。", nameof(assetId));
        }

        string fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, assetId));
        string relative = Path.GetRelativePath(_rootDirectory, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? throw new ArgumentException("音频 asset id 不能逃逸内容根目录。", nameof(assetId))
            : fullPath;
    }

    private static string NormalizeRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return Path.GetFullPath(rootDirectory);
    }
}
