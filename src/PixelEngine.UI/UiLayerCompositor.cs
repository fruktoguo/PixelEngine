using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 将游戏大 UI 宿主注册为渲染管线的显式 UI present 层。
/// </summary>
public sealed class UiLayerCompositor : IUiPresentLayer, IDisposable
{
    private readonly GameUiHost _host;
    private readonly IDisposable _registration;
    private bool _disposed;

    private UiLayerCompositor(RenderPipeline pipeline, GameUiHost host)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _registration = pipeline.RegisterUiLayer(UiPresentLayerOrders.Game, this);
    }

    /// <summary>
    /// 使用游戏 UI 固定层级注册合成层。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <returns>已注册的合成层。</returns>
    public static UiLayerCompositor Attach(RenderPipeline pipeline, GameUiHost host)
    {
        return new UiLayerCompositor(pipeline, host);
    }

    /// <summary>
    /// present 层被调用的帧数。
    /// </summary>
    public long FrameIndex { get; private set; }

    /// <inheritdoc />
    public void Present(in UiPresentContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context.Gl);
        _host.Composite(in context);
        FrameIndex++;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _registration.Dispose();
        _disposed = true;
    }
}
