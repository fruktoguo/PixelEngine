namespace PixelEngine.Editor;

/// <summary>
/// 编辑器世界画刷工具。
/// </summary>
public enum EditorBrushTool : byte
{
    /// <summary>
    /// 写入目标材质。
    /// </summary>
    Paint,

    /// <summary>
    /// 挖除为 Empty。
    /// </summary>
    Dig,

    /// <summary>
    /// 擦除 cell 内容与运行时标记。
    /// </summary>
    Erase,

    /// <summary>
    /// 写入温度场。
    /// </summary>
    Temperature,
}
