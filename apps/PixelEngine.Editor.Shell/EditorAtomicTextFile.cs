using System.Text;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 通过同目录临时文件与原子替换持久化 Editor 文本数据，失败时保留既有文件。
/// </summary>
internal static class EditorAtomicTextFile
{
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
