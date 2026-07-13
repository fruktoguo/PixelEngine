using System.Text.Json.Serialization;

namespace PixelEngine.Rendering;

/// <summary>
/// 独立 Player 的平台窗口模式。Editor Game View 的面板最大化不使用也不改写此值。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlayerWindowMode>))]
public enum PlayerWindowMode
{
    /// <summary>可调整大小的普通窗口；Width/Height 是初始客户区与 presentation 尺寸。</summary>
    Windowed,

    /// <summary>保留系统标题栏与任务栏的最大化窗口。</summary>
    MaximizedWindow,

    /// <summary>覆盖当前显示器工作表面的无边框全屏窗口；不声明 exclusive fullscreen。</summary>
    BorderlessFullscreen,
}
