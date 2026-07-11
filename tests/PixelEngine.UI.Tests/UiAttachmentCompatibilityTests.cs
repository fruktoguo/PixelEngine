using PixelEngine.Gui;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// UI present surface 公开绑定 API 的 CLR 兼容契约测试。
/// </summary>
public sealed class UiAttachmentCompatibilityTests
{
    /// <summary>
    /// 验证 GuiRenderBridge 保留所有旧窗口绑定签名，并新增显式 surface 签名。
    /// </summary>
    [Fact]
    public void GuiRenderBridgeRetainsLegacyWindowAttachmentOverloads()
    {
        System.Reflection.MethodInfo[] methods = PublicStaticMethods(
            typeof(GuiRenderBridge),
            nameof(GuiRenderBridge.AttachIfEnabled));

        AssertLegacyMethod(
            methods,
            typeof(RenderPipeline),
            typeof(GuiApp),
            typeof(IScriptRuntime));
        AssertLegacyMethod(
            methods,
            typeof(RenderPipeline),
            typeof(GuiApp),
            typeof(IScriptRuntime),
            typeof(Action<IGuiDrawContext>));
        AssertLegacyMethod(
            methods,
            typeof(RenderPipeline),
            typeof(GuiApp),
            typeof(IScriptRuntime),
            typeof(Action<IGuiDrawContext>),
            typeof(Action<UiPresentTarget>));
        _ = FindMethod(
            methods,
            typeof(RenderPipeline),
            typeof(UiPresentSurface),
            typeof(GuiApp),
            typeof(IScriptRuntime),
            typeof(Action<IGuiDrawContext>),
            typeof(Action<UiPresentTarget>));
    }

    /// <summary>
    /// 验证 UiLayerCompositor 保留旧窗口绑定签名，并新增显式 surface 签名。
    /// </summary>
    [Fact]
    public void UiLayerCompositorRetainsLegacyWindowAttachmentOverloads()
    {
        System.Reflection.MethodInfo[] methods = PublicStaticMethods(
            typeof(UiLayerCompositor),
            nameof(UiLayerCompositor.Attach));

        AssertLegacyMethod(methods, typeof(RenderPipeline), typeof(GameUiHost));
        AssertLegacyMethod(
            methods,
            typeof(RenderPipeline),
            typeof(GameUiHost),
            typeof(IUiPresentTargetProvider));
        _ = FindMethod(
            methods,
            typeof(RenderPipeline),
            typeof(UiPresentSurface),
            typeof(GameUiHost));
        _ = FindMethod(
            methods,
            typeof(RenderPipeline),
            typeof(UiPresentSurface),
            typeof(GameUiHost),
            typeof(IUiPresentTargetProvider));
    }

    private static System.Reflection.MethodInfo[] PublicStaticMethods(Type type, string name)
    {
        return
        [
            .. type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(method => method.Name == name),
        ];
    }

    private static void AssertLegacyMethod(
        System.Reflection.MethodInfo[] methods,
        params Type[] parameterTypes)
    {
        System.Reflection.MethodInfo method = FindMethod(methods, parameterTypes);
        Assert.Null(method.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
    }

    private static System.Reflection.MethodInfo FindMethod(
        System.Reflection.MethodInfo[] methods,
        params Type[] parameterTypes)
    {
        return Assert.Single(methods, method =>
        {
            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == parameterTypes.Length &&
                parameters.Select(static parameter => parameter.ParameterType).SequenceEqual(parameterTypes);
        });
    }
}
