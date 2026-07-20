using PixelEngine.Audio;
using PixelEngine.Rendering;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将脚本运行时控制请求映射到真实 Engine 执行模式与生命周期。
/// </summary>
/// <param name="engine">运行时引擎。</param>
public sealed class EngineScriptRuntimeControlApi(Engine engine) : IRuntimeControlApi
{
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private AudioSettings? _lastAudibleSettings;

    /// <summary>
    /// 捕获当前 Engine 运行模式、关闭请求、sim 频率与帧号。
    /// </summary>
    /// <returns>脚本可读的运行控制快照。</returns>
    public RuntimeControlSnapshot Capture()
    {
        return new RuntimeControlSnapshot(
            _engine.Mode == EngineExecutionMode.Play,
            _engine.IsShutdownRequested,
            _engine.RequestedSimHz,
            _engine.Context.Clock.FrameIndex,
            _engine.Context.TryGetService(out PixelEngine.Simulation.SimulationKernel kernel) ? kernel.WorldSeed : 0,
            _engine.LastRuntimeRestartStatus,
            _engine.LastRuntimeRestartMessage);
    }

    /// <summary>
    /// 切换到 Edit 模式，暂停 sim/physics 推进但保留渲染与 GUI。
    /// </summary>
    public void PauseSimulation()
    {
        _engine.EnterEditMode();
    }

    /// <summary>
    /// 切换到 Play 模式，恢复 sim/physics 推进。
    /// </summary>
    public void ResumeSimulation()
    {
        _engine.EnterPlayMode();
    }

    /// <summary>
    /// 请求 Engine 在当前 tick 结束后关闭。
    /// </summary>
    /// <returns>关闭请求结果。</returns>
    public RuntimeControlResult RequestShutdown()
    {
        _engine.RequestShutdown();
        return new RuntimeControlResult(true, "已请求关闭。");
    }

    /// <summary>
    /// 请求显示已启用的内嵌 Editor dockspace。
    /// </summary>
    /// <returns>打开 Editor 的结果。</returns>
    public RuntimeControlResult OpenEditor()
    {
        return _engine.OpenEditor();
    }

    /// <summary>
    /// 请求重开当前关卡；Hosting 会恢复首次脚本 tick 后捕获的 world/script 运行基线。
    /// </summary>
    /// <returns>重开请求结果。</returns>
    public RuntimeControlResult RequestRestartCurrentScene()
    {
        return _engine.RequestRestartCurrentSceneAtSafePoint();
    }

    /// <summary>
    /// 以指定 seed 重建当前流式程序化场景，并恢复脚本运行基线。
    /// </summary>
    /// <param name="worldSeed">新程序化世界 seed。</param>
    /// <returns>重建请求结果。</returns>
    public RuntimeControlResult RequestRestartCurrentProceduralWorld(ulong worldSeed)
    {
        return _engine.RequestRestartCurrentProceduralWorldAtSafePoint(worldSeed);
    }

    /// <summary>
    /// 捕获当前运行态设置开关快照。
    /// </summary>
    /// <returns>包含 VSync、音频开关及其可切换能力的脚本可读快照。</returns>
    public RuntimeSettingsSnapshot CaptureSettings()
    {
        bool canToggleVSync = _engine.Context.TryGetService(out IRenderPresentationControl presentation) && presentation.CanToggleVSync;
        bool vSyncEnabled = _engine.Context.TryGetService(out IRenderPresentationControl presentationState)
            ? presentationState.VSyncEnabled
            : _engine.Context.Options.VSync;
        bool canToggleAudio = _engine.Context.TryGetService(out AudioSystem audio);
        bool audioEnabled = canToggleAudio && IsAudioEnabled(audio.Settings);
        return new RuntimeSettingsSnapshot(vSyncEnabled, canToggleVSync, audioEnabled, canToggleAudio);
    }

