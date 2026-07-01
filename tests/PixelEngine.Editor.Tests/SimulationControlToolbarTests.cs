using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// sim 控制条测试。
/// </summary>
public sealed class SimulationControlToolbarTests
{
    /// <summary>
    /// 验证控制条只调用运行时控制服务，不自行推进 sim。
    /// </summary>
    [Fact]
    public void ToolbarDelegatesPlayPauseStepAndFrequencyToControlService()
    {
        RecordingControlService control = new();
        SimulationControlToolbar toolbar = new(control);

        toolbar.Play();
        toolbar.Pause();
        toolbar.Use30Hz();
        toolbar.StepOnce();
        toolbar.Use60Hz();

        Assert.Equal(["play", "pause", "hz:30", "step", "hz:60"], control.Calls);
        Assert.Equal(1, control.SimTicks);
        Assert.Equal(60, toolbar.LastSnapshot.SimHz);
    }

    private sealed class RecordingControlService : ISimulationControlService
    {
        private bool _playing;
        private double _simHz = 60;
        private long _frame;

        public List<string> Calls { get; } = [];

        public long SimTicks { get; private set; }

        public SimulationControlSnapshot Capture()
        {
            return new SimulationControlSnapshot(_playing, _simHz, _frame, SimTicks, RunSimThisFrame: false);
        }

        public void EnterPlayMode()
        {
            Calls.Add("play");
            _playing = true;
        }

        public void EnterEditMode()
        {
            Calls.Add("pause");
            _playing = false;
        }

        public void StepOnce()
        {
            Calls.Add("step");
            _frame++;
            SimTicks++;
        }

        public void SetSimHz(double simHz)
        {
            Calls.Add($"hz:{simHz:F0}");
            _simHz = simHz;
        }
    }
}
