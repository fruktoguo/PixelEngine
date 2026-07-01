using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 脚本相机到 Rendering/World 的同步测试。
/// </summary>
public sealed class ScriptCameraSynchronizerTests
{
    /// <summary>
    /// 验证脚本相机快照可转换为 Rendering CameraState。
    /// </summary>
    [Fact]
    public void SynchronizerPublishesRenderingCameraState()
    {
        ScriptCameraApi camera = new(viewportWidth: 100, viewportHeight: 50, centerX: 200, centerY: 100, zoom: 2);
        ScriptCameraSynchronizer synchronizer = new(camera);

        CameraState state = synchronizer.Sync(viewportWidth: 120, viewportHeight: 60);

        Assert.Equal(120, camera.ViewportWidth);
        Assert.Equal(60, camera.ViewportHeight);
        Assert.Equal(170f, state.OriginWorldX);
        Assert.Equal(85f, state.OriginWorldY);
        Assert.Equal(0.5f, state.CellsPerPixel);
        Assert.Equal(120, state.ViewportWidth);
        Assert.Equal(60, state.ViewportHeight);
        Assert.Equal(state, synchronizer.Current);
    }

    /// <summary>
    /// 验证同步器会用脚本相机中心与 cell 视口更新 World residency 相机。
    /// </summary>
    [Fact]
    public void SynchronizerUpdatesWorldCameraFocusAndViewport()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pixelengine-camera-world-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty));
            WorldManager world = new(
                new WorldCamera(0, 0, 1, 1),
                new TemperatureField(),
                materials,
                path,
                fallbackMaterialId: 0);
            ScriptCameraApi camera = new(viewportWidth: 100, viewportHeight: 50, centerX: 200, centerY: 100, zoom: 2);
            ScriptCameraSynchronizer synchronizer = new(camera, world);

            _ = synchronizer.Sync();

            Assert.Equal(200, world.Camera.FocusX);
            Assert.Equal(100, world.Camera.FocusY);
            Assert.Equal(50, world.Camera.ViewportCellsX);
            Assert.Equal(25, world.Camera.ViewportCellsY);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }
}
