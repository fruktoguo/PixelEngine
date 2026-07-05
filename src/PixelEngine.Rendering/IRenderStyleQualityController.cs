namespace PixelEngine.Rendering;

/// <summary>
/// CPU RenderStyle 着色质量控制器，由 Hosting 过载策略按质量档动态切换。
/// </summary>
public interface IRenderStyleQualityController
{
    /// <summary>
    /// 当前 RenderStyle 着色质量档。
    /// </summary>
    RenderBufferStyleLevel RenderStyleLevel { get; }

    /// <summary>
    /// 设置 RenderStyle 着色质量档；实现必须在稳态帧内零托管堆分配。
    /// </summary>
    /// <param name="level">目标质量档。</param>
    void SetRenderStyleLevel(RenderBufferStyleLevel level);
}
