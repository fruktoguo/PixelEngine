using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using Silk.NET.Input;

namespace PixelEngine.Demo;

/// <summary>
/// 在真实窗口相位观察原始 Silk 输入、UI 仲裁、RmlUi 事件与 Demo 屏栈；不注入或改写任何输入。
/// </summary>
internal sealed class DemoPhysicalUiInputProbe : IEnginePhaseDriver
{
    private readonly EngineProbeApi _probe;
    private readonly RenderWindow _window;
    private readonly long _initialDrainedEvents;
    private bool _previousLeftDown;

    public DemoPhysicalUiInputProbe(EngineProbeApi probe, RenderWindow window)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _probe.EnablePhysicalUiInputDiagnostics();
        _initialDrainedEvents = _probe.CapturePhysicalUiInput().TotalDrainedEventCount;
    }

    public long FramesObserved { get; private set; }

    public long RawPointerFrames { get; private set; }

    public long RawLeftDownFrames { get; private set; }

    public long RawLeftPressEdges { get; private set; }

    public long RawLeftReleaseEdges { get; private set; }

    public long RuntimeGuiMouseCaptureFrames { get; private set; }

    public long GameUiMouseCaptureFrames { get; private set; }

    public float LastFramebufferX { get; private set; }

    public float LastFramebufferY { get; private set; }

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
        if (_window.Input.Mice.Count > 0)
        {
            IMouse mouse = _window.Input.Mice[0];
            RawPointerFrames++;
            LastFramebufferX = mouse.Position.X * _window.FramebufferScaleX;
            LastFramebufferY = mouse.Position.Y * _window.FramebufferScaleY;
            bool leftDown = mouse.IsButtonPressed(MouseButton.Left);
            if (leftDown)
            {
                RawLeftDownFrames++;
            }

            if (leftDown != _previousLeftDown)
            {
                if (leftDown)
                {
                    RawLeftPressEdges++;
                }
                else
                {
                    RawLeftReleaseEdges++;
                }

                _previousLeftDown = leftDown;
            }
        }

        PhysicalUiInputProbeSnapshot input = _probe.CapturePhysicalUiInput();
        if (input.GuiCapture.WantCaptureMouse)
        {
            RuntimeGuiMouseCaptureFrames++;
        }

        if (input.Capture.WantCaptureMouse)
        {
            GameUiMouseCaptureFrames++;
        }
    }

    private static string Normalize(string value)
    {
        return value.Replace(',', ';').Replace('\r', ' ').Replace('\n', ' ');
    }
}
