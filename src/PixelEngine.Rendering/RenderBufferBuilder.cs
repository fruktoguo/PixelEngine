using System.Diagnostics;
using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Threading;
using PixelEngine.Simulation;

namespace PixelEngine.Rendering;

/// <summary>
/// 相位 9 CPU render buffer 构建器。只读 sim cell 与温度场，在本相位生成颜色，守护不变式 #7。
/// </summary>
/// <param name="jobs">可选 Core 持久线程池；为 null 时单线程执行。</param>
/// <param name="textures">可选材质纹理提供器。</param>
/// <param name="options">构建参数。</param>
public sealed class RenderBufferBuilder(
    JobSystem? jobs = null,
    IMaterialTextureProvider? textures = null,
    RenderBufferBuilderOptions? options = null)
{
    private readonly JobSystem? _jobs = jobs;
    private readonly IMaterialTextureProvider? _textures = textures;
    private readonly RenderBufferBuilderOptions _options = options ?? new RenderBufferBuilderOptions();
    private readonly BuildState _state = new();

    /// <summary>
    /// 构建 BGRA8 render buffer 及 emissive/occluder 副输出。
    /// </summary>
    /// <param name="context">渲染帧上下文。</param>
    /// <param name="target">目标 render buffer。</param>
    /// <param name="aux">副输出 buffer。</param>
    /// <param name="profiler">可选 Core 诊断 profiler。</param>
    public void Build(RenderFrameContext context, RenderBuffer target, RenderAuxBuffers aux, FrameProfiler? profiler = null)
    {
        long started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(aux);
        if (target.Width != context.Camera.ViewportWidth || target.Height != context.Camera.ViewportHeight)
        {
            target.Resize(context.Camera.ViewportWidth, context.Camera.ViewportHeight);
        }

        aux.Resize(target.Width, target.Height);
        if (!context.SimStepped && context.DebugCellColors is null)
        {
            RecordSub(profiler, started);
            return;
        }

        aux.Clear();
        _state.Context = context;
        _state.Target = target;
        _state.Aux = aux;
        _state.Builder = this;
        if (_jobs is null)
        {
            BuildRows(0, target.Height, 0, _state);
            RecordSub(profiler, started);
            return;
        }

        _jobs.ParallelRange(target.Height, Math.Max(1, _options.MinRowsPerJob), BuildRows, _state);
        RecordSub(profiler, started);
    }

    private static void BuildRows(int start, int end, int workerIndex, object? state)
    {
        BuildState buildState = (BuildState)state!;
        RenderFrameContext context = buildState.Context!;
        RenderBuffer target = buildState.Target!;
        RenderAuxBuffers aux = buildState.Aux!;
        RenderBufferBuilder builder = buildState.Builder!;
        if (builder.CanUsePaletteFastPath(context))
        {
            builder.BuildRowsPaletteFast(context, target, aux, start, end);
            return;
        }

        Span<uint> pixels = target.Pixels;
        Span<uint> emissive = aux.Emissive;
        Span<byte> occluder = aux.Occluder;
        for (int sy = start; sy < end; sy++)
        {
            int row = sy * target.Width;
            for (int sx = 0; sx < target.Width; sx++)
            {
                int worldX = builder.ScreenToWorldX(context.Camera, sx);
                int worldY = builder.ScreenToWorldY(context.Camera, sy);
                int index = row + sx;
                uint color = builder.SampleCell(context, worldX, worldY, out bool isEmissive, out bool isOccluder);
                pixels[index] = color;
                if (isEmissive)
                {
                    emissive[index] = color;
                }

                if (isOccluder)
                {
                    occluder[index] = byte.MaxValue;
                }
            }
        }
    }

    private void BuildRowsPaletteFast(RenderFrameContext context, RenderBuffer target, RenderAuxBuffers aux, int start, int end)
    {
        Span<uint> pixels = target.Pixels;
        Span<uint> emissive = aux.Emissive;
        Span<byte> occluder = aux.Occluder;
        MaterialHotTable hot = context.Materials.Hot;
        ReadOnlySpan<uint> palette = hot.BaseColorBGRA;
        int originX = (int)context.Camera.OriginWorldX;
        int originY = (int)context.Camera.OriginWorldY;

        for (int sy = start; sy < end; sy++)
        {
            int row = sy * target.Width;
            int worldY = originY + sy;
            int localY = CellAddressing.LocalCoord(worldY);
            int sx = 0;
            while (sx < target.Width)
            {
                int worldX = originX + sx;
                int localX = CellAddressing.LocalCoord(worldX);
                int run = Math.Min(target.Width - sx, EngineConstants.ChunkSize - localX);
                Span<uint> pixelRun = pixels.Slice(row + sx, run);
                if (!context.Chunks.TryGetChunk(CellAddressing.WorldToChunk(worldX, worldY), out Chunk chunk))
                {
                    pixelRun.Clear();
                    sx += run;
                    continue;
                }

                ReadOnlySpan<ushort> materials = chunk.Material.AsSpan(
                    CellAddressing.LocalIndexFromLocal(localX, localY),
                    run);
                PaletteBgraConverter.Convert(materials, palette, pixelRun);
                FillAuxFast(materials, hot, pixelRun, emissive.Slice(row + sx, run), occluder.Slice(row + sx, run));
                sx += run;
            }
        }
    }

    private bool CanUsePaletteFastPath(RenderFrameContext context)
    {
        CameraState camera = context.Camera;
        MaterialHotTable hot = context.Materials.Hot;
        return camera.CellsPerPixel == 1f &&
            camera.OriginWorldX == MathF.Truncate(camera.OriginWorldX) &&
            camera.OriginWorldY == MathF.Truncate(camera.OriginWorldY) &&
            context.DebugCellColors is null &&
            !context.Temperature.HasActiveBlocks &&
            !hot.HasColorNoise &&
            (_textures is null || !hot.HasTexturedMaterials);
    }

    private static void FillAuxFast(
        ReadOnlySpan<ushort> materials,
        MaterialHotTable hot,
        ReadOnlySpan<uint> colors,
        Span<uint> emissive,
        Span<byte> occluder)
    {
        ReadOnlySpan<MaterialProperty> propertyFlags = hot.PropertyFlags;
        ReadOnlySpan<CellType> type = hot.Type;
        for (int i = 0; i < materials.Length; i++)
        {
            ushort material = materials[i];
            MaterialProperty flags = propertyFlags[material];
            if ((flags & MaterialProperty.Emissive) != 0)
            {
                emissive[i] = colors[i];
            }

            if (type[material] == CellType.Solid || (flags & MaterialProperty.Static) != 0)
            {
                occluder[i] = byte.MaxValue;
            }
        }
    }

    private int ScreenToWorldX(CameraState camera, int screenX)
    {
        return (int)MathF.Floor(camera.OriginWorldX + (screenX * camera.CellsPerPixel));
    }

    private int ScreenToWorldY(CameraState camera, int screenY)
    {
        return (int)MathF.Floor(camera.OriginWorldY + (screenY * camera.CellsPerPixel));
    }

    private uint SampleCell(RenderFrameContext context, int worldX, int worldY, out bool isEmissive, out bool isOccluder)
    {
        isEmissive = false;
        isOccluder = false;
        ChunkCoord coord = CellAddressing.WorldToChunk(worldX, worldY);
        if (!context.Chunks.TryGetChunk(coord, out Chunk chunk))
        {
            return 0;
        }

        int local = CellAddressing.LocalIndex(worldX, worldY);
        ushort materialId = chunk.Material[local];
        byte cellFlags = chunk.Flags[local];
        ref readonly MaterialDef material = ref context.Materials.Get(materialId);
        uint color = material.BaseColorBGRA;
        if (material.TextureId >= 0 && _textures is not null &&
            _textures.TrySample(in material, worldX, worldY, out uint textureColor))
        {
            color = textureColor;
        }

        color = ApplyColorNoise(color, material.ColorNoise, worldX, worldY);
        float temperature = context.Temperature.GetTemperature(worldX, worldY);
        color = ApplyTemperatureGlow(color, temperature);
        if (context.DebugCellColors?.TryGetDebugColor(worldX, worldY, materialId, cellFlags, temperature, out uint debugColor) == true)
        {
            color = debugColor;
        }

        MaterialProperty flags = material.PropertyFlags;
        isEmissive = (flags & MaterialProperty.Emissive) != 0 ||
            temperature > _options.TemperatureGlowThreshold;
        isOccluder = material.Type == CellType.Solid || (flags & MaterialProperty.Static) != 0;
        return color;
    }

    private uint ApplyTemperatureGlow(uint bgra, float temperature)
    {
        if (temperature <= _options.TemperatureGlowThreshold)
        {
            return bgra;
        }

        float glow = MathF.Min(1f, (temperature - _options.TemperatureGlowThreshold) * _options.TemperatureGlowScale);
        byte b = (byte)(bgra & 0xFF);
        byte g = (byte)((bgra >> 8) & 0xFF);
        byte r = (byte)((bgra >> 16) & 0xFF);
        byte a = (byte)((bgra >> 24) & 0xFF);
        r = AddScaled(r, 255, glow);
        g = AddScaled(g, 96, glow * 0.5f);
        return PackBgra(b, g, r, a);
    }

    private static uint ApplyColorNoise(uint bgra, byte amount, int worldX, int worldY)
    {
        if (amount == 0)
        {
            return bgra;
        }

        uint hash = unchecked((uint)(worldX * 73856093) ^ (uint)(worldY * 19349663));
        int delta = ((int)(hash & 0xFF) - 128) * amount / 255;
        byte b = Adjust((byte)(bgra & 0xFF), delta);
        byte g = Adjust((byte)((bgra >> 8) & 0xFF), delta);
        byte r = Adjust((byte)((bgra >> 16) & 0xFF), delta);
        byte a = (byte)((bgra >> 24) & 0xFF);
        return PackBgra(b, g, r, a);
    }

    private static byte AddScaled(byte value, byte target, float amount)
    {
        return (byte)Math.Clamp(value + ((target - value) * amount), 0, 255);
    }

    private static byte Adjust(byte value, int delta)
    {
        return (byte)Math.Clamp(value + delta, 0, 255);
    }

    private static uint PackBgra(byte b, byte g, byte r, byte a)
    {
        return b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    private static void RecordSub(FrameProfiler? profiler, long started)
    {
        if (profiler is null)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        profiler.RecordSub(FrameSubPhase.RenderBufferBuild, elapsed * 1000.0 / Stopwatch.Frequency);
    }

    private sealed class BuildState
    {
        public RenderFrameContext? Context;
        public RenderBuffer? Target;
        public RenderAuxBuffers? Aux;
        public RenderBufferBuilder? Builder;
    }
}
