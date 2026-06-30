namespace PixelEngine.Serialization;

/// <summary>
/// 逐版本应用存档迁移器的迁移链。
/// </summary>
public sealed class MigrationChain
{
    private readonly Dictionary<int, ISaveMigrator> _migrators;

    /// <summary>
    /// 创建迁移链。
    /// </summary>
    public MigrationChain(int targetVersion, IEnumerable<ISaveMigrator> migrators)
    {
        ArgumentNullException.ThrowIfNull(migrators);
        if (targetVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetVersion), "目标版本必须为正。");
        }

        TargetVersion = targetVersion;
        _migrators = new Dictionary<int, ISaveMigrator>();
        foreach (ISaveMigrator migrator in migrators)
        {
            ArgumentNullException.ThrowIfNull(migrator);
            if (migrator.FromVersion <= 0 || migrator.FromVersion >= targetVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(migrators), "迁移器源版本必须位于 1..targetVersion-1。");
            }

            if (!_migrators.TryAdd(migrator.FromVersion, migrator))
            {
                throw new ArgumentException($"重复的迁移源版本：{migrator.FromVersion}。", nameof(migrators));
            }
        }
    }

    /// <summary>
    /// 目标格式版本。
    /// </summary>
    public int TargetVersion { get; }

    /// <summary>
    /// 将字节 payload 从指定版本逐级升级到目标版本。
    /// </summary>
    public byte[] Upgrade(ReadOnlySpan<byte> payload, int fromVersion)
    {
        if (fromVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), "源版本必须为正。");
        }

        if (fromVersion > TargetVersion)
        {
            throw new InvalidDataException($"存档版本 {fromVersion} 高于当前支持版本 {TargetVersion}。");
        }

        MigrationContext context = new(payload, fromVersion);
        while (context.FormatVersion < TargetVersion)
        {
            int current = context.FormatVersion;
            if (!_migrators.TryGetValue(current, out ISaveMigrator? migrator))
            {
                throw new InvalidDataException($"缺少 v{current} 到 v{current + 1} 的存档迁移器。");
            }

            migrator.Migrate(context);
            if (context.FormatVersion != current + 1)
            {
                throw new InvalidDataException($"迁移器 v{current} 必须逐级升级到 v{current + 1}。");
            }
        }

        return context.Payload;
    }

    /// <summary>
    /// 从输入流读取 payload，升级后写入输出流。
    /// </summary>
    public void Upgrade(Stream input, Stream output, int fromVersion)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        using MemoryStream buffer = new();
        input.CopyTo(buffer);
        byte[] upgraded = Upgrade(buffer.ToArray(), fromVersion);
        output.Write(upgraded);
    }
}
