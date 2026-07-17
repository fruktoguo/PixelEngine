using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;

namespace PixelEngine.Demo;

/// <summary>
/// 在真实窗口相位观察原始 Silk 输入、UI 仲裁、RmlUi 事件与 Demo 屏栈；不注入或改写任何输入。
/// </summary>
internal sealed class DemoPhysicalUiInputProbe : IEnginePhaseDriver
{
    private const long StabilizationFramesAfterAction = 30;
    private readonly EngineProbeApi _probe;
    private readonly RenderWindow _window;
    private readonly long _initialDrainedEvents;
    private readonly PhysicalPointerInputDiagnostics _initialPointer;
    private readonly string _readyFilePath;
    private bool _readyFilePublished;
    private long _actionObservedFrame = -1;

    public DemoPhysicalUiInputProbe(
        EngineProbeApi probe,
        RenderWindow window,
        string readyFilePath = "")
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _readyFilePath = string.IsNullOrWhiteSpace(readyFilePath)
            ? string.Empty
            : Path.GetFullPath(readyFilePath);
        _probe.EnablePhysicalUiInputDiagnostics();
        PhysicalUiInputProbeSnapshot initial = _probe.CapturePhysicalUiInput();
        _initialDrainedEvents = initial.TotalDrainedEventCount;
        _initialPointer = initial.Pointer;
    }

    public long FramesObserved { get; private set; }

    public long RawPointerFrames { get; private set; }

    public long RawLeftDownFrames { get; private set; }

    public long RawLeftPressEdges { get; private set; }

    public long RawLeftReleaseEdges { get; private set; }

    public long RuntimeGuiMouseCaptureFrames { get; private set; }

    public long GameUiMouseCaptureFrames { get; private set; }

    public bool ShouldComplete =>
        ShouldStopAfterAction(FramesObserved, _actionObservedFrame);

    public float LastFramebufferX { get; private set; }

    public float LastFramebufferY { get; private set; }

    public float FirstPressFramebufferX { get; private set; }

    public float FirstPressFramebufferY { get; private set; }

    public float LastPressFramebufferX { get; private set; }

    public float LastPressFramebufferY { get; private set; }

    public float FirstReleaseFramebufferX { get; private set; }

    public float FirstReleaseFramebufferY { get; private set; }

    public float LastReleaseFramebufferX { get; private set; }

    public float LastReleaseFramebufferY { get; private set; }

    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        // AttachWindowRuntime 已先注册 SilkInputPhaseDriver；同相位后注册可观察完成 DoEvents 与 UI Pump 的结果。
        phases.Register(EnginePhase.InputAndTime, CaptureInput);
    }

    public string BuildSummary()
    {
        GameUiDemoController? controller = null;
        if (_probe.TryGetScriptScene(out PixelEngine.Scripting.Scene? scene))
        {
            _ = scene.TryGetFirstComponent(out controller);
        }

        PhysicalUiInputProbeSnapshot input = _probe.CapturePhysicalUiInput();
        long drainedEvents = Math.Max(0, input.TotalDrainedEventCount - _initialDrainedEvents);
        GameUiCanvasInputDiagnostics canvas = input.Canvas;
        GuiButtonInputDiagnostics guiButtons = input.GuiButtons;
        string fault = controller?.LastException is Exception exception
            ? Normalize(exception.GetType().Name + ":" + exception.Message)
            : "none";
        return
            $"physical_ui_input_probe schema=pixelengine.physical-ui-input/v1, " +
            $"frames={FramesObserved}, raw_pointer_frames={RawPointerFrames}, " +
            $"raw_left_down_frames={RawLeftDownFrames}, raw_press_edges={RawLeftPressEdges}, " +
            $"raw_release_edges={RawLeftReleaseEdges}, " +
            $"pointer_pending={input.Pointer.PendingTransitions}, " +
            $"pointer_coalesced={input.Pointer.CoalescedTransitions - _initialPointer.CoalescedTransitions}, " +
            $"first_press={FirstPressFramebufferX:0.###}:{FirstPressFramebufferY:0.###}, " +
            $"last_press={LastPressFramebufferX:0.###}:{LastPressFramebufferY:0.###}, " +
            $"first_release={FirstReleaseFramebufferX:0.###}:{FirstReleaseFramebufferY:0.###}, " +
            $"last_release={LastReleaseFramebufferX:0.###}:{LastReleaseFramebufferY:0.###}, " +
            $"action_observed_frame={_actionObservedFrame}, " +
            $"runtime_gui_mouse_capture_frames={RuntimeGuiMouseCaptureFrames}, " +
            $"game_ui_mouse_capture_frames={GameUiMouseCaptureFrames}, " +
            $"last_pointer={LastFramebufferX:0.###}:{LastFramebufferY:0.###}, " +
            $"canvas_pointer={canvas.PointerX:0.###}:{canvas.PointerY:0.###}, " +
            $"canvas_target={canvas.PointerTargetIndex}, canvas_capture={canvas.PointerCaptureIndex}, " +
            $"button_target={canvas.LastButtonTargetIndex}, button_canvas={canvas.LastButtonTargetCanvas}, " +
            $"button_backend={canvas.LastButtonTargetBackend}, " +
            $"button_hit={canvas.LastButtonTargetHit.HitsUi}:{canvas.LastButtonTargetHit.WantsMouse}:{canvas.LastButtonTargetHit.Opaque}, " +
            $"button_calls={canvas.PointerButtonCalls}, button_forwarded={canvas.ForwardedPointerButtonCalls}, " +
            $"canvas_press={canvas.LeftPressCalls}, canvas_release={canvas.LeftReleaseCalls}, " +
            $"gui_button_calls={guiButtons.ButtonCalls}, gui_button_hovered={guiButtons.HoveredCalls}, " +
            $"gui_button_pressed={guiButtons.PressedCalls}, gui_button_down={guiButtons.DownCalls}, " +
            $"gui_button_released={guiButtons.ReleasedCalls}, gui_button_clicked={guiButtons.ClickedCalls}, " +
            $"gui_last_hover={Normalize(guiButtons.LastHoveredLabel ?? "none")}, " +
            $"gui_hover_rect={guiButtons.LastHoveredRectMin.X:0.###}:{guiButtons.LastHoveredRectMin.Y:0.###}:" +
            $"{guiButtons.LastHoveredRectMax.X:0.###}:{guiButtons.LastHoveredRectMax.Y:0.###}, " +
            $"gui_mouse={guiButtons.LastMousePosition.X:0.###}:{guiButtons.LastMousePosition.Y:0.###}, " +
            $"drained_events={drainedEvents}, controller={(controller is null ? "missing" : "present")}, " +
            $"controller_faulted={controller?.Faulted.ToString() ?? "missing"}, " +
            $"main_screen={controller?.MainScreen.Value ?? -1}, hud_screen={controller?.HudScreenHandle.Value ?? -1}, " +
            $"modal_screen={controller?.ModalScreen.Value ?? -1}, last_action={controller?.LastAction.Value ?? -1}, " +
            $"fault={fault}";
    }

    private void CaptureInput(EngineTickContext context)
    {
        _ = context;
        FramesObserved++;
        PhysicalUiInputProbeSnapshot input = _probe.CapturePhysicalUiInput();
        PhysicalPointerInputDiagnostics pointer = input.Pointer;
        RawPointerFrames = Math.Max(0, pointer.PointerSamples - _initialPointer.PointerSamples);
        RawLeftDownFrames = Math.Max(0, pointer.LeftDownSamples - _initialPointer.LeftDownSamples);
        RawLeftPressEdges = Math.Max(0, pointer.LeftPressEdges - _initialPointer.LeftPressEdges);
        RawLeftReleaseEdges = Math.Max(0, pointer.LeftReleaseEdges - _initialPointer.LeftReleaseEdges);
        LastFramebufferX = pointer.LastPointerX;
        LastFramebufferY = pointer.LastPointerY;
        if (RawLeftPressEdges > 0)
        {
            FirstPressFramebufferX = pointer.LastPressX;
            FirstPressFramebufferY = pointer.LastPressY;
            LastPressFramebufferX = pointer.LastPressX;
            LastPressFramebufferY = pointer.LastPressY;
        }

        if (RawLeftReleaseEdges > 0)
        {
            FirstReleaseFramebufferX = pointer.LastReleaseX;
            FirstReleaseFramebufferY = pointer.LastReleaseY;
            LastReleaseFramebufferX = pointer.LastReleaseX;
            LastReleaseFramebufferY = pointer.LastReleaseY;
        }

        bool controllerAvailable = TryGetController(out GameUiDemoController? controller) &&
            controller is not null;
        if (!_readyFilePublished &&
            controllerAvailable &&
            !controller!.Faulted &&
            controller.MainScreen.Value != 0 &&
            _probe.CaptureGameUi().IsAttached)
        {
            PublishReadyFile();
        }

        if (input.GuiCapture.WantCaptureMouse)
        {
            RuntimeGuiMouseCaptureFrames++;
        }

        if (input.Capture.WantCaptureMouse)
        {
            GameUiMouseCaptureFrames++;
        }

        if (_actionObservedFrame < 0 &&
            RawLeftReleaseEdges > 0 &&
            input.TotalDrainedEventCount > _initialDrainedEvents &&
            controllerAvailable &&
            controller!.LastAction.Value != 0)
        {
            _actionObservedFrame = FramesObserved;
        }
    }

    internal static bool ShouldStopAfterAction(long framesObserved, long actionObservedFrame)
    {
        return actionObservedFrame >= 0 &&
            framesObserved >= actionObservedFrame &&
            framesObserved - actionObservedFrame >= StabilizationFramesAfterAction;
    }

    private bool TryGetController(out GameUiDemoController? controller)
    {
        controller = null;
        return _probe.TryGetScriptScene(out PixelEngine.Scripting.Scene? scene) &&
            scene is not null &&
            scene.TryGetFirstComponent(out controller);
    }

    private void PublishReadyFile()
    {
        if (!string.IsNullOrEmpty(_readyFilePath))
        {
            if (!_window.TryGetWin32WindowHandle(out IntPtr windowHandle) || windowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("物理 UI ready 握手无法取得 Player HWND。");
            }

            using FileStream readyFile = new(
                _readyFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            string payload = FormattableString.Invariant(
                $"pixelengine.physical-ui-ready/v1;hwnd={windowHandle.ToInt64()}");
            readyFile.Write(System.Text.Encoding.UTF8.GetBytes(payload));
            readyFile.Flush(flushToDisk: true);
        }

        _readyFilePublished = true;
    }

    private static string Normalize(string value)
    {
        return value.Replace(',', ';').Replace('\r', ' ').Replace('\n', ' ');
    }
}
