namespace PixelEngine.Serialization;

/// <summary>
/// 单步存档格式迁移器。每个 migrator 只负责 FromVersion 到 FromVersion + 1。
/// </summary>
public interface ISaveMigrator
{
    /// <summary>
    /// 当前迁移器支持的源版本。
    /// </summary>
    int FromVersion { get; }

    /// <summary>
    /// 原地迁移 payload，并通过 context 标记新版本。
    /// </summary>
    void Migrate(MigrationContext context);
}
