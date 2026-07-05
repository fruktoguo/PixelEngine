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
                      body { background-color: transparent; pointer-events: none; }
                      #panel { position: absolute; left: 4px; top: 4px; width: 24px; height: 24px; background-color: #ff4040; pointer-events: auto; }
                      #score { position: absolute; left: 32px; top: 4px; width: 28px; height: 24px; color: #ffffff; pointer-events: none; }
                    </style>
                  </head>
                  <body>
                    <div id="panel" data-event-click="start_game"></div>
                    <div id="score" data-model="score">0</div>
                  </body>
                </rml>
                """);

            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(documentPath, 1));
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: false)]);
            backend.Update(1f / 60f);
            UiPathId scorePath = new(UiStableId.Hash("score"));
            UiPathId[] paths = new UiPathId[4];
            Assert.Equal(1, backend.CopyModelPaths(document, paths));
            Assert.Equal(scorePath, paths[0]);
            backend.SetModelValue(document, scorePath, new UiValue(42L));
            Assert.True(backend.TryGetModelValue(document, scorePath, out UiValue score));
            Assert.Equal(42L, score.AsInt64());

            backend.FeedPointerMove(20, 24);
            Assert.True(backend.HitTest(20, 24).WantsMouse);
            backend.FeedPointerMove(60, 60);
            Assert.False(backend.HitTest(60, 60).WantsMouse);
            backend.FeedPointerMove(20, 24);
            backend.FeedPointerButton(UiPointerButton.Left, isDown: true);
            backend.FeedPointerButton(UiPointerButton.Left, isDown: false);
            backend.Update(1f / 60f);
            UiEvent[] events = new UiEvent[4];
            int eventCount = backend.DrainEvents(events);
            Assert.Equal(1, eventCount);
            Assert.Equal(document, events[0].Document);
            Assert.Equal(new UiElementId(UiStableId.Hash("panel")), events[0].Element);
            Assert.Equal(new UiActionId(UiStableId.Hash("start_game")), events[0].Action);

            backend.FeedScroll(0, 1);
            backend.FeedKey(new UiKey(65), isDown: true, UiKeyModifiers.Control);
            backend.FeedKey(new UiKey(65), isDown: false, UiKeyModifiers.Control);
            backend.FeedText("a");
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
