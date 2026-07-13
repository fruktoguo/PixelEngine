using PixelEngine.Testing;
using Xunit;
using Xunit.Abstractions;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// Player 三种窗口模式的纯合同与真实 Win32 冒烟。
/// </summary>
[Collection(RenderWindowNativeCollection.Name)]
public sealed class PlayerWindowModeProbeTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void WindowedRequiresResizableCaptionAndPresentationSizedClient()
    {
        PlayerWindowModeProbeSnapshot snapshot = Snapshot(
            PlayerWindowMode.Windowed,
            style: 0x00CF0000,
            isZoomed: false,
            windowRect: new PlatformPixelRect(50, 20, 1132, 772),
            clientRect: new PlatformPixelRect(0, 0, 1080, 720),
            monitorRect: new PlatformPixelRect(0, 0, 3840, 2160),
            workRect: new PlatformPixelRect(0, 0, 3840, 2054));

        PlayerWindowModeProbeEvaluation evaluation = PlayerWindowModeProbe.Evaluate(in snapshot);

        Assert.True(evaluation.Applied, evaluation.Reason);
    }

    [Fact]
    public void WindowedRejectsClientThatDoesNotMatchConfiguredPresentation()
    {
        PlayerWindowModeProbeSnapshot snapshot = Snapshot(
            PlayerWindowMode.Windowed,
            style: 0x00CF0000,
            isZoomed: false,
            windowRect: new PlatformPixelRect(50, 20, 1332, 772),
            clientRect: new PlatformPixelRect(0, 0, 1280, 720),
            monitorRect: new PlatformPixelRect(0, 0, 3840, 2160),
            workRect: new PlatformPixelRect(0, 0, 3840, 2054));

        PlayerWindowModeProbeEvaluation evaluation = PlayerWindowModeProbe.Evaluate(in snapshot);

        Assert.False(evaluation.Applied);
        Assert.Equal("windowed_client_size_mismatch", evaluation.Reason);
    }

    [Fact]
    public void WindowedAllowsOperatingSystemClampWhenConfiguredWindowDoesNotFitWorkArea()
    {
        PlayerWindowModeProbeSnapshot snapshot = Snapshot(
            PlayerWindowMode.Windowed,
            style: 0x00CF0000,
            isZoomed: false,
            windowRect: new PlatformPixelRect(0, 0, 1044, 759),
            clientRect: new PlatformPixelRect(0, 0, 1028, 720),
            monitorRect: new PlatformPixelRect(0, 0, 1024, 768),
            workRect: new PlatformPixelRect(0, 0, 1024, 720));

        PlayerWindowModeProbeEvaluation evaluation = PlayerWindowModeProbe.Evaluate(in snapshot);

        Assert.True(evaluation.Applied, evaluation.Reason);
        Assert.False(snapshot.ClientMatchesRequestedPresentation);
        Assert.False(snapshot.RequestedWindowFitsWorkArea);
    }

    [Fact]
    public void MaximizedRequiresZoomedResizableWindowCoveringWorkArea()
    {
        PlayerWindowModeProbeSnapshot snapshot = Snapshot(
            PlayerWindowMode.MaximizedWindow,
            style: 0x01CF0000,
            isZoomed: true,
            windowRect: new PlatformPixelRect(-8, -8, 3848, 2062),
            clientRect: new PlatformPixelRect(0, 0, 3840, 2015),
            monitorRect: new PlatformPixelRect(0, 0, 3840, 2160),
            workRect: new PlatformPixelRect(0, 0, 3840, 2054));

        PlayerWindowModeProbeEvaluation evaluation = PlayerWindowModeProbe.Evaluate(in snapshot);

        Assert.True(evaluation.Applied, evaluation.Reason);
    }

    [Fact]
    public void BorderlessRequiresPopupWithoutCaptionCoveringWholeMonitor()
    {
        PlayerWindowModeProbeSnapshot snapshot = Snapshot(
            PlayerWindowMode.BorderlessFullscreen,
            style: 0x96000000,
            isZoomed: false,
            windowRect: new PlatformPixelRect(0, 0, 3840, 2160),
            clientRect: new PlatformPixelRect(0, 0, 3840, 2160),
            monitorRect: new PlatformPixelRect(0, 0, 3840, 2160),
            workRect: new PlatformPixelRect(0, 0, 3840, 2054));

        PlayerWindowModeProbeEvaluation evaluation = PlayerWindowModeProbe.Evaluate(in snapshot);

        Assert.True(evaluation.Applied, evaluation.Reason);
    }

    [Fact]
    public void BorderlessRejectsCaptionedMonitorSizedWindow()
    {
        PlayerWindowModeProbeSnapshot snapshot = Snapshot(
            PlayerWindowMode.BorderlessFullscreen,
            style: 0x96CF0000,
            isZoomed: false,
            windowRect: new PlatformPixelRect(0, 0, 3840, 2160),
            clientRect: new PlatformPixelRect(0, 0, 3840, 2160),
            monitorRect: new PlatformPixelRect(0, 0, 3840, 2160),
            workRect: new PlatformPixelRect(0, 0, 3840, 2054));

        PlayerWindowModeProbeEvaluation evaluation = PlayerWindowModeProbe.Evaluate(in snapshot);

        Assert.False(evaluation.Applied);
        Assert.Equal("borderless_style_mismatch", evaluation.Reason);
    }

    [NativeSmokeFact]
    public void ThreeWindowModesMatchActualWin32StateWhenExplicitlyEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        nint baselineMonitor = 0;
        PlatformPixelRect baselineMonitorRect = default;
        foreach (PlayerWindowMode mode in Enum.GetValues<PlayerWindowMode>())
        {
            using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
            {
                Title = $"PixelEngine {mode} smoke",
                Width = 640,
                Height = 360,
                WindowMode = mode,
                BackendPreference = RenderBackendPreference.DesktopGl33,
                VSync = false,
            });
            for (int i = 0; i < 4; i++)
            {
                window.DoEvents();
            }

            PlayerWindowModeProbeSnapshot snapshot = PlayerWindowModeProbe.Capture(window);
            PlayerWindowModeProbeEvaluation evaluation = PlayerWindowModeProbe.Evaluate(in snapshot);
            _output.WriteLine(
                $"{mode}: applied={evaluation.Applied}, reason={evaluation.Reason}, " +
                $"style=0x{snapshot.Style:X8}, zoomed={snapshot.IsZoomed}, " +
                $"window={snapshot.WindowRect.ToProbeValue()}, client={snapshot.ClientRect.ToProbeValue()}, " +
                $"monitor={snapshot.MonitorRect.ToProbeValue()}, work={snapshot.WorkRect.ToProbeValue()}, dpi={snapshot.Dpi}");

            Assert.True(evaluation.Applied, $"{mode}: {evaluation.Reason}");
            if (mode == PlayerWindowMode.Windowed)
            {
                baselineMonitor = snapshot.MonitorHandle;
                baselineMonitorRect = snapshot.MonitorRect;
            }
            else if (mode == PlayerWindowMode.BorderlessFullscreen && snapshot.MonitorHandle == baselineMonitor)
            {
                Assert.Equal(baselineMonitorRect, snapshot.MonitorRect);
            }
        }
    }

    private static PlayerWindowModeProbeSnapshot Snapshot(
        PlayerWindowMode mode,
        uint style,
        bool isZoomed,
        PlatformPixelRect windowRect,
        PlatformPixelRect clientRect,
        PlatformPixelRect monitorRect,
        PlatformPixelRect workRect)
    {
        return new PlayerWindowModeProbeSnapshot(
            available: true,
            captureFailureReason: "none",
            requestedMode: mode,
            requestedWidth: 1080,
            requestedHeight: 720,
            windowHandle: 1,
            monitorHandle: 2,
            style,
            extendedStyle: 0,
            isVisible: true,
            isZoomed,
            windowRect,
            clientRect,
            monitorRect,
            workRect,
            dpi: 144);
    }
}