    /// <summary>
    /// 请求在运行时切换窗口 present 的 VSync 状态。
    /// </summary>
    /// <param name="enabled">为 true 时开启 VSync；为 false 时关闭。</param>
    /// <returns>切换结果和可显示诊断消息。</returns>
    public RuntimeControlResult SetVSyncEnabled(bool enabled)
    {
        if (!_engine.Context.TryGetService(out IRenderPresentationControl presentation))
        {
            return new RuntimeControlResult(false, "当前宿主未接入窗口 present 控制。");
        }

        if (!presentation.CanToggleVSync)
        {
            return new RuntimeControlResult(false, "当前渲染后端不支持运行时切换 VSync。");
        }

        presentation.VSyncEnabled = enabled;
        _engine.Context.Counters.VSyncEnabled = enabled;
        return new RuntimeControlResult(true, enabled ? "VSync 已开启。" : "VSync 已关闭。");
    }

    /// <summary>
    /// 请求在运行时启用或静音音频系统。
    /// </summary>
    /// <param name="enabled">为 true 时恢复可听音量；为 false 时静音 master 音量。</param>
    /// <returns>切换结果和可显示诊断消息。</returns>
    public RuntimeControlResult SetAudioEnabled(bool enabled)
    {
        if (!_engine.Context.TryGetService(out AudioSystem audio))
        {
            return new RuntimeControlResult(false, "当前宿主未接入音频系统。");
        }

        if (enabled)
        {
            AudioSettings restored = _lastAudibleSettings is null || !IsAudioEnabled(_lastAudibleSettings)
                ? CloneSettings(audio.Settings)
                : CloneSettings(_lastAudibleSettings);
            if (!IsAudioEnabled(restored))
            {
                restored.MasterVolume = 1f;
                if (restored.SfxVolume <= 0f && restored.UiVolume <= 0f && restored.AmbientVolume <= 0f)
                {
                    restored.SfxVolume = 1f;
                    restored.UiVolume = 1f;
                    restored.AmbientVolume = 1f;
                }
            }

            audio.ApplySettings(restored);
            return new RuntimeControlResult(true, "音频已开启。");
        }

        AudioSettings current = audio.Settings;
        if (IsAudioEnabled(current))
        {
            _lastAudibleSettings = CloneSettings(current);
        }

        AudioSettings muted = CloneSettings(current);
        muted.MasterVolume = 0f;
        audio.ApplySettings(muted);
        return new RuntimeControlResult(true, "音频已关闭。");
    }

    private static bool IsAudioEnabled(AudioSettings settings)
    {
        return settings.MasterVolume > 0f &&
            (settings.SfxVolume > 0f || settings.UiVolume > 0f || settings.AmbientVolume > 0f);
    }

    private static AudioSettings CloneSettings(AudioSettings settings)
    {
        return new AudioSettings
        {
            MaxVoices = settings.MaxVoices,
            MaxAmbientVoices = settings.MaxAmbientVoices,
            PixelsPerMeter = settings.PixelsPerMeter,
            ListenerDepth = settings.ListenerDepth,
            MasterVolume = settings.MasterVolume,
            SfxVolume = settings.SfxVolume,
            UiVolume = settings.UiVolume,
            AmbientVolume = settings.AmbientVolume,
            ReferenceDistance = settings.ReferenceDistance,
            MaxDistance = settings.MaxDistance,
            RolloffFactor = settings.RolloffFactor,
            MaxDrainedAudioEventsPerFrame = settings.MaxDrainedAudioEventsPerFrame,
            MaxParticleImpactEventsPerFrame = settings.MaxParticleImpactEventsPerFrame,
            MaxFireCrackleEventsPerFrame = settings.MaxFireCrackleEventsPerFrame,
            MaxLiquidSplashEventsPerFrame = settings.MaxLiquidSplashEventsPerFrame,
            MaxExplosionEventsPerFrame = settings.MaxExplosionEventsPerFrame,
            MaxRigidbodyShatterEventsPerFrame = settings.MaxRigidbodyShatterEventsPerFrame,
            MaxAmbientRegionEventsPerFrame = settings.MaxAmbientRegionEventsPerFrame,
            CoalesceBucketSize = settings.CoalesceBucketSize,
            DefaultCooldownTicks = settings.DefaultCooldownTicks,
            AmbientEnterThreshold = settings.AmbientEnterThreshold,
            AmbientExitThreshold = settings.AmbientExitThreshold,
            AmbientFadeRate = settings.AmbientFadeRate,
            CooldownTableCapacity = settings.CooldownTableCapacity,
        };
    }
}
