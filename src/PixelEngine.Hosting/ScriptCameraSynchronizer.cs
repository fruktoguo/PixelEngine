using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.World;

namespace PixelEngine.Hosting;

/// <summary>
/// 将脚本相机快照同步为 Rendering 可消费的 CameraState，并可选驱动 World residency 相机。
/// </summary>
public sealed class ScriptCameraSynchronizer(ScriptCameraApi camera, WorldManager? world = null)
{
    private readonly ScriptCameraApi _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly WorldManager? _world = world;

    /// <summary>
    /// 最新 Rendering 相机快照。
    /// </summary>
    public CameraState Current { get; private set; } = ToCameraState(camera.Snapshot());

    /// <summary>
    /// 从脚本相机读取快照，并同步到 Rendering/World 状态。
    /// </summary>
    /// <param name="viewportWidth">可选窗口视口宽度；大于 0 时回写脚本相机。</param>
    /// <param name="viewportHeight">可选窗口视口高度；大于 0 时回写脚本相机。</param>
    /// <returns>最新 Rendering 相机快照。</returns>
    public CameraState Sync(int viewportWidth = 0, int viewportHeight = 0)
    {
        if (viewportWidth > 0 && viewportHeight > 0)
        {
            _camera.SetViewport(viewportWidth, viewportHeight);
        }

        CameraSnapshot snapshot = _camera.Snapshot();
        CameraState state = ToCameraState(snapshot);
        Current = state;
        SyncWorld(state);
        return state;
    }

    private void SyncWorld(CameraState state)
    {
        if (_world is null)
        {
            return;
        }

        float widthCells = state.ViewportWidth * state.CellsPerPixel;
        float heightCells = state.ViewportHeight * state.CellsPerPixel;
        long centerX = (long)MathF.Round(state.OriginWorldX + (widthCells * 0.5f));
        long centerY = (long)MathF.Round(state.OriginWorldY + (heightCells * 0.5f));
        _world.UpdateCamera(centerX, centerY);
        _world.Camera.SetViewport(
            Math.Max(1, (int)MathF.Ceiling(widthCells)),
            Math.Max(1, (int)MathF.Ceiling(heightCells)));
    }

    private static CameraState ToCameraState(CameraSnapshot snapshot)
    {
        return new CameraState(
            snapshot.OriginWorldX,
            snapshot.OriginWorldY,
            snapshot.CellsPerPixel,
            snapshot.ViewportWidth,
            snapshot.ViewportHeight);
    }
}
