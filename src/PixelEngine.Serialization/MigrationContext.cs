namespace PixelEngine.Serialization;

/// <summary>
/// 存档迁移过程中的可替换 payload 上下文。
/// </summary>
public sealed class MigrationContext
{
    /// <summary>
    /// 创建迁移上下文。
    /// </summary>
    public MigrationContext(ReadOnlySpan<byte> payload, int formatVersion)
    {
        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion), "格式版本必须为正。");
        }

        Payload = payload.ToArray();
        FormatVersion = formatVersion;
    }

    /// <summary>
    /// 当前 payload 格式版本。
    /// </summary>
    public int FormatVersion { get; private set; }

    /// <summary>
    /// 当前 payload 字节。
    /// </summary>
    public byte[] Payload { get; private set; }

    /// <summary>
    /// 替换 payload 并标记迁移后的版本。
    /// </summary>
    public void ReplacePayload(ReadOnlySpan<byte> payload, int formatVersion)
    {
        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion), "格式版本必须为正。");
        }

        Payload = payload.ToArray();
        FormatVersion = formatVersion;
    }
}
