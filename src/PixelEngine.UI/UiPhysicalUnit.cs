namespace PixelEngine.UI;

/// <summary>
/// <see cref="UiScaleMode.ConstantPhysicalSize" /> 使用的物理单位。
/// </summary>
public enum UiPhysicalUnit : byte
{
    /// <summary>厘米。</summary>
    Centimeters = 0,

    /// <summary>毫米。</summary>
    Millimeters = 1,

    /// <summary>英寸。</summary>
    Inches = 2,

    /// <summary>点；每英寸 72 点。</summary>
    Points = 3,

    /// <summary>派卡；每英寸 6 派卡。</summary>
    Picas = 4,
}
