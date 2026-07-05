using PixelEngine.Rendering;

namespace PixelEngine.Hosting;

/// <summary>
/// 独立编辑器壳注入 Hosting 的中性扩展点；实现方可在相位 [10] present 前绘制 GUI，
/// 但 Hosting 不依赖任何 <c>PixelEngine.Editor</c> 类型。
/// </summary>
public interface IEditorHostExtension
{
    /// <summary>
    /// 在渲染管线创建后绑定扩展。实现应通过 <see cref="RenderPipeline.RegisterUiLayer(int, IUiPresentLayer)" /> 注册确定性 UI 层。
    /// </summary>
    /// <param name="engine">当前引擎门面。</param>
    /// <param name="window">已存在的渲染窗口；所有权仍归创建方。</param>
    /// <param name="pipeline">当前渲染管线。</param>
    /// <returns>解除绑定所需的资源；无资源可返回 null。</returns>
    IDisposable? Attach(Engine engine, RenderWindow window, RenderPipeline pipeline);
}
