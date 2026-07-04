namespace PixelEngine.UI;

/// <summary>
/// UI 文档来源类型。
/// </summary>
public enum UiDocumentSourceKind : byte
{
    /// <summary>
    /// content/ui 下的资产。
    /// </summary>
    Asset = 0,

    /// <summary>
    /// 运行时生成的虚拟文档。
    /// </summary>
    Runtime = 1,
}
