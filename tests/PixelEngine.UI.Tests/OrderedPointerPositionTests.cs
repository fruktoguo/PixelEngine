using Hexa.NET.ImGui;
using PixelEngine.Gui;
using System.Numerics;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class OrderedPointerPositionTests
{
    [Fact]
    public void DearImGuiEnablesInputTrickleQueueByDefault()
    {
        ImGuiContextPtr context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(context);

            Assert.True(ImGui.GetIO().ConfigInputTrickleEventQueue);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void DearImGuiTricklesCompleteDragSequenceAcrossFrames()
    {
        ImGuiContextPtr context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(context);
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(3840f, 2032f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            Vector2 dragStart = new(2081f, 1096f);
            Vector2 dragTarget = new(1366f, 704f);
            ImGui.AddMousePosEvent(io, dragStart.X, dragStart.Y);
            ImGui.AddMouseButtonEvent(io, 0, true);
            ImGui.AddMousePosEvent(io, dragTarget.X, dragTarget.Y);
            ImGui.AddMouseButtonEvent(io, 0, false);

            ImGui.NewFrame();
            Vector2 firstFramePosition = io.MousePos;
            bool firstFrameDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            ImGui.EndFrame();

            ImGui.NewFrame();
            Vector2 secondFramePosition = io.MousePos;
            bool secondFrameReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            ImGui.EndFrame();

            Assert.Equal(dragStart, firstFramePosition);
            Assert.True(firstFrameDown);
            Assert.Equal(dragTarget, secondFramePosition);
            Assert.True(secondFrameReleased);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void ButtonBeforeAnyMoveFallsBackToCurrentDevicePosition()
    {
        OrderedPointerPosition tracker = new();
        Vector2 fallback = new(17f, 23f);

        Vector2 resolved = tracker.ResolveButtonPosition(fallback);

        Assert.Equal(fallback, resolved);
        Assert.True(tracker.HasPosition);
    }

    [Fact]
    public void ButtonKeepsLastDeliveredMoveInsteadOfSamplingFutureDevicePosition()
    {
        OrderedPointerPosition tracker = new();
        Vector2 dragStart = new(2081f, 1096f);
        Vector2 futureTarget = new(1366f, 704f);
        _ = tracker.RecordMove(dragStart);

        Vector2 resolved = tracker.ResolveButtonPosition(futureTarget);

        Assert.Equal(dragStart, resolved);
    }

    [Fact]
    public void LaterMoveAdvancesPositionAndFocusResetRestoresFallback()
    {
        OrderedPointerPosition tracker = new();
        Vector2 dragStart = new(100f, 200f);
        Vector2 dragTarget = new(300f, 400f);
        Vector2 refocusedPointer = new(500f, 600f);
        _ = tracker.RecordMove(dragStart);
        _ = tracker.RecordMove(dragTarget);

        Assert.Equal(dragTarget, tracker.ResolveButtonPosition(new Vector2(999f, 999f)));

        tracker.Reset();

        Assert.Equal(refocusedPointer, tracker.ResolveButtonPosition(refocusedPointer));
    }
}
