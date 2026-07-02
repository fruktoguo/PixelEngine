using PixelEngine.Core.Events;
using PixelEngine.Hosting;
using PixelEngine.Simulation;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 窗口音频探针，在真实窗口相位中注入材质音频事件并采样派发诊断。
/// </summary>
internal sealed class DemoAudioProbe(MaterialTable materials) : IEnginePhaseDriver
{
    private const int StressEventCount = 64;

    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private ushort _stone;
    private ushort _water;
    private ushort _lava;
    private int _frames;

    /// <summary>
    /// 探针是否已解析所需材质并开始运行。
    /// </summary>
    public bool Initialized { get; private set; }

    /// <summary>
    /// 是否成功把全部探针事件写入音频事件 ring。
    /// </summary>
    public bool Enqueued { get; private set; }

    /// <summary>
    /// 高密度限流样本成功写入的事件数量。
    /// </summary>
    public int StressEnqueued { get; private set; }

    /// <summary>
    /// 窗口短跑中观测到的最大音频事件排空数。
    /// </summary>
    public long MaxDrained { get; private set; }

    /// <summary>
    /// 窗口短跑中观测到的最大音频事件合并数。
    /// </summary>
    public long MaxCoalesced { get; private set; }

    /// <summary>
    /// 窗口短跑中观测到的最大音频限流 / 丢弃数。
    /// </summary>
    public long MaxDropped { get; private set; }

    /// <summary>
    /// 窗口短跑中观测到的最大成功播放 one-shot 材质事件数。
    /// </summary>
    public long MaxPlayed { get; private set; }

    /// <summary>
    /// 窗口短跑中观测到的最大活跃定位 voice 数。
    /// </summary>
    public long MaxActiveVoices { get; private set; }

    /// <summary>
    /// 窗口短跑中观测到的最大活跃 ambient voice 数。
    /// </summary>
    public long MaxActiveAmbientVoices { get; private set; }

    /// <summary>
    /// one-shot 材质事件是否至少成功播放一次。
    /// </summary>
    public bool OneShotPlayed => MaxPlayed > 0;

    /// <summary>
    /// ambient loop 是否至少被激活一次。
    /// </summary>
    public bool AmbientActivated => MaxActiveAmbientVoices > 0;

    /// <summary>
    /// 高密度 splash 事件是否触发合并 / 限流。
    /// </summary>
    public bool Limited => StressEnqueued == StressEventCount && MaxDropped > 0;

    /// <inheritdoc />
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.GameLogicAndScripts, Enqueue);
        phases.Register(EnginePhase.BuildRenderBuffer, Capture);
    }

    private void Enqueue(EngineTickContext context)
    {
        if (!Initialized)
        {
            _stone = ResolveMaterial("stone");
            _water = ResolveMaterial("water");
            _lava = ResolveMaterial("lava");
            Initialized = _stone != 0 && _water != 0 && _lava != 0;
        }

        if (!Initialized)
        {
            return;
        }

        int frame = _frames++;
        if (frame == 0)
        {
            EnqueuePlayableSample(context.Context.Events.Channel<AudioEvent>());
            return;
        }

        if (frame == 1)
        {
            EnqueueStressSample(context.Context.Events.Channel<AudioEvent>());
        }
    }

    private void EnqueuePlayableSample(MpscRingBuffer<AudioEvent> channel)
    {
        Enqueued =
            channel.TryEnqueue(new AudioEvent(AudioEventType.Explosion, 320, 180, _stone, 1f)) &&
            channel.TryEnqueue(new AudioEvent(AudioEventType.LiquidSplash, 336, 180, _water, 0.8f)) &&
            channel.TryEnqueue(new AudioEvent(AudioEventType.AmbientRegion, 352, 180, _lava, 0.9f));
    }

    private void EnqueueStressSample(MpscRingBuffer<AudioEvent> channel)
    {
        for (int i = 0; i < StressEventCount; i++)
        {
            int x = 32 + (i * 24);
            int y = 96 + (i % 8 * 24);
            if (channel.TryEnqueue(new AudioEvent(AudioEventType.LiquidSplash, x, y, _water, 0.75f)))
            {
                StressEnqueued++;
            }
        }
    }

    private void Capture(EngineTickContext context)
    {
        MaxDrained = Math.Max(MaxDrained, context.Context.Counters.AudioDrained);
        MaxCoalesced = Math.Max(MaxCoalesced, context.Context.Counters.AudioCoalesced);
        MaxDropped = Math.Max(MaxDropped, context.Context.Counters.AudioDropped);
        MaxPlayed = Math.Max(MaxPlayed, context.Context.Counters.AudioPlayed);
        MaxActiveVoices = Math.Max(MaxActiveVoices, context.Context.Counters.AudioActiveVoices);
        MaxActiveAmbientVoices = Math.Max(MaxActiveAmbientVoices, context.Context.Counters.AudioActiveAmbientVoices);
    }

    private ushort ResolveMaterial(string name)
    {
        return _materials.TryGetId(name, out ushort id) ? id : (ushort)0;
    }
}
