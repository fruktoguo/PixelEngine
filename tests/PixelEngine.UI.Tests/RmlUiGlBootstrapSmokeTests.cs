using System.Buffers.Binary;
using System.IO.Compression;
using PixelEngine.Rendering;
using PixelEngine.Testing;
using Silk.NET.OpenGL;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// RmlUi GL 引导冒烟测试：校验视口区域解析、IME 组合输入规范化及 C#/native 源码契约。
/// </summary>
public sealed class RmlUiGlBootstrapSmokeTests
{
    /// <summary>
    /// 验证Rml Ui Composite解析Present Target Viewport Region。
    /// </summary>
    [Fact]
    public void RmlUiCompositeResolvesPresentTargetViewportRegion()
    {
        UiPresentContext context = default;
        UiPresentContext targetContext = context.WithTarget(new UiPresentTarget(12, 16, 320, 180, 1f));

        Assert.Equal((0, 0, 64, 48), RmlUiBackend.ResolveCompositeViewportRegion(in context, 64, 48));
        Assert.Equal((12, 16, 320, 180), RmlUiBackend.ResolveCompositeViewportRegion(in targetContext, 64, 48));
        Assert.Equal((12, 524, 320, 180), RmlUiBackend.ResolveCompositeViewportRegion(new UiPresentTarget(12, 16, 320, 180, 1f), 720, 64, 48));
    }

    /// <summary>
    /// 验证Rml Ui Text Composition规范化And Clears Preedit State。
    /// </summary>
    [Fact]
    public void RmlUiTextCompositionNormalizesAndClearsPreeditState()
    {
        UiTextComposition active = RmlUiBackend.NormalizeTextComposition(
            "候補",
            new UiTextComposition(isActive: true, cursorIndex: 99, selectionStart: 1, selectionLength: 99));
        UiTextComposition empty = RmlUiBackend.NormalizeTextComposition(
            [],
            new UiTextComposition(isActive: true, cursorIndex: 1));
        UiTextComposition inactive = RmlUiBackend.NormalizeTextComposition(
            "候補",
            UiTextComposition.Inactive);

        Assert.True(active.IsActive);
        Assert.Equal(2, active.CursorIndex);
        Assert.Equal(1, active.SelectionStart);
        Assert.Equal(1, active.SelectionLength);
        Assert.False(empty.IsActive);
        Assert.False(inactive.IsActive);
    }

    /// <summary>
    /// 验证Rml Ui Composite Source Documents Present Target Viewport Contract。
    /// </summary>
    [Fact]
    public void RmlUiCompositeSourceDocumentsPresentTargetViewportContract()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiBackend.cs"));
        string nativeBinding = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiNative.cs"));
        string nativeShim = File.ReadAllText(ProjectPath("native", "ui_native", "PixelEngineUiNative.cpp"));
        string gl3Renderer = File.ReadAllText(ProjectPath("native", "rmlui", "Backends", "RmlUi_Renderer_GL3.cpp"));

