using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 统一判定 Demo 旧版脚本 GUI 是否仍需作为无 Web Canvas 时的降级界面。
/// </summary>
internal static class LegacyGuiFallback
{
    /// <summary>
    /// 当前场景没有物化 primary Canvas 时才保留旧 GUI。
    /// </summary>
    public static bool IsRequired(IGameUiService gameUi)
    {
        ArgumentNullException.ThrowIfNull(gameUi);
        return gameUi.PrimaryCanvas.Value == 0;
    }
}
