using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 把 Demo 专项负载计数发布到引擎 counters，供性能 HUD 与窗口 profiling 读取。
/// </summary>
internal sealed class DemoLoadCountersPhaseDriver(EngineProbeApi probe) : IEnginePhaseDriver
{
    private readonly EngineProbeApi _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private RisingHazardDirector? _risingHazard;
    private int _framesUntilRescan;

    /// <inheritdoc />
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.GameLogicAndScripts, Publish);
    }

    private void Publish(EngineTickContext context)
    {
        RisingHazardDirector? hazard = ResolveRisingHazard();
        context.Context.Counters.SetCustomMetric("lava_active_area_cells", hazard?.ActiveAreaCells ?? 0);
    }

    private RisingHazardDirector? ResolveRisingHazard()
    {
        if (_risingHazard is not null)
        {
            return _risingHazard;
        }

        if (_framesUntilRescan > 0)
        {
            _framesUntilRescan--;
            return null;
        }

        ScriptEntityInspection[] entities = _probe.ScriptScene.CaptureInspectionSnapshot();
        for (int i = 0; i < entities.Length; i++)
        {
            ScriptComponentInspection[] components = entities[i].Components;
            for (int j = 0; j < components.Length; j++)
            {
                if (components[j].Behaviour is RisingHazardDirector hazard)
                {
                    _risingHazard = hazard;
                    return hazard;
                }
            }
        }

        _framesUntilRescan = 30;
        return null;
    }
}
