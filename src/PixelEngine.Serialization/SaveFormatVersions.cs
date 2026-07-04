namespace PixelEngine.Serialization;

/// <summary>
/// PixelEngine 存档与 chunk blob 格式版本常量。
/// </summary>
public static class SaveFormatVersions
{
    /// <summary>
    /// 当前 chunk blob 格式版本。
    /// </summary>
    public const int ChunkBlob = 2;

    /// <summary>
    /// 当前 world manifest 格式版本。
    /// </summary>
    public const int WorldManifest = 3;

    /// <summary>
    /// 引入 Damage 平面前的 world manifest 版本，结构与 v3 相同但 chunk blob 为 v1。
    /// </summary>
    public const int WorldManifestBeforeDamageLane = 2;
}
