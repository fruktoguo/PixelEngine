using PixelEngine.Core;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Editor;

/// <summary>
/// 生成 editor 调试叠层命令，并为 Rendering 提供逐 cell debug 着色。
/// </summary>
public sealed class DebugOverlayController(DebugOverlaySettings settings, IRigidCellOwnershipLookup? rigidOwnership = null) : IDebugCellColorProvider
{
    private const uint DirtyCurrentColor = 0xD000FFFFu;
    private const uint DirtyWorkingColor = 0xD0FFAA00u;
    private const uint KeepAliveColor = 0xA00000FFu;
    private const uint ParticleColor = 0xD0FFFFFFu;
    private const float ParticleTrailScale = 3f;
    private readonly uint[] _chunkParityColors =
    [
        0xC000FF00u,
        0xC0FFFF00u,
        0xC0FF00FFu,
        0xC000FFFFu,
    ];

    private readonly IRigidCellOwnershipLookup? _rigidOwnership = rigidOwnership;

    /// <summary>
    /// 当前叠层设置。
    /// </summary>
    public DebugOverlaySettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>
    /// 构建屏幕空间矢量 overlay 命令。
    /// </summary>
    public int BuildVectorOverlays(
        IChunkSource chunks,
        CameraState camera,
        ReadOnlySpan<BoundaryWakeSnapshot> keepAlive,
        ReadOnlySpan<Particle> particles,
        ReadOnlySpan<ConnectedComponentDebugSnapshot> connectedComponents,
        ICollection<OverlayCommand> destination)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(destination);
        ValidateCamera(camera);
        int before = destination.Count;
        if (Settings.IsEnabled(DebugOverlayFlags.ChunkGridParity))
        {
            AppendChunkGrid(chunks, camera, destination);
        }

        if (Settings.IsEnabled(DebugOverlayFlags.DirtyRects))
        {
            AppendDirtyRects(chunks, camera, destination);
        }

        if (Settings.IsEnabled(DebugOverlayFlags.KeepAliveHotspots))
        {
            AppendKeepAlive(camera, keepAlive, destination);
        }

        if (Settings.IsEnabled(DebugOverlayFlags.ParticleTrails))
        {
            AppendParticles(camera, particles, destination);
        }

        if (Settings.IsEnabled(DebugOverlayFlags.ConnectedComponents))
        {
            AppendConnectedComponents(camera, connectedComponents, destination);
        }

