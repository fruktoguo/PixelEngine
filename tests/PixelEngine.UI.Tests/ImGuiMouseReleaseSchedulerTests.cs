using PixelEngine.Gui;
using System.Numerics;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class ImGuiMouseReleaseSchedulerTests
{
    [Fact]
    public void CompleteDragBetweenFramesDefersReleaseUntilTargetDownFrameWasVisible()
    {
        ImGuiMouseReleaseScheduler scheduler = new();
        scheduler.RecordPosition(new Vector2(100f, 100f));
        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: true, out bool releaseBeforeDown));
        Assert.False(releaseBeforeDown);
        scheduler.RecordPosition(new Vector2(500f, 400f));
        Assert.False(scheduler.ShouldEmitButtonEvent(0, down: false, out bool releaseBeforeUp));
        Assert.False(releaseBeforeUp);
        int[] releases = new int[ImGuiMouseReleaseScheduler.MaximumMouseButtons];

        Assert.Equal(0, scheduler.BeginFrame(releases));
        Assert.Equal(0, scheduler.BeginFrame(releases));
        Assert.Equal(1, scheduler.BeginFrame(releases));
        Assert.Equal(0, releases[0]);
        Assert.Equal(0, scheduler.BeginFrame(releases));
    }

    [Fact]
    public void ClickWithoutMovementEmitsReleaseImmediately()
    {
        ImGuiMouseReleaseScheduler scheduler = new();
        scheduler.RecordPosition(new Vector2(100f, 100f));

        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: true, out bool releaseBeforeDown));
        Assert.False(releaseBeforeDown);
        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: false, out bool releaseBeforeUp));
        Assert.False(releaseBeforeUp);
    }

    [Fact]
    public void DragThatAlreadySpansFrameEmitsReleaseImmediately()
    {
        ImGuiMouseReleaseScheduler scheduler = new();
        scheduler.RecordPosition(new Vector2(100f, 100f));
        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: true, out bool releaseBeforeDown));
        Assert.False(releaseBeforeDown);
        int[] releases = new int[ImGuiMouseReleaseScheduler.MaximumMouseButtons];
        _ = scheduler.BeginFrame(releases);
        scheduler.RecordPosition(new Vector2(500f, 400f));

        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: false, out bool releaseBeforeUp));
        Assert.False(releaseBeforeUp);
    }

    [Fact]
    public void FocusLossFlushesPendingAndLogicalButtonsExactlyOnce()
    {
        ImGuiMouseReleaseScheduler scheduler = new();
        scheduler.RecordPosition(new Vector2(10f, 10f));
        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: true, out bool releaseBeforeFirstDown));
        Assert.False(releaseBeforeFirstDown);
        scheduler.RecordPosition(new Vector2(40f, 40f));
        Assert.False(scheduler.ShouldEmitButtonEvent(0, down: false, out bool releaseBeforeFirstUp));
        Assert.False(releaseBeforeFirstUp);
        Assert.True(scheduler.ShouldEmitButtonEvent(1, down: true, out bool releaseBeforeSecondDown));
        Assert.False(releaseBeforeSecondDown);
        int[] releases = new int[ImGuiMouseReleaseScheduler.MaximumMouseButtons];

        int count = scheduler.FlushForFocusLoss(releases);

        Assert.Equal(2, count);
        Assert.Equal([0, 1], releases.AsSpan(0, count).ToArray());
        Assert.Equal(0, scheduler.FlushForFocusLoss(releases));
    }

    [Fact]
    public void RapidSecondDownPreservesPendingReleaseBeforeNewPress()
    {
        ImGuiMouseReleaseScheduler scheduler = new();
        scheduler.RecordPosition(new Vector2(10f, 10f));
        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: true, out bool releaseBeforeFirstDown));
        Assert.False(releaseBeforeFirstDown);
        scheduler.RecordPosition(new Vector2(40f, 40f));
        Assert.False(scheduler.ShouldEmitButtonEvent(0, down: false, out bool releaseBeforeFirstUp));
        Assert.False(releaseBeforeFirstUp);

        scheduler.RecordPosition(new Vector2(50f, 50f));
        Assert.True(scheduler.ShouldEmitButtonEvent(0, down: true, out bool releaseBeforeSecondDown));
        Assert.True(releaseBeforeSecondDown);

        int[] releases = new int[ImGuiMouseReleaseScheduler.MaximumMouseButtons];
        Assert.Equal(0, scheduler.BeginFrame(releases));
        Assert.Equal(0, scheduler.BeginFrame(releases));
        Assert.Equal(0, scheduler.BeginFrame(releases));
    }
}
