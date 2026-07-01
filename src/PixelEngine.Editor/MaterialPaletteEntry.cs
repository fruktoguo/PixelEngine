namespace PixelEngine.Editor;

/// <summary>
/// 材质调色板条目。
/// </summary>
/// <param name="Id">运行时材质 id。</param>
/// <param name="Name">稳定材质名。</param>
/// <param name="BaseColorBgra">BGRA8 基色，供 UI 色块/缩略图使用。</param>
public readonly record struct MaterialPaletteEntry(ushort Id, string Name, uint BaseColorBgra);
