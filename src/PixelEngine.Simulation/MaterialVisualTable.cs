namespace PixelEngine.Simulation;

/// <summary>
/// 渲染相位消费的材质视觉 SoA 表。字段全部来自 <see cref="MaterialDef" />，不会写回 sim cell。
/// </summary>
public sealed class MaterialVisualTable
{
    private readonly MaterialRenderStyle[] _renderStyle;
    private readonly MaterialLegendCategory[] _legendCategory;
    private readonly uint[] _edgeColorBgra;
    private readonly byte[] _opacity;
    private readonly uint[] _highlightColorBgra;
    private readonly bool[] _legendVisible;

    private MaterialVisualTable(
        MaterialRenderStyle[] renderStyle,
        MaterialLegendCategory[] legendCategory,
        uint[] edgeColorBgra,
        byte[] opacity,
        uint[] highlightColorBgra,
        bool[] legendVisible)
    {
        _renderStyle = renderStyle;
        _legendCategory = legendCategory;
        _edgeColorBgra = edgeColorBgra;
        _opacity = opacity;
        _highlightColorBgra = highlightColorBgra;
        _legendVisible = legendVisible;
    }

    /// <summary>
    /// material id 的可用数量。
    /// </summary>
    public int Count => _renderStyle.Length;

    /// <summary>
    /// 着色风格列。
    /// </summary>
    public ReadOnlySpan<MaterialRenderStyle> RenderStyle => _renderStyle;

    /// <summary>
    /// 图例分类列。
    /// </summary>
    public ReadOnlySpan<MaterialLegendCategory> LegendCategory => _legendCategory;

    /// <summary>
    /// 描边 / 裂纹 BGRA8 颜色列。
    /// </summary>
    public ReadOnlySpan<uint> EdgeColorBGRA => _edgeColorBgra;

    /// <summary>
    /// 渲染 alpha 列。
    /// </summary>
    public ReadOnlySpan<byte> Opacity => _opacity;

    /// <summary>
    /// 高亮 / emissive BGRA8 颜色列。
    /// </summary>
    public ReadOnlySpan<uint> HighlightColorBGRA => _highlightColorBgra;

    /// <summary>
    /// 图例默认可见性列。
    /// </summary>
    public ReadOnlySpan<bool> LegendVisible => _legendVisible;

    /// <summary>
    /// 从材质定义派生渲染相位只读视觉表。
    /// </summary>
    public static MaterialVisualTable FromDefinitions(ReadOnlySpan<MaterialDef> definitions)
    {
        int count = definitions.Length;
        MaterialRenderStyle[] renderStyle = new MaterialRenderStyle[count];
        MaterialLegendCategory[] legendCategory = new MaterialLegendCategory[count];
        uint[] edgeColorBgra = new uint[count];
        byte[] opacity = new byte[count];
        uint[] highlightColorBgra = new uint[count];
        bool[] legendVisible = new bool[count];

        for (int i = 0; i < count; i++)
        {
            MaterialDef def = definitions[i];
            renderStyle[i] = def.RenderStyle;
            legendCategory[i] = def.LegendCategory;
            edgeColorBgra[i] = def.EdgeColorBGRA;
            opacity[i] = def.Opacity;
            highlightColorBgra[i] = def.HighlightColorBGRA;
            legendVisible[i] = def.LegendVisible;
        }

        return new MaterialVisualTable(
            renderStyle,
            legendCategory,
            edgeColorBgra,
            opacity,
            highlightColorBgra,
            legendVisible);
    }
}
