namespace PixelEngine.Simulation;

/// <summary>
/// dirty 标记应写入 chunk 的哪个相位缓冲区。
/// </summary>
internal enum DirtyPhaseTarget
{
    /// <summary>当前读相位（本帧 CA 扫描依据）。</summary>
    Current,
    /// <summary>工作写相位（双缓冲写入侧）。</summary>
    Working,
}
