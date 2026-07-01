using PixelEngine.Core.Time;

namespace PixelEngine.Scripting;

/// <summary>
/// 将 Hosting/Core 的固定步长帧时钟暴露为脚本可读时间 facade。
/// </summary>
/// <param name="clock">引擎帧时钟。</param>
public sealed class ScriptFrameTime(FrameClock clock) : IGameTime
{
    private readonly FrameClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public float DeltaTime => (float)(_clock.Dt * _clock.TimeScale);

    /// <inheritdoc />
    public float FixedStep => (float)_clock.Dt;

    /// <inheritdoc />
    public long FrameCount => _clock.FrameIndex;

    /// <inheritdoc />
    public float TimeScale => (float)_clock.TimeScale;

    /// <inheritdoc />
    public bool SimSteppedThisFrame => _clock.RunSimThisFrame;
}
