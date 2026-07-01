namespace PixelEngine.Rendering;

/// <summary>
/// PBO 上传路径选择。
/// </summary>
public enum PboUploadMode
{
    /// <summary>
    /// 每次上传前使用 <c>glBufferData</c> orphan 并临时映射。默认路径，兼容 GL 3.3 / ES 3.0。
    /// </summary>
    OrphanMap,

    /// <summary>
    /// 使用 <c>glBufferStorage</c> persistent/coherent mapping 与 fence 同步。仅在 <see cref="GlCapabilities.HasBufferStorage"/> 可用时启用。
    /// </summary>
    PersistentMapped,
}
