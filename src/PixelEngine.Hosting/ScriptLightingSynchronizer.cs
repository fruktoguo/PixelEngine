using PixelEngine.Rendering;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将脚本光照请求转换为 Rendering 可消费的点光源与 fog-of-war buffer。
/// </summary>
public sealed class ScriptLightingSynchronizer(ScriptLightingApi lighting, ScriptCameraSynchronizer camera)
{
    private readonly ScriptLightingApi _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
    private readonly ScriptCameraSynchronizer _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    private LightSource[] _pointLights = new LightSource[16];
    private int _pointLightCount;

    /// <summary>
    /// 当前帧点光源快照。
    /// </summary>
    public ReadOnlySpan<LightSource> PointLights => _pointLights.AsSpan(0, _pointLightCount);

    /// <summary>
    /// 当前 fog-of-war reveal buffer。
    /// </summary>
    public FogOfWarBuffer FogOfWar { get; private set; } = new(1, 1);

    /// <summary>
    /// 消费脚本光照请求并更新 Rendering 快照。
    /// </summary>
    public void Sync()
    {
        CameraState camera = _camera.Current;
        EnsureFogSize(camera);
        CopyPointLights(camera);
        ApplyFogReveals(camera);
    }

    private void CopyPointLights(CameraState camera)
    {
        int count = _lighting.PointLightCount;
        EnsureLightCapacity(count);
        for (int i = 0; i < count; i++)
        {
            ScriptPointLight source = _lighting.GetPointLight(i);
            LightSource light = new(
                (source.X - camera.OriginWorldX) / camera.CellsPerPixel,
                (source.Y - camera.OriginWorldY) / camera.CellsPerPixel,
                source.Radius / camera.CellsPerPixel,
                source.ColorBgra,
                source.Intensity);
            light.Validate();
            _pointLights[i] = light;
        }

        if (_pointLightCount > count)
        {
            _pointLights.AsSpan(count, _pointLightCount - count).Clear();
        }

        _pointLightCount = count;
        _lighting.ClearPointLights();
    }

    private void ApplyFogReveals(CameraState camera)
    {
        int count = _lighting.RevealCount;
        for (int i = 0; i < count; i++)
        {
            FogRevealRequest reveal = _lighting.GetReveal(i);
            int localX = (int)MathF.Round((reveal.X - camera.OriginWorldX) / camera.CellsPerPixel);
            int localY = (int)MathF.Round((reveal.Y - camera.OriginWorldY) / camera.CellsPerPixel);
            int radius = Math.Max(0, (int)MathF.Ceiling(reveal.Radius / camera.CellsPerPixel));
            FogOfWar.RevealCircle(localX, localY, radius, reveal.Alpha);
        }

        _lighting.ClearReveals();
    }

    private void EnsureLightCapacity(int count)
    {
        if (_pointLights.Length >= count)
        {
            return;
        }

        int capacity = _pointLights.Length;
        while (capacity < count)
        {
            capacity *= 2;
        }

        Array.Resize(ref _pointLights, capacity);
    }

    private void EnsureFogSize(CameraState camera)
    {
        if (FogOfWar.ViewportCellWidth == camera.ViewportWidth &&
            FogOfWar.ViewportCellHeight == camera.ViewportHeight)
        {
            return;
        }

        FogOfWar = new FogOfWarBuffer(camera.ViewportWidth, camera.ViewportHeight);
    }
}
