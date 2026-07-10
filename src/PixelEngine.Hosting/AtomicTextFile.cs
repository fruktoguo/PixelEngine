using System.Text;

namespace PixelEngine.Hosting;

/// <summary>
/// 通过同目录临时文件与原子替换持久化文本，避免失败时截断既有配置或场景文件。
/// </summary>
internal static class AtomicTextFile
{
    /// <summary>
    /// 将文本刷新到同目录临时文件后原子替换目标，提交失败时保留旧文件。
    /// </summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="contents">要写入的完整文本。</param>
    public static void WriteAllText(string path, string contents)
    {
        WriteAllText(path, contents, static (temporaryPath, destinationPath) =>
            File.Move(temporaryPath, destinationPath, overwrite: true));
    }

    internal static void WriteAllText(
        string path,
        string contents,
        Action<string, string> commit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(commit);

        string destinationPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{destinationPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(contents);
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            commit(temporaryPath, destinationPath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