        Assert.Contains("context.Target", source, StringComparison.Ordinal);
        Assert.Contains("target.IsValid", source, StringComparison.Ordinal);
        Assert.Contains("target.X", source, StringComparison.Ordinal);
        Assert.Contains("target.Y", source, StringComparison.Ordinal);
        Assert.Contains("target.Width", source, StringComparison.Ordinal);
        Assert.Contains("target.Height", source, StringComparison.Ordinal);
        Assert.Contains("context.FramebufferHeight", source, StringComparison.Ordinal);
        Assert.Contains("framebufferHeight - target.Y - target.Height", source, StringComparison.Ordinal);
        Assert.Contains("RmlUiNative.RendererSetCanvasMetrics", source, StringComparison.Ordinal);
        Assert.Contains("CanvasMetrics.LayoutWidth", source, StringComparison.Ordinal);
        Assert.Contains("CanvasMetrics.LayoutHeight", source, StringComparison.Ordinal);
        Assert.DoesNotContain("context.FramebufferWidth, context.FramebufferHeight", source, StringComparison.Ordinal);
        Assert.Contains("peui_native_renderer_set_canvas_metrics", nativeBinding, StringComparison.Ordinal);
        Assert.Contains("peui_native_renderer_set_canvas_metrics", nativeShim, StringComparison.Ordinal);
        Assert.Contains("peui_native_renderer_set_viewport_region", nativeBinding, StringComparison.Ordinal);
        Assert.Contains("peui_native_renderer_set_viewport_region", nativeShim, StringComparison.Ordinal);
        Assert.Contains("renderer->renderer->SetViewport(width, height, x, y)", nativeShim, StringComparison.Ordinal);
        Assert.Contains("renderer->context->SetDimensions(Rml::Vector2i(layout_width, layout_height))", nativeShim, StringComparison.Ordinal);
        Assert.Contains("renderer->renderer->SetViewport(renderer->layoutWidth, renderer->layoutHeight)", nativeShim, StringComparison.Ordinal);
        Assert.Contains("renderer->outputWidth", nativeShim, StringComparison.Ordinal);
        Assert.Contains("glViewport(viewport_offset_x, viewport_offset_y, viewport_width, viewport_height)", gl3Renderer, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 native FileInterface 在 Windows 以 UTF-8→filesystem path→_wfopen 打开非 ASCII 路径。
    /// </summary>
    [Fact]
    public void RmlUiNativeFileInterfaceUsesUnicodeSafeWindowsPathOpen()
    {
        string nativeShim = File.ReadAllText(ProjectPath("native", "ui_native", "PixelEngineUiNative.cpp"));

        Assert.Contains("class PeUiFileInterface final : public Rml::FileInterface", nativeShim, StringComparison.Ordinal);
        Assert.Contains("std::filesystem::u8path(path)", nativeShim, StringComparison.Ordinal);
        Assert.Contains("_wfopen(nativePath.c_str(), L\"rb\")", nativeShim, StringComparison.Ordinal);
        Assert.Contains("Rml::SetFileInterface(&g_fileInterface)", nativeShim, StringComparison.Ordinal);
        Assert.Contains("Rml::SetFileInterface(nullptr)", nativeShim, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Rml Ui Composition Source保持Preedit Separate From Committed Text。
    /// </summary>
    [Fact]
    public void RmlUiCompositionSourceKeepsPreeditSeparateFromCommittedText()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiBackend.cs"));
        int start = source.IndexOf("public void FeedTextComposition", StringComparison.Ordinal);
        int end = source.IndexOf("private void ApplyNativeComposition", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string method = source[start..end];

        Assert.Contains("CompositionText", method, StringComparison.Ordinal);
        Assert.Contains("CompositionState", method, StringComparison.Ordinal);
        Assert.Contains("NormalizeTextComposition(text, in composition)", method, StringComparison.Ordinal);
        Assert.Contains("ApplyNativeComposition", method, StringComparison.Ordinal);
        Assert.Contains("Dirty = true", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessTextUtf8", method, StringComparison.Ordinal);
        Assert.DoesNotContain("FeedText(", method, StringComparison.Ordinal);

        int feedTextStart = source.IndexOf("public void FeedText(", StringComparison.Ordinal);
        int feedTextEnd = source.IndexOf("public void FeedTextComposition", feedTextStart, StringComparison.Ordinal);
        string feedText = source[feedTextStart..feedTextEnd];
        Assert.Contains("ConfirmTextComposition", feedText, StringComparison.Ordinal);
        Assert.Contains("ProcessTextUtf8", feedText, StringComparison.Ordinal);
        Assert.Contains("CompositionState.IsActive", feedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Rml Ui Ime Geometry Source Prefers Native Text Input Bounds Over Overlay Fallback。
    /// </summary>
    [Fact]
    public void RmlUiImeGeometrySourcePrefersNativeTextInputBoundsOverOverlayFallback()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiBackend.cs"));
        string nativeBinding = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiNative.cs"));
        string nativeShim = File.ReadAllText(ProjectPath("native", "ui_native", "PixelEngineUiNative.cpp"));

        Assert.Contains("TryGetNativeTextInputGeometry", source, StringComparison.Ordinal);
        Assert.Contains("TryGetActiveTextInputGeometry", source, StringComparison.Ordinal);
        Assert.Contains("CompositionImeGeometry", source, StringComparison.Ordinal);
        Assert.Contains("peui_native_try_get_active_text_input_geometry", nativeBinding, StringComparison.Ordinal);
        Assert.Contains("peui_native_try_get_active_text_input_geometry", nativeShim, StringComparison.Ordinal);
        Assert.Contains("SetTextInputHandler", nativeShim, StringComparison.Ordinal);
        Assert.Contains("GetBoundingBox", nativeShim, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Rml Ui Gles Angle Profile Source Documents Shader Rewrite And Native Profile Api。
    /// </summary>
    [Fact]
    public void RmlUiGlesAngleProfileSourceDocumentsShaderRewriteAndNativeProfileApi()
    {
        string gate = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiNativeProfileGate.cs"));
        string bootstrap = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiGlBootstrap.cs"));
        string nativeBinding = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiNative.cs"));
        string nativeShim = File.ReadAllText(ProjectPath("native", "ui_native", "PixelEngineUiNative.cpp"));
        string gl3 = File.ReadAllText(ProjectPath("native", "rmlui", "Backends", "RmlUi_Renderer_GL3.cpp"));

        Assert.Contains("CanUseNativeRenderer", gate, StringComparison.Ordinal);
        Assert.Contains("NativeProfileGles3Angle = 1", gate, StringComparison.Ordinal);
        Assert.Contains("SetRendererProfile", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ToNativeProfileId", bootstrap, StringComparison.Ordinal);
        Assert.Contains("peui_native_set_renderer_profile", nativeBinding, StringComparison.Ordinal);
        Assert.Contains("peui_native_set_renderer_profile", nativeShim, StringComparison.Ordinal);
        Assert.Contains("SetPixelEngineGlShaderProfile", nativeShim, StringComparison.Ordinal);
        Assert.Contains("RewriteShaderSource", gl3, StringComparison.Ordinal);
        Assert.Contains("#version 300 es\\nprecision highp float;", gl3, StringComparison.Ordinal);
        Assert.Contains("Gles300Angle", gl3, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Rml Ui Native Composition Bridge Documents Set Confirm And Cancel Apis。
    /// </summary>
    [Fact]
    public void RmlUiNativeCompositionBridgeDocumentsSetConfirmAndCancelApis()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiBackend.cs"));
        string nativeBinding = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "RmlUiNative.cs"));
        string nativeShim = File.ReadAllText(ProjectPath("native", "ui_native", "PixelEngineUiNative.cpp"));
        int compositionStart = source.IndexOf("public void FeedTextComposition", StringComparison.Ordinal);
        int compositionEnd = source.IndexOf("private void ApplyNativeComposition", compositionStart, StringComparison.Ordinal);
        Assert.True(compositionStart >= 0 && compositionEnd > compositionStart);
        string compositionMethod = source[compositionStart..compositionEnd];

        Assert.Contains("peui_native_set_text_composition", nativeBinding, StringComparison.Ordinal);
        Assert.Contains("peui_native_confirm_text_composition", nativeBinding, StringComparison.Ordinal);
        Assert.Contains("peui_native_is_text_composition_active", nativeBinding, StringComparison.Ordinal);
        Assert.Contains("peui_native_set_text_composition", nativeShim, StringComparison.Ordinal);
        Assert.Contains("peui_native_confirm_text_composition", nativeShim, StringComparison.Ordinal);
        Assert.Contains("SetComposition", nativeShim, StringComparison.Ordinal);
        Assert.Contains("CancelComposition", nativeShim, StringComparison.Ordinal);
        Assert.Contains("ConfirmComposition", nativeShim, StringComparison.Ordinal);
        Assert.Contains("CommitComposition", nativeShim, StringComparison.Ordinal);
        Assert.Contains("SetTextComposition", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmTextComposition", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessTextUtf8", compositionMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Can Create Native Renderer When Gl Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void CanCreateNativeRendererWhenGlSmokeIsEnabled()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi GL smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });

        Assert.True(RmlUiGlBootstrap.TryProbeRenderer(window, out RmlUiGlVersion version));
        Assert.True(version.Major >= 3);
        Assert.Equal(RmlUiNativeProfileGate.NativeProfileDesktopGl3, RmlUiNative.GetRendererProfile());
    }

    /// <summary>
    /// 验证Can Create Native Renderer On Angle Gles When Angle Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact("PIXELENGINE_RENDERING_ANGLE_SMOKE")]
    [Trait("Category", "NativeSmoke")]
    public void CanCreateNativeRendererOnAngleGlesWhenAngleSmokeIsEnabled()
    {
        RenderWindow? window = null;
        try
        {
            window = RenderWindow.Create(new RenderWindowOptions
            {
                Title = "PixelEngine RmlUi ANGLE GLES smoke",
                Width = 64,
                Height = 64,
                BackendPreference = RenderBackendPreference.GlEs30Angle,
                EnableDebugContext = true,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"显式 ANGLE smoke 要求可创建 GlEs30Angle 窗口，但失败：{ex.Message}", ex);
        }

        using (window)
        {
            Assert.Equal(RenderBackend.GlEs30Angle, window.Backend);
            RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(window.Backend, window.Capabilities);
            Assert.Equal(RmlUiNativeRendererProfile.Gles3Angle, decision.RequestedProfile);
            Assert.True(decision.CanUseNativeRenderer, decision.FallbackReason);
            Assert.Equal("#version 300 es", decision.ShaderVersionDirective);

            Assert.True(RmlUiGlBootstrap.TryProbeRenderer(window, out RmlUiGlVersion version), "RmlUi native 应在 ANGLE/GLES 上下文用 300 es profile 成功 create/destroy renderer。");
            Assert.True(version.Major >= 3 || window.Capabilities.MajorVersion >= 3);
            Assert.Equal(RmlUiNativeProfileGate.NativeProfileGles3Angle, RmlUiNative.GetRendererProfile());
        }
    }

    /// <summary>
    /// 验证Rml Ui Backend Can Load And Render Document When Gl Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiBackendCanLoadAndRenderDocumentWhenGlSmokeIsEnabled()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi backend smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });
        RunDocumentLoadRenderHitTestSmoke(
            window,
            expectedProfileId: RmlUiNativeProfileGate.NativeProfileDesktopGl3);
    }

    /// <summary>
    /// 验证正式输出常见的中文目录不会让 RmlUi 字体注册回退 ManagedFallback。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiBackendLoadsFontFromUnicodePathWhenGlSmokeIsEnabled()
    {
        string root = Path.Combine(Path.GetTempPath(), $"像素引擎-最终输出-{Guid.NewGuid():N}", "游戏Demo", "content", "ui", "fonts");
        _ = Directory.CreateDirectory(root);
        string fontPath = Path.Combine(root, "NotoSansSC-VF.ttf");
        File.Copy(
            ProjectPath("demo", "PixelEngine.Demo", "content", "ui", "fonts", "NotoSansSC-VF.ttf"),
            fontPath);

        try
        {
            using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
            {
                Title = "PixelEngine RmlUi Unicode font smoke",
                Width = 64,
                Height = 64,
                BackendPreference = RenderBackendPreference.DesktopGl33,
                EnableDebugContext = true,
            });
            using RmlUiBackend backend = new(window);
            UiFontSelection font = new(fontPath, 16f, UiFontSource.ContentFonts);

            backend.Initialize(new UiBackendInitializeInfo(
                new UiViewport(0, 0, window.Width, window.Height, 1f),
                UiBackendKind.RmlUi,
                font));

            Assert.Equal(UiBackendKind.RmlUi, backend.Kind);
            Assert.Equal(RmlUiNativeProfileGate.NativeProfileDesktopGl3, RmlUiNative.GetRendererProfile());
        }
        finally
        {
            Directory.Delete(Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..")), recursive: true);
        }
    }

    /// <summary>
    /// 验证 CanvasScaler logical viewport、presentation raster 与输入映射在真实 RmlUi GL 路径保持一致。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiCanvasScalerSeparatesLogicalLayoutFromPresentationRasterWhenGlSmokeIsEnabled()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi CanvasScaler smoke",
            Width = 128,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });
        RmlUiBackend backend = new(window);
        using GameUiHost host = new(backend);
        UiDisplayMetrics display = new(window.Width, window.Height, 1f, 1f, 96f, 1, 1);
        UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default with { ScaleFactor = 2f };
        UiBackendInitializeInfo info = new(display, settings, UiBackendKind.RmlUi);
        host.Initialize(in info);

        string documentPath = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-canvas-{Guid.NewGuid():N}.rml");
        try
        {
            File.WriteAllText(
                documentPath,
                """
                <rml>
                  <head>
                    <style>
                      body { margin: 0px; background-color: transparent; pointer-events: none; }
                      #panel { position: absolute; left: 0px; top: 0px; width: 64px; height: 32px; background-color: #ff2020; pointer-events: auto; }
                    </style>
                  </head>
                  <body><div id="panel" data-event-click="scaled_panel"></div></body>
                </rml>
                """);
            _ = host.ShowScreen(new UiScreenId(1), UiDocumentSource.Asset(documentPath, 1));
            host.Update(1f / 60f);

            Assert.Equal(
                (window.Width / 2f, window.Height / 2f),
                (backend.DebugCanvasMetrics.LogicalWidth, backend.DebugCanvasMetrics.LogicalHeight));
            Assert.True(host.HitTest(120f, 60f).WantsMouse);
            Assert.False(backend.HitTest(120f, 60f).WantsMouse);

            GL gl = window.Gl;
            window.BindPresentationFramebuffer();
            gl.Viewport(0, 0, (uint)window.Width, (uint)window.Height);
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(ClearBufferMask.ColorBufferBit);
            UiPresentContext context = default;
            host.Composite(in context);
            gl.Finish();
            byte[] pixel = new byte[4];
            gl.ReadPixels(120, 4, 1, 1, GLEnum.Rgba, GLEnum.UnsignedByte, out pixel[0]);

            Assert.True(pixel[0] > 128, $"scaled raster 右侧像素未被 Canvas 覆盖: rgba=({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]})");
            Assert.True(pixel[0] > pixel[1] * 2, "scaled raster 应保持红色面板颜色。 ");
        }
        finally
        {
            File.Delete(documentPath);
        }
    }

    /// <summary>
    /// 验证Rml Ui Backend Can Load And Render Document On Angle When Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact("PIXELENGINE_RENDERING_ANGLE_SMOKE")]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiBackendCanLoadAndRenderDocumentOnAngleWhenSmokeIsEnabled()
    {
        RenderWindow? window = null;
        try
        {
            window = RenderWindow.Create(new RenderWindowOptions
            {
                Title = "PixelEngine RmlUi ANGLE backend document smoke",
                Width = 64,
                Height = 64,
                BackendPreference = RenderBackendPreference.GlEs30Angle,
                EnableDebugContext = true,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"显式 ANGLE smoke 要求可创建 GlEs30Angle 窗口，但失败：{ex.Message}", ex);
        }

        using (window)
        {
            Assert.Equal(RenderBackend.GlEs30Angle, window.Backend);
            RunDocumentLoadRenderHitTestSmoke(
                window,
                expectedProfileId: RmlUiNativeProfileGate.NativeProfileGles3Angle);
        }
    }

    private static void RunDocumentLoadRenderHitTestSmoke(RenderWindow window, int expectedProfileId)
    {
        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(window.Backend, window.Capabilities);
        Assert.True(decision.CanUseNativeRenderer, decision.FallbackReason);
        Assert.Equal(expectedProfileId, RmlUiNativeProfileGate.ToNativeProfileId(decision.RequestedProfile));

        UiStringPool strings = new();
        using RmlUiBackend backend = new(window, stringResolver: strings);
        backend.Initialize(new UiBackendInitializeInfo(
            new UiViewport(0, 0, window.Width, window.Height, 1f),
            UiBackendKind.RmlUi));
        Assert.Equal(expectedProfileId, RmlUiNative.GetRendererProfile());

        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-{Guid.NewGuid():N}");
        string screens = Path.Combine(root, "screens");
        string images = Path.Combine(root, "images");
        _ = Directory.CreateDirectory(screens);
        _ = Directory.CreateDirectory(images);
        string documentPath = Path.Combine(screens, "main.rml");
        string imagePath = Path.Combine(images, "logo.png");
        WritePng(imagePath, 4, 4);
        try
        {
            File.WriteAllText(
                documentPath,
                """
                <rml>
                  <head>
                    <style>
                      body { background-color: transparent; pointer-events: none; }
                      #panel { position: absolute; left: 4px; top: 4px; width: 24px; height: 24px; background-color: #ff4040; pointer-events: auto; }
                      #score { position: absolute; left: 32px; top: 4px; width: 28px; height: 24px; color: #ffffff; pointer-events: none; }
                      #title { position: absolute; left: 4px; top: 52px; width: 56px; height: 10px; color: #ffffff; pointer-events: none; }
                      .armed_probe { position: absolute; left: 4px; top: 36px; width: 12px; height: 12px; }
                      #logo { position: absolute; left: 36px; top: 36px; width: 16px; height: 16px; pointer-events: none; }
                    </style>
                  </head>
                  <body>
                    <div id="panel" data-event-click="start_game"></div>
                    <div id="score" data-model="score">0</div>
                    <div id="score_mirror" path="score">0</div>
                    <div id="health" path="hud.health.current">0</div>
                    <div id="title" path="hud.title">title</div>
                    <input class="armed_probe" type="checkbox" path="weapon.armed" />
                    <img id="logo" data-image="logo" />
                  </body>
                </rml>
                """);

            ((IGameUiImagePreloader)backend).PreloadImage(imagePath);
            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(documentPath, 1));
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: false)]);
            backend.Update(1f / 60f);
            UiPathId scorePath = new(UiStableId.Hash("score"));
            UiPathId healthPath = UiModelPathName.ToPathId("hud.health.current");
            UiPathId armedPath = UiModelPathName.ToPathId("weapon.armed");
            UiPathId titlePath = UiModelPathName.ToPathId("hud.title");
            UiPathId[] paths = new UiPathId[5];
            int pathCount = backend.CopyModelPaths(document, paths);
            Assert.Equal(4, pathCount);
            Assert.Contains(scorePath, paths[..pathCount]);
            Assert.Contains(healthPath, paths[..pathCount]);
            Assert.Contains(armedPath, paths[..pathCount]);
            Assert.Contains(titlePath, paths[..pathCount]);
            UiStringHandle title = strings.Intern("晶体 3/3");
            backend.SetModelValue(document, scorePath, new UiValue(42L));
            backend.SetModelValue(document, healthPath, new UiValue(0.75));
            backend.SetModelValue(document, armedPath, UiValue.FromBoolean(true));
            backend.SetModelValue(document, titlePath, UiValue.FromStringHandle(title));
            backend.Update(1f / 60f);
            Assert.True(backend.TryGetModelValue(document, scorePath, out UiValue score));
            Assert.True(backend.TryGetModelValue(document, healthPath, out UiValue health));
            Assert.True(backend.TryGetModelValue(document, armedPath, out UiValue armed));
            Assert.True(backend.TryGetModelValue(document, titlePath, out UiValue titleValue));
            Assert.Equal(42L, score.AsInt64());
            Assert.Equal(0.75, health.AsDouble());
            Assert.True(armed.AsBoolean());
            Assert.Equal(title, titleValue.AsStringHandle());
            Assert.True(backend.InvokeAction(
                document,
                new UiActionId(UiStableId.Hash("start_game")),
                UiValue.FromBoolean(true)));
            UiEvent[] events = new UiEvent[4];
            Assert.Equal(0, backend.DrainEvents(events));

            backend.FeedPointerMove(20, 24);
            Assert.True(backend.HitTest(20, 24).WantsMouse);
            backend.FeedPointerMove(60, 60);
            Assert.False(backend.HitTest(60, 60).WantsMouse);
            backend.FeedPointerMove(20, 24);
            backend.FeedPointerButton(UiPointerButton.Left, isDown: true);
            backend.FeedPointerButton(UiPointerButton.Left, isDown: false);
            backend.Update(1f / 60f);
            int eventCount = backend.DrainEvents(events);
            Assert.Equal(1, eventCount);
            Assert.Equal(document, events[0].Document);
            Assert.Equal(new UiElementId(UiStableId.Hash("panel")), events[0].Element);
            Assert.Equal(new UiActionId(UiStableId.Hash("start_game")), events[0].Action);

            backend.FeedScroll(0, 1);
            backend.FeedKey(new UiKey(65), isDown: true, UiKeyModifiers.Control);
            backend.FeedKey(new UiKey(65), isDown: false, UiKeyModifiers.Control);
            backend.FeedText("a");
            // IME composition 必须与 committed text 分离：预编辑后 cancel 不得走 FeedText。
            backend.FeedTextComposition("候", new UiTextComposition(isActive: true, cursorIndex: 1));
            Assert.True(backend.DebugCompositionState.IsActive);
            backend.FeedTextComposition([], UiTextComposition.Inactive);
            Assert.False(backend.DebugCompositionState.IsActive);
            backend.Update(1f / 60f);

            UiPresentContext context = default;
            backend.Composite(in context);
            window.SwapBuffers();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 验证Rml Ui Backend Unload Modal Stops Capture When Gl Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiBackendUnloadModalStopsCaptureWhenGlSmokeIsEnabled()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi unload modal smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });
        RunUnloadModalStopsCaptureSmoke(window, RmlUiNativeProfileGate.NativeProfileDesktopGl3);
    }

    /// <summary>
    /// 验证Rml Ui Backend Unload Modal Stops Capture On Angle When Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact("PIXELENGINE_RENDERING_ANGLE_SMOKE")]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiBackendUnloadModalStopsCaptureOnAngleWhenSmokeIsEnabled()
    {
        if (!TryCreateAngleSmokeWindow("PixelEngine RmlUi ANGLE unload modal smoke", out RenderWindow? window))
        {
            throw new InvalidOperationException("ANGLE smoke window 创建失败。");
        }

        RenderWindow smokeWindow = window ?? throw new InvalidOperationException("ANGLE smoke window 创建成功但返回 null。");
        using (smokeWindow)
        {
            RunUnloadModalStopsCaptureSmoke(smokeWindow, RmlUiNativeProfileGate.NativeProfileGles3Angle);
        }
    }

    /// <summary>
    /// 验证Rml Ui Composite Restores Gl State When Gl Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiCompositeRestoresGlStateWhenGlSmokeIsEnabled()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi GL state smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });
        RunCompositeRestoresGlStateSmoke(window, RmlUiNativeProfileGate.NativeProfileDesktopGl3);
    }

    /// <summary>
    /// 验证Rml Ui Composite Restores Gl State On Angle When Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact("PIXELENGINE_RENDERING_ANGLE_SMOKE")]
    [Trait("Category", "NativeSmoke")]
    public void RmlUiCompositeRestoresGlStateOnAngleWhenSmokeIsEnabled()
    {
        if (!TryCreateAngleSmokeWindow("PixelEngine RmlUi ANGLE GL state smoke", out RenderWindow? window))
        {
            throw new InvalidOperationException("ANGLE smoke window 创建失败。");
        }

        RenderWindow smokeWindow = window ?? throw new InvalidOperationException("ANGLE smoke window 创建成功但返回 null。");
        using (smokeWindow)
        {
            RunCompositeRestoresGlStateSmoke(smokeWindow, RmlUiNativeProfileGate.NativeProfileGles3Angle);
        }
    }

    private static void RunUnloadModalStopsCaptureSmoke(RenderWindow window, int expectedProfileId)
    {
        using RmlUiBackend backend = new(window);
        backend.Initialize(new UiBackendInitializeInfo(
            new UiViewport(0, 0, window.Width, window.Height, 1f),
            UiBackendKind.RmlUi));
        Assert.Equal(expectedProfileId, RmlUiNative.GetRendererProfile());

        string documentPath = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-unload-{Guid.NewGuid():N}.rml");
        try
        {
            File.WriteAllText(
                documentPath,
                """
                <rml>
                  <head>
                    <style>
                      body { background-color: transparent; pointer-events: none; }
                    </style>
                  </head>
                  <body></body>
                </rml>
                """);

            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(documentPath, 1));
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: true)]);
            Assert.True(backend.HitTest(60, 60).WantsMouse);

            backend.UnloadDocument(document);
            UiHitResult afterUnload = backend.HitTest(60, 60);

            Assert.False(afterUnload.HitsUi);
            Assert.False(afterUnload.Opaque);
            Assert.False(afterUnload.WantsMouse);
            Assert.False(afterUnload.WantsKeyboard);
        }
        finally
        {
            File.Delete(documentPath);
        }
    }

    private static void RunCompositeRestoresGlStateSmoke(RenderWindow window, int expectedProfileId)
    {
        GL gl = window.Gl;
        using RmlUiBackend backend = new(window);
        backend.Initialize(new UiBackendInitializeInfo(
            new UiViewport(0, 0, window.Width, window.Height, 1f),
            UiBackendKind.RmlUi));
        Assert.Equal(expectedProfileId, RmlUiNative.GetRendererProfile());

        string documentPath = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-state-{Guid.NewGuid():N}.rml");
        uint texture = gl.GenTexture();
        try
        {
            File.WriteAllText(
                documentPath,
                """
                <rml>
                  <head>
                    <style>
                      body { background-color: transparent; }
                      #panel { position: absolute; left: 0px; top: 0px; width: 32px; height: 32px; background-color: #40ff40; }
                    </style>
                  </head>
                  <body><div id="panel"></div></body>
                </rml>
                """);

            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(documentPath, 1));
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: false)]);
            backend.Update(1f / 60f);

            gl.Enable(EnableCap.DepthTest);
            gl.Disable(EnableCap.Blend);
            gl.Enable(EnableCap.ScissorTest);
            gl.Scissor(1, 2, 9, 10);
            gl.Viewport(3, 4, 33, 34);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
            gl.ActiveTexture(TextureUnit.Texture1);
            gl.BindTexture(TextureTarget.Texture2D, texture);

            GlStateSample before = GlStateSample.Capture(gl);
            UiPresentContext context = default;
            backend.Composite(in context);
            GlStateSample after = GlStateSample.Capture(gl);

            Assert.Equal(before, after);
        }
        finally
        {
            gl.DeleteTexture(texture);
            File.Delete(documentPath);
        }
    }

    private static bool TryCreateAngleSmokeWindow(string title, out RenderWindow? window)
    {
        window = null;
        try
        {
            window = RenderWindow.Create(new RenderWindowOptions
            {
                Title = title,
                Width = 64,
                Height = 64,
                BackendPreference = RenderBackendPreference.GlEs30Angle,
                EnableDebugContext = true,
            });
            Assert.Equal(RenderBackend.GlEs30Angle, window.Backend);
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"显式 ANGLE smoke 要求可创建 GlEs30Angle 窗口，但失败：{ex.Message}", ex);
        }
    }

    private readonly record struct GlStateSample(
        int CurrentProgram,
        int VertexArray,
        int ArrayBuffer,
        int ElementArrayBuffer,
        int DrawFramebuffer,
        int ReadFramebuffer,
        int ActiveTexture,
        int TextureBinding2D,
        int BlendSrcRgb,
        int BlendDstRgb,
        int BlendSrcAlpha,
        int BlendDstAlpha,
        int BlendEquationRgb,
        int BlendEquationAlpha,
        int UnpackAlignment,
        bool Blend,
        bool Depth,
        bool Cull,
        bool Scissor,
        int ViewportX,
        int ViewportY,
        int ViewportWidth,
        int ViewportHeight,
        int ScissorX,
        int ScissorY,
        int ScissorWidth,
        int ScissorHeight)
    {
        public static GlStateSample Capture(GL gl)
        {
            Span<int> viewport = stackalloc int[4];
            Span<int> scissor = stackalloc int[4];
            gl.GetInteger(GLEnum.Viewport, viewport);
            gl.GetInteger(GLEnum.ScissorBox, scissor);
            gl.GetInteger(GLEnum.CurrentProgram, out int currentProgram);
            gl.GetInteger(GLEnum.VertexArrayBinding, out int vertexArray);
            gl.GetInteger(GLEnum.ArrayBufferBinding, out int arrayBuffer);
            gl.GetInteger(GLEnum.ElementArrayBufferBinding, out int elementArrayBuffer);
            gl.GetInteger(GLEnum.DrawFramebufferBinding, out int drawFramebuffer);
            gl.GetInteger(GLEnum.ReadFramebufferBinding, out int readFramebuffer);
            gl.GetInteger(GLEnum.ActiveTexture, out int activeTexture);
            gl.GetInteger(GLEnum.TextureBinding2D, out int textureBinding2D);
            gl.GetInteger(GLEnum.BlendSrcRgb, out int blendSrcRgb);
            gl.GetInteger(GLEnum.BlendDstRgb, out int blendDstRgb);
            gl.GetInteger(GLEnum.BlendSrcAlpha, out int blendSrcAlpha);
            gl.GetInteger(GLEnum.BlendDstAlpha, out int blendDstAlpha);
            gl.GetInteger(GLEnum.BlendEquationRgb, out int blendEquationRgb);
            gl.GetInteger(GLEnum.BlendEquationAlpha, out int blendEquationAlpha);
            gl.GetInteger(GLEnum.UnpackAlignment, out int unpackAlignment);
            return new GlStateSample(
                currentProgram,
                vertexArray,
                arrayBuffer,
                elementArrayBuffer,
                drawFramebuffer,
                readFramebuffer,
                activeTexture,
                textureBinding2D,
                blendSrcRgb,
                blendDstRgb,
                blendSrcAlpha,
                blendDstAlpha,
                blendEquationRgb,
                blendEquationAlpha,
                unpackAlignment,
                gl.IsEnabled(EnableCap.Blend),
                gl.IsEnabled(EnableCap.DepthTest),
                gl.IsEnabled(EnableCap.CullFace),
                gl.IsEnabled(EnableCap.ScissorTest),
                viewport[0],
                viewport[1],
                viewport[2],
                viewport[3],
                scissor[0],
                scissor[1],
                scissor[2],
                scissor[3]);
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        using MemoryStream idat = new();
        using (ZLibStream zlib = new(idat, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    zlib.WriteByte((byte)(x * 50));
                    zlib.WriteByte((byte)(y * 50));
                    zlib.WriteByte(180);
                    zlib.WriteByte(255);
                }
            }
        }

        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(file, "IHDR"u8, ihdr);
        WriteChunk(file, "IDAT"u8, idat.ToArray());
        WriteChunk(file, "IEND"u8, []);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        stream.Write(stackalloc byte[4]);
    }

    private static string ProjectPath(params string[] segments)
    {
        string root = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && !File.Exists(Path.Combine(root, "PixelEngine.sln")); i++)
        {
            root = Path.GetFullPath(Path.Combine(root, ".."));
        }

        return Path.Combine([root, .. segments]);
    }
}
