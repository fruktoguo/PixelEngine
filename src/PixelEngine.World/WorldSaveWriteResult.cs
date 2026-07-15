namespace PixelEngine.World;

/// <summary>整世界存档目录成功发布后的持久化结果。</summary>
public readonly record struct WorldSaveWriteResult
{
    /// <summary>创建存档发布结果。</summary>
    /// <param name="savePath">已提交的 canonical 存档目录。</param>
    /// <param name="retainedJournalPath">清理受阻时保留的恢复 journal；否则为 null。</param>
    /// <param name="cleanupError">保留 journal 的清理错误；否则为 null。</param>
    public WorldSaveWriteResult(
        string savePath,
        string? retainedJournalPath,
        string? cleanupError)
    {
        SavePath = string.IsNullOrWhiteSpace(savePath)
            ? throw new ArgumentException("存档路径不能为空。", nameof(savePath))
            : Path.GetFullPath(savePath);
        if ((retainedJournalPath is null) != (cleanupError is null))
        {
            throw new ArgumentException("retained journal 与 cleanup error 必须同时存在或同时为空。");
        }

        RetainedJournalPath = retainedJournalPath is null
            ? null
            : Path.GetFullPath(retainedJournalPath);
        CleanupError = cleanupError;
    }

    /// <summary>已提交的 canonical 存档目录。</summary>
    public string SavePath { get; }

    /// <summary>清理受阻时保留的恢复 journal；正常清理完成时为 null。</summary>
    public string? RetainedJournalPath { get; }

    /// <summary>保留 journal 的清理错误；正常清理完成时为 null。</summary>
    public string? CleanupError { get; }

    /// <summary>是否存在需要后续人工或维护任务清理的 journal。</summary>
    public bool CleanupPending => RetainedJournalPath is not null;
}