        return destination.Count - before;
    }

    /// <inheritdoc />
    public bool TryGetDebugColor(int worldX, int worldY, ushort materialId, byte flags, float temperatureCelsius, out uint colorBgra)
    {
        if (Settings.IsEnabled(DebugOverlayFlags.OwnedByBody) && CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            int bodyKey = _rigidOwnership is not null && _rigidOwnership.TryGetBodyAtCell(worldX, worldY, out int resolved)
                ? resolved
                : materialId;
            colorBgra = BodyColor(bodyKey);
            return true;
        }

        if (Settings.IsEnabled(DebugOverlayFlags.TemperatureHeatmap) && temperatureCelsius != 0f)
        {
            colorBgra = TemperatureColor(temperatureCelsius);
            return true;
        }

        if (Settings.IsEnabled(DebugOverlayFlags.CellParity) && materialId != 0)
        {
            colorBgra = CellFlags.HasParity(flags) ? 0xC000FF00u : 0xC0FF0000u;
            return true;
        }

        colorBgra = 0;
        return false;
    }

    private void AppendDirtyRects(IChunkSource chunks, CameraState camera, ICollection<OverlayCommand> destination)
    {
        foreach (Chunk chunk in chunks.ResidentChunks)
        {
            AppendDirtyRect(chunk.Coord, chunk.CurrentDirty, camera, DirtyCurrentColor, destination);
            AppendDirtyRect(chunk.Coord, chunk.WorkingDirty, camera, DirtyWorkingColor, destination);
        }
    }

    private void AppendDirtyRect(ChunkCoord coord, DirtyRect rect, CameraState camera, uint color, ICollection<OverlayCommand> destination)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        int worldX = (coord.X << EngineConstants.ChunkSizeLog2) + rect.MinX;
        int worldY = (coord.Y << EngineConstants.ChunkSizeLog2) + rect.MinY;
        AppendOutline(worldX, worldY, rect.MaxX - rect.MinX + 1, rect.MaxY - rect.MinY + 1, camera, 1f, color, destination);
    }

    private void AppendChunkGrid(IChunkSource chunks, CameraState camera, ICollection<OverlayCommand> destination)
    {
        foreach (Chunk chunk in chunks.ResidentChunks)
        {
            int parity = ((chunk.Coord.Y & 1) << 1) | (chunk.Coord.X & 1);
            AppendOutline(
                chunk.Coord.X << EngineConstants.ChunkSizeLog2,
                chunk.Coord.Y << EngineConstants.ChunkSizeLog2,
                EngineConstants.ChunkSize,
                EngineConstants.ChunkSize,
                camera,
                1f,
                _chunkParityColors[parity],
                destination);
        }
    }

    private void AppendKeepAlive(CameraState camera, ReadOnlySpan<BoundaryWakeSnapshot> keepAlive, ICollection<OverlayCommand> destination)
    {
        for (int i = 0; i < keepAlive.Length; i++)
        {
            BoundaryWakeSnapshot wake = keepAlive[i];
            if (wake.Rect.IsEmpty)
            {
                continue;
            }

            int worldX = (wake.TargetCoord.X << EngineConstants.ChunkSizeLog2) + wake.Rect.MinX;
            int worldY = (wake.TargetCoord.Y << EngineConstants.ChunkSizeLog2) + wake.Rect.MinY;
            AppendSolid(worldX, worldY, wake.Rect.MaxX - wake.Rect.MinX + 1, wake.Rect.MaxY - wake.Rect.MinY + 1, camera, KeepAliveColor, destination);
        }
    }

    private static void AppendParticles(CameraState camera, ReadOnlySpan<Particle> particles, ICollection<OverlayCommand> destination)
    {
        for (int i = 0; i < particles.Length; i++)
        {
            Particle particle = particles[i];
            float x = (particle.X - camera.OriginWorldX) / camera.CellsPerPixel;
            float y = (particle.Y - camera.OriginWorldY) / camera.CellsPerPixel;
            float vx = particle.Vx / camera.CellsPerPixel * ParticleTrailScale;
            float vy = particle.Vy / camera.CellsPerPixel * ParticleTrailScale;
            if (vx == 0f && vy == 0f)
            {
                destination.Add(OverlayCommand.SolidRectangle(x - 1f, y - 1f, 2f, 2f, ParticleColor));
                continue;
            }

            destination.Add(OverlayCommand.Line(x - vx, y - vy, x, y, 1f, ParticleColor));
        }
    }

    private static void AppendConnectedComponents(CameraState camera, ReadOnlySpan<ConnectedComponentDebugSnapshot> components, ICollection<OverlayCommand> destination)
    {
        for (int i = 0; i < components.Length; i++)
        {
            ConnectedComponentDebugSnapshot component = components[i];
            uint color = component.IsFragment ? 0xD00080FFu : BodyColor(component.BodyKey ^ component.Label);
            AppendOutline(
                component.WorldBounds.MinX,
                component.WorldBounds.MinY,
                component.WorldBounds.Width,
                component.WorldBounds.Height,
                camera,
                1f,
                color,
                destination);
        }
    }

    private static void AppendOutline(int worldX, int worldY, int width, int height, CameraState camera, float thickness, uint color, ICollection<OverlayCommand> destination)
    {
        (float x, float y, float w, float h) = WorldRectToViewport(worldX, worldY, width, height, camera);
        if (w > 0f && h > 0f)
        {
            destination.Add(OverlayCommand.OutlineRectangle(x, y, w, h, thickness, color));
        }
    }

    private static void AppendSolid(int worldX, int worldY, int width, int height, CameraState camera, uint color, ICollection<OverlayCommand> destination)
    {
        (float x, float y, float w, float h) = WorldRectToViewport(worldX, worldY, width, height, camera);
        if (w > 0f && h > 0f)
        {
            destination.Add(OverlayCommand.SolidRectangle(x, y, w, h, color));
        }
    }

    private static (float X, float Y, float Width, float Height) WorldRectToViewport(int worldX, int worldY, int width, int height, CameraState camera)
    {
        float x = (worldX - camera.OriginWorldX) / camera.CellsPerPixel;
        float y = (worldY - camera.OriginWorldY) / camera.CellsPerPixel;
        return (x, y, width / camera.CellsPerPixel, height / camera.CellsPerPixel);
    }

    private static void ValidateCamera(CameraState camera)
    {
        if (!float.IsFinite(camera.CellsPerPixel) || camera.CellsPerPixel <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "CameraState.CellsPerPixel 必须为正有限数。");
        }
    }

    private static uint TemperatureColor(float temperature)
    {
        float hot = Math.Clamp(temperature / 1000f, 0f, 1f);
        float cold = Math.Clamp(-temperature / 200f, 0f, 1f);
        byte r = (byte)(255f * hot);
        byte g = (byte)(96f * MathF.Max(0f, 1f - (MathF.Abs(temperature) / 1000f)));
        byte b = (byte)(255f * cold);
        return PackBgra(b, g, r, 0xC0);
    }

    private static uint BodyColor(int key)
    {
        uint hash = unchecked((uint)key * 0x9E3779B9u);
        byte r = (byte)(96 + (hash & 0x7F));
        byte g = (byte)(96 + ((hash >> 8) & 0x7F));
        byte b = (byte)(96 + ((hash >> 16) & 0x7F));
        return PackBgra(b, g, r, 0xC0);
    }

    private static uint PackBgra(byte b, byte g, byte r, byte a)
    {
        return b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }
}
