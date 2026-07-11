namespace PixelEngine.Rendering;

/// <summary>
/// 为 UI present 层提供可选目标区域。目标坐标属于该层注册时选择的 present surface，
/// 不得混用宿主窗口、Editor 面板与 runtime viewport 坐标。
/// </summary>
public interface IUiPresentTargetProvider
{
    /// <summary>
    /// 尝试取得当前帧 UI present 目标。
    /// </summary>
    /// <param name="target">目标区域，坐标以当前 present 表面左上角为原点。</param>
    /// <returns>存在有效目标则返回 true；返回 false 时使用渲染管线默认世界 viewport。</returns>
    bool TryGetPresentTarget(out UiPresentTarget target);
}
