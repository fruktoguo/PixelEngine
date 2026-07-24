namespace PixelEngine.Rendering;

/// <summary>Overlay 命令在世界渲染图中的组合位置。</summary>
public enum OverlayCompositionLayer : byte
{
    /// <summary>在透明 cell 后方绘制。</summary>
    Background,

    /// <summary>在 cell 上方、光照合成前绘制。</summary>
    WorldDecoration,

    /// <summary>在后处理后绘制；用于玩家、准星与调试图形。</summary>
    Foreground,
}
