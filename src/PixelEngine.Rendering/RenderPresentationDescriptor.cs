namespace PixelEngine.Rendering;

/// <summary>
/// Rendering 在帧边界提交的游戏呈现描述；固定 world surface 只会被等比写入
/// <see cref="WorldViewport" />，不会因 presentation 尺寸改变而重建相机或世界像素。
/// </summary>
/// <param name="PresentationWidth">完整游戏呈现纹理宽度。</param>
/// <param name="PresentationHeight">完整游戏呈现纹理高度。</param>
/// <param name="WorldViewport">固定内部世界在呈现纹理中的居中区域。</param>
/// <param name="DisplayMetricsRevision">产生本描述的显示器度量版本。</param>
/// <param name="Revision">单调递增的游戏呈现版本。</param>
public readonly record struct RenderPresentationDescriptor(
    int PresentationWidth,
    int PresentationHeight,
    PresentationViewport WorldViewport,
    long DisplayMetricsRevision,
    long Revision)
{
    /// <summary>
    /// 为与 presentation 同尺寸的内部世界创建初始描述。
    /// </summary>
    /// <param name="worldWidth">固定内部世界宽度。</param>
    /// <param name="worldHeight">固定内部世界高度。</param>
    /// <returns>revision 为 0 的初始描述。</returns>
    public static RenderPresentationDescriptor CreateInitial(int worldWidth, int worldHeight)
    {
        PresentationViewport viewport = PresentationViewport.Fit(
            worldWidth,
            worldHeight,
            worldWidth,
            worldHeight);
        return new RenderPresentationDescriptor(
            worldWidth,
            worldHeight,
            viewport,
            DisplayMetricsRevision: 0,
            Revision: 0);
    }

    /// <summary>
    /// 校验 presentation、world viewport 与 revision 的一致性。
    /// </summary>
    /// <param name="worldWidth">管线当前固定内部世界宽度。</param>
    /// <param name="worldHeight">管线当前固定内部世界高度。</param>
    public void Validate(int worldWidth, int worldHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PresentationWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PresentationHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldHeight);
        if (DisplayMetricsRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DisplayMetricsRevision), "显示器度量 revision 不能为负数。");
        }

        if (Revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Revision), "Presentation revision 不能为负数。");
        }

        if (WorldViewport.SourceWidth != worldWidth ||
            WorldViewport.SourceHeight != worldHeight ||
            WorldViewport.TargetWidth != PresentationWidth ||
            WorldViewport.TargetHeight != PresentationHeight ||
            WorldViewport.Width <= 0 ||
            WorldViewport.Height <= 0 ||
            WorldViewport.X < 0 ||
            WorldViewport.Y < 0 ||
            WorldViewport.X + WorldViewport.Width > PresentationWidth ||
            WorldViewport.Y + WorldViewport.Height > PresentationHeight)
        {
            throw new ArgumentException("WorldViewport 与固定 world / presentation 尺寸不一致。", nameof(WorldViewport));
        }
    }
}
