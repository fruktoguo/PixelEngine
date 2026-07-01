namespace PixelEngine.Hosting;

/// <summary>
/// 场景初始世界来源。
/// </summary>
public enum SceneSourceKind
{
    /// <summary>
    /// 空场景，不主动构建世界内容。
    /// </summary>
    Empty,

    /// <summary>
    /// 从 plan/07 存档目录加载。
    /// </summary>
    SaveDirectory,

    /// <summary>
    /// 从编辑器序列化的 .scene 文件加载。
    /// </summary>
    SceneFile,

    /// <summary>
    /// 由宿主注册的程序化生成器构建。
    /// </summary>
    Procedural,
}
