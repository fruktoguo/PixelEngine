using PixelEngine.Rendering.Compute;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderPipelineContractTests
{
    [Fact]
    public void RenderPipelineSettingsValidateRejectsInvalidValues()
    {
        RenderPipelineSettings settings = new() { DitherStrength = -1f };

        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);

        settings = new RenderPipelineSettings { Gamma = 0f };
        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);

        settings = new RenderPipelineSettings { CrtScanlineStrength = float.NaN };
        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);

        settings = new RenderPipelineSettings { RadianceCascades = RadianceCascadeSettings.Default with { BaseRayCount = 63 } };
        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);

        settings = new RenderPipelineSettings { PreferComputeSharpBackend = true };
        settings.Validate();
        Assert.True(settings.PreferComputeSharpBackend);
    }

    [Fact]
    public void RenderViewportTextureRequiresValidHandleAndDimensions()
    {
        RenderViewportTexture texture = new(handle: 7, width: 64, height: 32);

        Assert.True(texture.IsValid);
        Assert.Equal(7u, texture.Handle);
        Assert.Equal(64, texture.Width);
        Assert.Equal(32, texture.Height);
        Assert.False(default(RenderViewportTexture).IsValid);
        AssertThrows<ArgumentOutOfRangeException>(() => new RenderViewportTexture(0, 64, 32));
        AssertThrows<ArgumentOutOfRangeException>(() => new RenderViewportTexture(7, 0, 32));
        AssertThrows<ArgumentOutOfRangeException>(() => new RenderViewportTexture(7, 64, 0));
    }

    [Fact]
    public void PresentationViewportFitsInternalCanvasIntoFramebuffer()
    {
        PresentationViewport wide = PresentationViewport.Fit(720, 480, 1920, 1080);

        Assert.Equal(150, wide.X);
        Assert.Equal(0, wide.Y);
        Assert.Equal(1620, wide.Width);
        Assert.Equal(1080, wide.Height);
        Assert.Equal(720, wide.SourceWidth);
        Assert.Equal(480, wide.SourceHeight);
        Assert.Equal(1920, wide.TargetWidth);
        Assert.Equal(1080, wide.TargetHeight);
        Assert.Equal((0f, 0f), wide.MapFramebufferToSource(150f, 0f));
        Assert.Equal((720f, 480f), wide.MapFramebufferToSource(1770f, 1080f));

        PresentationViewport tall = PresentationViewport.Fit(720, 480, 600, 1200);
        Assert.Equal(0, tall.X);
        Assert.Equal(400, tall.Y);
        Assert.Equal(600, tall.Width);
        Assert.Equal(400, tall.Height);
    }

    [Fact]
    public void RenderPipelineSourceDocumentsRequiredOrderingAndHooks()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));

        Assert.Contains("_worldBlit.Render", source, StringComparison.Ordinal);
        Assert.Contains("_overlay.Render", source, StringComparison.Ordinal);
        Assert.Contains("_composite.Render", source, StringComparison.Ordinal);
        Assert.Contains("_bloom.Render", source, StringComparison.Ordinal);
        Assert.Contains("_computeBloom!.Render", source, StringComparison.Ordinal);
        Assert.Contains("ComputeCapabilityGate.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("RenderPipelineSettings? settings", source, StringComparison.Ordinal);
        Assert.Contains("Settings.PreferComputeSharpBackend", source, StringComparison.Ordinal);
        Assert.DoesNotContain("preferComputeSharp: false", source, StringComparison.Ordinal);
        Assert.Contains("_dither.Render", source, StringComparison.Ordinal);
        Assert.Contains("_gamma.Render", source, StringComparison.Ordinal);
        Assert.Contains("_crt.Render", source, StringComparison.Ordinal);
        Assert.Contains("BeforePresentUi?.Invoke", source, StringComparison.Ordinal);
        Assert.Contains("RegisterUiLayer", source, StringComparison.Ordinal);
        Assert.Contains("PresentUiLayers(new UiPresentContext", source, StringComparison.Ordinal);
        Assert.Contains("CompareUiLayers", source, StringComparison.Ordinal);
        Assert.Contains("PrepareUiState", source, StringComparison.Ordinal);
        Assert.Contains("UiGlStateSnapshot.Capture", source, StringComparison.Ordinal);
        Assert.Contains("state.Restore(_gl)", source, StringComparison.Ordinal);
        Assert.Contains("BlendingFactor.SrcAlpha", source, StringComparison.Ordinal);
        Assert.Contains("ShouldDelegateComputeLighting", source, StringComparison.Ordinal);
        Assert.Contains("ShouldUseComputeLightComposite", source, StringComparison.Ordinal);
        Assert.Contains("ComputeLightCompositePass", source, StringComparison.Ordinal);
        Assert.Contains("FrameSubPhase.GpuLightComposite", source, StringComparison.Ordinal);
        Assert.Contains("ComputeFeatureSwitches? computeFeatures", source, StringComparison.Ordinal);
        Assert.Contains("new RadianceCascadePass", source, StringComparison.Ordinal);
        Assert.Contains("ShouldUseRadianceCascades", source, StringComparison.Ordinal);
        Assert.Contains("FrameSubPhase.GpuRadianceCascades", source, StringComparison.Ordinal);
        Assert.Contains("DegradeGpuComputeOneStep", source, StringComparison.Ordinal);
        Assert.Contains("PublishComputeDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("_computeGate.PublishDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("RadianceCascades.Enabled", source, StringComparison.Ordinal);
        Assert.Contains("Settings.PreferComputeLighting = false", source, StringComparison.Ordinal);
        Assert.Contains("CreateComputeResourcesSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("CurrentViewportTexture = new RenderViewportTexture", source, StringComparison.Ordinal);
        Assert.Contains("ReadOnlySpan<LightSource> pointLights", source, StringComparison.Ordinal);
        Assert.Contains("UploadVisibility(fogOfWar, pointLights)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPointLights(mask, pointLights)", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("_worldBlit.Render", StringComparison.Ordinal) < source.IndexOf("_composite.Render", StringComparison.Ordinal));
        Assert.True(source.IndexOf("_composite.Render", StringComparison.Ordinal) < source.IndexOf("_overlay.Render", StringComparison.Ordinal));
        Assert.True(source.IndexOf("CurrentViewportTexture = new RenderViewportTexture", StringComparison.Ordinal) < source.IndexOf("_present.Render", StringComparison.Ordinal));
        Assert.True(source.IndexOf("PresentUiLayers(new UiPresentContext", StringComparison.Ordinal) < source.IndexOf("BeforePresentUi?.Invoke", StringComparison.Ordinal));
    }

    [Fact]
    public void CompositeShadersSampleCpuUploadedEffectMasksWithTopLeftOrigin()
    {
        string fragmentComposite = LightingShaderSources.CompositeFragment(GlslProfile.DesktopGl330);

        Assert.Contains("vec2 cpuUv = vec2(vUv.x, 1.0 - vUv.y);", fragmentComposite, StringComparison.Ordinal);
        Assert.Contains("texture(uEmissiveTexture, cpuUv)", fragmentComposite, StringComparison.Ordinal);
        Assert.Contains("texture(uVisibilityTexture, cpuUv)", fragmentComposite, StringComparison.Ordinal);
        Assert.Contains("vec2 cpuUv = vec2(uv.x, 1.0 - uv.y);", GpuComputeShaderSources.LightComposite, StringComparison.Ordinal);
        Assert.Contains("texture(uEmissiveTexture, cpuUv)", GpuComputeShaderSources.LightComposite, StringComparison.Ordinal);
        Assert.Contains("texture(uVisibilityTexture, cpuUv)", GpuComputeShaderSources.LightComposite, StringComparison.Ordinal);
    }

    [Fact]
    public void UiPresentLayersUseStableOrdersForGameAndEditor()
    {
        string pipeline = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));
        string orders = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiPresentLayerOrders.cs"));
        string guiBridge = File.ReadAllText(ProjectPath("src", "PixelEngine.Gui", "GuiRenderBridge.cs"));
        string uiCompositor = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "UiLayerCompositor.cs"));
        string editorBridge = File.ReadAllText(ProjectPath("src", "PixelEngine.Editor", "EditorRenderBridge.cs"));

        Assert.Contains("public interface IUiPresentLayer", File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "IUiPresentLayer.cs")), StringComparison.Ordinal);
        string context = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiPresentContext.cs"));
        string primitive = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiPrimitiveRenderer.cs"));
        string state = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiGlStateSnapshot.cs"));

        Assert.Contains("public readonly struct UiPresentContext", context, StringComparison.Ordinal);
        Assert.Contains("SubmitTriangles(ReadOnlySpan<UiVertex> vertices, ReadOnlySpan<ushort> indices", context, StringComparison.Ordinal);
        Assert.Contains("UiPrimitiveRenderer", context, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct UiVertex", File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiVertex.cs")), StringComparison.Ordinal);
        Assert.Contains("public readonly record struct UiDrawState", File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiDrawState.cs")), StringComparison.Ordinal);
        Assert.Contains("public readonly record struct UiScissorRect", File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiScissorRect.cs")), StringComparison.Ordinal);
        string overlayTexture = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "UiOverlayTexture.cs"));
        Assert.Contains("public sealed unsafe class UiOverlayTexture", overlayTexture, StringComparison.Ordinal);
        Assert.Contains("UploadDirtyRects(", overlayTexture, StringComparison.Ordinal);
        Assert.Contains("TexSubImage2D", overlayTexture, StringComparison.Ordinal);
        Assert.Contains("PixelUnpackBufferBinding", overlayTexture, StringComparison.Ordinal);
        Assert.Contains("UnpackRowLength", overlayTexture, StringComparison.Ordinal);
        Assert.Contains("ActiveTexture(TextureUnit.Texture0)", overlayTexture, StringComparison.Ordinal);
        Assert.Contains("finally", overlayTexture, StringComparison.Ordinal);
        Assert.Contains("UploadOverlayTexture(", context, StringComparison.Ordinal);
        Assert.Contains("FrameSubPhase.UiUpload", context, StringComparison.Ordinal);
        Assert.Contains("public const int Game = 100", orders, StringComparison.Ordinal);
        Assert.Contains("public const int Editor = 200", orders, StringComparison.Ordinal);
        Assert.Contains("while (index > 0 && CompareUiLayers(entry, _uiLayers[index - 1]) < 0)", pipeline, StringComparison.Ordinal);
        Assert.Contains("UiLayerEntry(int Order, int Sequence, IUiPresentLayer Layer)", pipeline, StringComparison.Ordinal);
        Assert.Contains("new UiPrimitiveRenderer(_gl, profile)", pipeline, StringComparison.Ordinal);
        Assert.Contains("DrawElements", primitive, StringComparison.Ordinal);
        Assert.Contains("BufferSubData(BufferTargetARB.ArrayBuffer", primitive, StringComparison.Ordinal);
        Assert.Contains("BufferSubData(BufferTargetARB.ElementArrayBuffer", primitive, StringComparison.Ordinal);
        Assert.Contains("Scissor(rect.X", primitive, StringComparison.Ordinal);
        Assert.Contains("UniformMatrix3", primitive, StringComparison.Ordinal);
        Assert.Contains("UiGlStateSnapshot.Capture", pipeline, StringComparison.Ordinal);
        Assert.Contains("FramebufferBinding", state, StringComparison.Ordinal);
        Assert.Contains("VertexArrayBinding", state, StringComparison.Ordinal);
        Assert.Contains("BlendFuncSeparate", state, StringComparison.Ordinal);
        Assert.Contains("BlendEquationRgb", state, StringComparison.Ordinal);
        Assert.Contains("BlendEquationSeparate", state, StringComparison.Ordinal);
        Assert.Contains("PixelUnpackBufferBinding", state, StringComparison.Ordinal);
        Assert.Contains("TextureUnit.Texture0", state, StringComparison.Ordinal);
        Assert.Contains("UnpackAlignment", state, StringComparison.Ordinal);
        Assert.Contains("UnpackRowLength", state, StringComparison.Ordinal);
        Assert.Contains("UnpackSkipPixels", state, StringComparison.Ordinal);
        Assert.Contains("UnpackSkipRows", state, StringComparison.Ordinal);
        Assert.Contains("BindBuffer(BufferTargetARB.PixelUnpackBuffer", state, StringComparison.Ordinal);
        Assert.Contains("PixelStore(PixelStoreParameter.UnpackAlignment", state, StringComparison.Ordinal);
        Assert.Contains("RegisterUiLayer(UiPresentLayerOrders.Game, this)", guiBridge, StringComparison.Ordinal);
        Assert.Contains("RegisterUiLayer(UiPresentLayerOrders.Game, this)", uiCompositor, StringComparison.Ordinal);
        Assert.Contains("RegisterUiLayer(UiPresentLayerOrders.Editor, this)", editorBridge, StringComparison.Ordinal);
        Assert.Contains("IUiPresentLayer, IDisposable", guiBridge, StringComparison.Ordinal);
        Assert.Contains("IUiPresentLayer, IDisposable", uiCompositor, StringComparison.Ordinal);
        Assert.Contains("IUiPresentLayer, IDisposable", editorBridge, StringComparison.Ordinal);
    }

    private static string ProjectPath(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            path = Directory.GetParent(path)!.FullName;
        }

        return Path.Combine([path, .. parts]);
    }

    private static void AssertThrows<T>(Action action)
        where T : Exception
    {
        T exception = Assert.Throws<T>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
}
