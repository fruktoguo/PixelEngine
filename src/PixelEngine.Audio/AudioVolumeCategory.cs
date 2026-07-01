namespace PixelEngine.Audio;

/// <summary>
/// 音频混音类别，用于运行时分类音量控制。
/// </summary>
public enum AudioVolumeCategory
{
    /// <summary>
    /// 世界内一次性音效与材质事件音效。
    /// </summary>
    Sfx,

    /// <summary>
    /// UI 与非定位反馈音效。
    /// </summary>
    Ui,

    /// <summary>
    /// 材质区域 ambient loop。
    /// </summary>
    Ambient,
}
