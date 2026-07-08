namespace PixelEngine.Rendering;

/// <summary>
/// 为 UI present 层提供可选目标区域。用于外部宿主把游戏 UI 裁剪到内嵌 viewport。
/// </summary>
public interface IUiPresentTargetProvider
{
    /// <summary>
    /// 尝试取得当前帧 UI present 目标。
    /// </summary>
    /// <param name="target">目标区域，坐标以默认 framebuffer 左上角为原点。</param>
    /// <returns>存在有效目标则返回 true；返回 false 时使用渲染管线默认世界 viewport。</returns>
    bool TryGetPresentTarget(out UiPresentTarget target);
}
