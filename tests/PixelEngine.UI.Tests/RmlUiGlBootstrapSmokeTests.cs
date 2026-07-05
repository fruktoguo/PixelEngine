using PixelEngine.Rendering;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class RmlUiGlBootstrapSmokeTests
{
    [Fact]
    public void CanCreateNativeRendererWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi GL smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });

        Assert.True(RmlUiGlBootstrap.TryProbeRenderer(window, out RmlUiGlVersion version));
        Assert.True(version.Major >= 3);
    }

    [Fact]
    public void RmlUiBackendCanLoadAndRenderDocumentWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi backend smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        using RmlUiBackend backend = new(window);
        backend.Initialize(new UiBackendInitializeInfo(
            new UiViewport(0, 0, window.Width, window.Height, 1f),
            UiBackendKind.RmlUi));

        string documentPath = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-{Guid.NewGuid():N}.rml");
        try
        {
            File.WriteAllText(
                documentPath,
                """
                <rml>
                  <head>
                    <style>
                      body { background-color: transparent; }
                      #panel { position: absolute; left: 4px; top: 4px; width: 24px; height: 24px; background-color: #ff4040; }
                    </style>
                  </head>
                  <body>
                    <div id="panel"></div>
                  </body>
                </rml>
                """);

            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(documentPath, 1));
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: false)]);
            backend.Update(1f / 60f);

            UiPresentContext context = default;
            backend.Composite(in context);
            window.SwapBuffers();
        }
        finally
        {
            File.Delete(documentPath);
        }
    }
}
