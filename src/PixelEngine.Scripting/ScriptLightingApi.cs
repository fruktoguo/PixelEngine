namespace PixelEngine.Scripting;

/// <summary>
/// 脚本光照请求缓冲；脚本写入 reveal 与点光源，Hosting/Rendering 在安全相位消费。
/// </summary>
public sealed class ScriptLightingApi : ILightingApi
{
    private readonly List<ScriptPointLight> _pointLights = [];
    private readonly List<FogRevealRequest> _reveals = [];

    /// <inheritdoc />
    public int PointLightCount => _pointLights.Count;

    /// <inheritdoc />
    public int RevealCount => _reveals.Count;

    /// <summary>
    /// 当前待消费的完整视口 reveal 强度；0 表示本帧未请求。
    /// </summary>
    public byte ViewportRevealAlpha { get; private set; }

    /// <inheritdoc />
    public void RevealAround(float x, float y, float radius, byte alpha = byte.MaxValue)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        ValidatePositive(radius, nameof(radius));
        _reveals.Add(new FogRevealRequest(x, y, radius, alpha));
    }

    /// <inheritdoc />
    public void RevealViewport(byte alpha = byte.MaxValue)
    {
        if (alpha > ViewportRevealAlpha)
        {
            ViewportRevealAlpha = alpha;
        }
    }

    /// <inheritdoc />
    public void AddPointLight(float x, float y, float radius, uint colorBgra, float intensity = 1f)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        ValidatePositive(radius, nameof(radius));
        ValidateFinite(intensity, nameof(intensity));
        if (intensity < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "光照强度不能为负。");
        }

        _pointLights.Add(new ScriptPointLight(x, y, radius, colorBgra, intensity));
    }

    /// <inheritdoc />
    public ScriptPointLight GetPointLight(int index)
    {
        return (uint)index < (uint)_pointLights.Count
            ? _pointLights[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, "点光源索引越界。");
    }

    /// <inheritdoc />
    public FogRevealRequest GetReveal(int index)
    {
        return (uint)index < (uint)_reveals.Count
            ? _reveals[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, "fog reveal 索引越界。");
    }

    /// <inheritdoc />
    public void ClearPointLights()
    {
        _pointLights.Clear();
    }

    /// <summary>
    /// 清空 fog reveal 请求；通常在切换场景或重建 fog buffer 时调用。
    /// </summary>
    public void ClearReveals()
    {
        _reveals.Clear();
        ViewportRevealAlpha = 0;
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "光照参数必须为有限值。");
        }
    }

    private static void ValidatePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(name, value, "光照参数必须为有限正数。");
        }
    }
}
