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
    : IRenderStyleQualityController
{
    private readonly JobSystem? _jobs = jobs;
    private readonly IMaterialTextureProvider? _textures = textures;
    private readonly RenderBufferBuilderOptions _options = options ?? new RenderBufferBuilderOptions();
    private readonly BuildState _state = new();
    private Chunk[] _emptyWorldChunks = [];
    private int _emptyWorldChunkCount;
    private IChunkSource? _emptyWorldSource;
    private MaterialTable? _emptyWorldMaterials;

    /// <inheritdoc />
    public RenderBufferStyleLevel RenderStyleLevel { get; private set; } = (options ?? new RenderBufferBuilderOptions()).StyleLevel;

    /// <inheritdoc />
    public void SetRenderStyleLevel(RenderBufferStyleLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "未知 RenderStyle 着色质量档。");
        }

        RenderStyleLevel = level;
    }

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
        // 早退：本帧未推进 sim 且无调试着色时跳过重建。
        if (!context.SimStepped && context.DebugCellColors is null)
        {
            RecordSub(profiler, started);
            return;
        }

        aux.Clear();
        // 空世界快路径：全 resident chunk 材质为 empty 时直接清零目标 buffer。
        if (CanClearEmptyWorld(context))
        {
            target.Pixels.Clear();
            RecordSub(profiler, started);
            return;
        }

        bool useMaterialStyles = ShouldUseMaterialStyles(context);
        _state.Context = context;
        _state.Target = target;
        _state.Aux = aux;
        _state.Builder = this;
        // 按行并行或单线程扫描视口，内部选择 palette/style/scalar 快路径。
        if (_jobs is null)
        {
            BuildRows(0, target.Height, 0, _state);
            RecordStyleSub(profiler, started, useMaterialStyles);
            RecordSub(profiler, started);
            return;
        }

        _jobs.ParallelRange(target.Height, Math.Max(1, _options.MinRowsPerJob), BuildRows, _state);
        RecordStyleSub(profiler, started, useMaterialStyles);
        RecordSub(profiler, started);
    }

    private static void BuildRows(int start, int end, int workerIndex, object? state)
    {
        BuildState buildState = (BuildState)state!;
        RenderFrameContext context = buildState.Context!;
        RenderBuffer target = buildState.Target!;
        RenderAuxBuffers aux = buildState.Aux!;
        RenderBufferBuilder builder = buildState.Builder!;
        // 快路径选择：1:1 palette → zoom palette → style 分段 → 逐像素标量采样。
        if (builder.CanUsePaletteFastPath(context))
        {
            builder.BuildRowsPaletteFast(context, target, aux, start, end);
            return;
        }

        if (builder.TryGetPaletteZoomPixelsPerCell(context, out int pixelsPerCell))
        {
            builder.BuildRowsPaletteZoomFast(context, target, aux, start, end, pixelsPerCell);
            return;
        }

        if (builder.CanUseStyledSegmentedFastPath(context))
        {
            builder.BuildRowsStyledSegmented(context, target, aux, start, end);
            return;
        }

        // 标量回退：逐屏幕像素采样世界 cell 并写入 emissive/occluder 副输出。
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

    private void BuildRowsStyledSegmented(RenderFrameContext context, RenderBuffer target, RenderAuxBuffers aux, int start, int end)
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
            // 按 chunk 行内连续区间批量转换，style 材质在边界处截断 run。
            int sx = 0;
            while (sx < target.Width)
            {
                int worldX = originX + sx;
                int localX = CellAddressing.LocalCoord(worldX);
                int run = Math.Min(target.Width - sx, EngineConstants.ChunkSize - localX);
                int rowOffset = row + sx;
                if (!context.Chunks.TryGetChunk(CellAddressing.WorldToChunk(worldX, worldY), out Chunk chunk))
                {
                    pixels.Slice(rowOffset, run).Clear();
                    sx += run;
                    continue;
                }

                int localStart = CellAddressing.LocalIndexFromLocal(localX, localY);
                int offset = 0;
                while (offset < run)
                {
                    int segment = GetStyledPaletteRunLength(context, chunk, localStart + offset, run - offset, worldX + offset, worldY);
                    if (segment > 0)
                    {
                        ReadOnlySpan<ushort> materials = chunk.Material.AsSpan(localStart + offset, segment);
                        Span<uint> pixelRun = pixels.Slice(rowOffset + offset, segment);
                        PaletteBgraConverter.Convert(materials, palette, pixelRun);
                        if (hot.HasColorNoise)
                        {
                            BgraColorMixer.ApplyColorNoise(materials, hot.ColorNoise, pixelRun, worldX + offset, worldY);
                        }

                        FillAuxFast(materials, hot, pixelRun, emissive.Slice(rowOffset + offset, segment), occluder.Slice(rowOffset + offset, segment));
                        offset += segment;
                        continue;
                    }

                    WriteScalarPixel(context, worldX + offset, worldY, rowOffset + offset, pixels, emissive, occluder);
                    offset++;
                }

                sx += run;
            }
        }
    }

    private void BuildRowsPaletteZoomFast(
        RenderFrameContext context,
        RenderBuffer target,
        RenderAuxBuffers aux,
        int start,
        int end,
        int pixelsPerCell)
    {
        Span<uint> pixels = target.Pixels;
        Span<uint> emissive = aux.Emissive;
        Span<byte> occluder = aux.Occluder;
        MaterialHotTable hot = context.Materials.Hot;
        ReadOnlySpan<uint> palette = hot.BaseColorBGRA;
        ReadOnlySpan<byte> colorNoise = hot.ColorNoise;
        int originX = (int)context.Camera.OriginWorldX;
        int originY = (int)context.Camera.OriginWorldY;
        bool hasColorNoise = hot.HasColorNoise;

        for (int sy = start; sy < end; sy++)
        {
            int row = sy * target.Width;
            int worldY = originY + (sy / pixelsPerCell);
            for (int sx = 0; sx < target.Width;)
            {
                int worldX = originX + (sx / pixelsPerCell);
                int repeat = Math.Min(pixelsPerCell - (sx % pixelsPerCell), target.Width - sx);
                int index = row + sx;
                if (!context.Chunks.TryGetChunk(CellAddressing.WorldToChunk(worldX, worldY), out Chunk chunk))
                {
                    pixels.Slice(index, repeat).Clear();
                    sx += repeat;
                    continue;
                }

                ushort material = chunk.Material[CellAddressing.LocalIndex(worldX, worldY)];
                uint color = palette[material];
                if (hasColorNoise)
                {
                    color = ApplyColorNoise(color, colorNoise[material], worldX, worldY);
                }

                pixels.Slice(index, repeat).Fill(color);
                MaterialProperty flags = hot.PropertyFlags[material];
                if ((flags & MaterialProperty.Emissive) != 0)
                {
                    emissive.Slice(index, repeat).Fill(color);
                }

                if (hot.Type[material] == CellType.Solid || (flags & MaterialProperty.Static) != 0)
                {
                    occluder.Slice(index, repeat).Fill(byte.MaxValue);
                }

                sx += repeat;
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
                if (hot.HasColorNoise)
                {
                    BgraColorMixer.ApplyColorNoise(materials, hot.ColorNoise, pixelRun, worldX, worldY);
                }

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
            !ShouldUseMaterialStyles(context) &&
            !context.Temperature.HasActiveBlocks &&
            (_textures is null || !hot.HasTexturedMaterials);
    }

    private bool CanUseStyledSegmentedFastPath(RenderFrameContext context)
    {
        CameraState camera = context.Camera;
        MaterialHotTable hot = context.Materials.Hot;
        return ShouldUseMaterialStyles(context) &&
            camera.CellsPerPixel == 1f &&
            camera.OriginWorldX == MathF.Truncate(camera.OriginWorldX) &&
            camera.OriginWorldY == MathF.Truncate(camera.OriginWorldY) &&
            context.DebugCellColors is null &&
            !context.Temperature.HasActiveBlocks &&
            (_textures is null || !hot.HasTexturedMaterials);
    }

    private bool TryGetPaletteZoomPixelsPerCell(RenderFrameContext context, out int pixelsPerCell)
    {
        pixelsPerCell = 0;
        CameraState camera = context.Camera;
        MaterialHotTable hot = context.Materials.Hot;
        if (camera.CellsPerPixel <= 0f ||
            camera.CellsPerPixel >= 1f ||
            camera.OriginWorldX != MathF.Truncate(camera.OriginWorldX) ||
            camera.OriginWorldY != MathF.Truncate(camera.OriginWorldY) ||
            context.DebugCellColors is not null ||
            ShouldUseMaterialStyles(context) ||
            context.Temperature.HasActiveBlocks ||
            (_textures is not null && hot.HasTexturedMaterials))
        {
            return false;
        }

        float reciprocal = 1f / camera.CellsPerPixel;
        int rounded = (int)MathF.Round(reciprocal);
        if (rounded <= 1 || rounded > 8 || MathF.Abs(reciprocal - rounded) > 0.0001f)
        {
            return false;
        }

        pixelsPerCell = rounded;
        return true;
    }

    private bool ShouldUseMaterialStyles(RenderFrameContext context)
    {
        return RenderStyleLevel == RenderBufferStyleLevel.Full && context.Materials.Visual.HasStyleEffects;
    }

    private int GetStyledPaletteRunLength(RenderFrameContext context, Chunk chunk, int localStart, int remaining, int worldX, int worldY)
    {
        MaterialHotTable hot = context.Materials.Hot;
        MaterialVisualTable visual = context.Materials.Visual;
        ushort materialId = chunk.Material[localStart];
        if (!IsStyledPaletteFastMaterial(hot, visual, materialId))
        {
            return 0;
        }

        int unbrokenRun = RenderStyleSegmentScanner.CountSolidUnbrokenRun(
            chunk.Material,
            chunk.Damage,
            localStart,
            remaining,
            materialId);
        if (unbrokenRun == 0)
        {
            return 0;
        }

        uint edgeColor = visual.EdgeColorBGRA[materialId];
        if (edgeColor == 0)
        {
            return unbrokenRun;
        }

        for (int length = 0; length < unbrokenRun; length++)
        {
            int currentX = worldX + length;
            if (IsBoundaryEdge(context, materialId, currentX - 1, worldY) ||
                IsBoundaryEdge(context, materialId, currentX, worldY - 1))
            {
                return length;
            }
        }

        return unbrokenRun;
    }

    private static bool IsStyledPaletteFastMaterial(MaterialHotTable hot, MaterialVisualTable visual, ushort materialId)
    {
        MaterialRenderStyle style = visual.RenderStyle[materialId];
        return (style is MaterialRenderStyle.Ground or MaterialRenderStyle.Solid or MaterialRenderStyle.Destructible) &&
            (hot.Type[materialId] == CellType.Solid ||
                (hot.PropertyFlags[materialId] & MaterialProperty.Static) != 0) &&
            hot.TextureId[materialId] < 0 &&
            visual.Opacity[materialId] == byte.MaxValue &&
            visual.HighlightColorBGRA[materialId] == 0 &&
            (hot.BaseColorBGRA[materialId] >> 24) == byte.MaxValue;
    }

    // 空世界检测：材质 id 0 为透明 empty 且所有 resident chunk 全为 0 时可跳过像素写入。
    private bool CanClearEmptyWorld(RenderFrameContext context)
    {
        if (context.DebugCellColors is not null)
        {
            InvalidateEmptyWorldCache();
            return false;
        }

        MaterialHotTable hot = context.Materials.Hot;
        if (hot.Count == 0 ||
            hot.BaseColorBGRA[0] != 0 ||
            hot.ColorNoise[0] != 0 ||
            hot.Type[0] != CellType.Empty ||
            (hot.PropertyFlags[0] & (MaterialProperty.Emissive | MaterialProperty.Static)) != 0)
        {
            InvalidateEmptyWorldCache();
            return false;
        }

        ReadOnlySpan<Chunk> chunks = context.Chunks.ResidentChunks;
        if (CanReuseEmptyWorldCache(context, chunks))
        {
            return true;
        }

        bool canCache = true;
        for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            Chunk chunk = chunks[chunkIndex];
            if (HasDirtyMetadata(chunk))
            {
                canCache = false;
            }

            ReadOnlySpan<ushort> materials = chunk.Material;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != 0)
                {
                    InvalidateEmptyWorldCache();
                    return false;
                }
            }
        }

        if (canCache)
        {
            StoreEmptyWorldCache(context, chunks);
        }
        else
        {
            InvalidateEmptyWorldCache();
        }

        return true;
    }

    private bool CanReuseEmptyWorldCache(RenderFrameContext context, ReadOnlySpan<Chunk> chunks)
    {
        if (!ReferenceEquals(_emptyWorldSource, context.Chunks) ||
            !ReferenceEquals(_emptyWorldMaterials, context.Materials) ||
            _emptyWorldChunkCount != chunks.Length)
        {
            return false;
        }

        for (int i = 0; i < chunks.Length; i++)
        {
            Chunk chunk = chunks[i];
            if (!ReferenceEquals(_emptyWorldChunks[i], chunk) || HasDirtyMetadata(chunk))
            {
                return false;
            }
        }

        return true;
    }

    private void StoreEmptyWorldCache(RenderFrameContext context, ReadOnlySpan<Chunk> chunks)
    {
        if (_emptyWorldChunks.Length < chunks.Length)
        {
            Array.Resize(ref _emptyWorldChunks, chunks.Length);
        }

        for (int i = 0; i < chunks.Length; i++)
        {
            _emptyWorldChunks[i] = chunks[i];
        }

        if (_emptyWorldChunkCount > chunks.Length)
        {
            Array.Clear(_emptyWorldChunks, chunks.Length, _emptyWorldChunkCount - chunks.Length);
        }

        _emptyWorldChunkCount = chunks.Length;
        _emptyWorldSource = context.Chunks;
        _emptyWorldMaterials = context.Materials;
    }

    private void InvalidateEmptyWorldCache()
    {
        if (_emptyWorldChunkCount > 0)
        {
            Array.Clear(_emptyWorldChunks, 0, _emptyWorldChunkCount);
        }

        _emptyWorldChunkCount = 0;
        _emptyWorldSource = null;
        _emptyWorldMaterials = null;
    }

    private static bool HasDirtyMetadata(Chunk chunk)
    {
        if (!chunk.CurrentDirty.IsEmpty || !chunk.WorkingDirty.IsEmpty)
        {
            return true;
        }

        for (int slot = 0; slot < chunk.IncomingDirtySlotCount; slot++)
        {
            if (!chunk.GetIncomingDirty(slot).IsEmpty)
            {
                return true;
            }
        }

        return false;
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

    private void WriteScalarPixel(
        RenderFrameContext context,
        int worldX,
        int worldY,
        int index,
        Span<uint> pixels,
        Span<uint> emissive,
        Span<byte> occluder)
    {
        uint color = SampleCell(context, worldX, worldY, out bool isEmissive, out bool isOccluder);
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

    // 标量采样管线：纹理 → 色噪 → 温度辉光 → 材质 style → 调试色 → emissive/occluder 标记。
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
        MaterialHotTable hot = context.Materials.Hot;
        MaterialVisualTable visual = context.Materials.Visual;
        uint color = hot.BaseColorBGRA[materialId];
        if (hot.TextureId[materialId] >= 0 && _textures is not null &&
            GetMaterialForTexture(context.Materials, materialId, out MaterialDef material) &&
            _textures.TrySample(in material, worldX, worldY, out uint textureColor))
        {
            color = textureColor;
        }

        color = ApplyColorNoise(color, hot.ColorNoise[materialId], worldX, worldY);
        float temperature = context.Temperature.GetTemperature(worldX, worldY);
        color = ApplyTemperatureGlow(color, temperature);
        bool styledEmissive = false;
        if (ShouldUseMaterialStyles(context))
        {
            color = ApplyMaterialStyle(
                context,
                chunk,
                local,
                materialId,
                hot.ColorNoise[materialId],
                hot.Integrity[materialId],
                visual.RenderStyle[materialId],
                visual.EdgeColorBGRA[materialId],
                visual.Opacity[materialId],
                visual.HighlightColorBGRA[materialId],
                color,
                worldX,
                worldY,
                out styledEmissive);
        }

        if (context.DebugCellColors?.TryGetDebugColor(worldX, worldY, materialId, cellFlags, temperature, out uint debugColor) == true)
        {
            color = debugColor;
        }

        MaterialProperty flags = hot.PropertyFlags[materialId];
        isEmissive = (flags & MaterialProperty.Emissive) != 0 ||
            styledEmissive ||
            temperature > _options.TemperatureGlowThreshold;
        isOccluder = hot.Type[materialId] == CellType.Solid || (flags & MaterialProperty.Static) != 0;
        return color;
    }

    private static bool GetMaterialForTexture(MaterialTable materials, ushort materialId, out MaterialDef material)
    {
        material = materials.Get(materialId);
        return true;
    }

    private uint ApplyMaterialStyle(
        RenderFrameContext context,
        Chunk chunk,
        int local,
        ushort materialId,
        byte colorNoise,
        ushort integrity,
        MaterialRenderStyle renderStyle,
        uint edgeColor,
        byte opacity,
        uint highlightColor,
        uint color,
        int worldX,
        int worldY,
        out bool styledEmissive)
    {
        styledEmissive = false;
        // 按材质渲染风格应用粉末噪点、液体高光、气体透明度、危险脉冲等视觉效果。
        switch (renderStyle)
        {
            case MaterialRenderStyle.Powder:
                color = ApplyColorNoise(color, colorNoise == 0 ? (byte)32 : colorNoise, worldX, worldY);
                break;
            case MaterialRenderStyle.Liquid:
                color = ApplyFlowHighlight(color, highlightColor, worldX, worldY, context.FrameTimeSeconds);
                break;
            case MaterialRenderStyle.Gas:
                color = ApplyOpacity(color, opacity);
                break;
            case MaterialRenderStyle.Hazard:
                styledEmissive = true;
                color = ApplyPulseHighlight(color, highlightColor, context.FrameTimeSeconds);
                color = ApplyBoundaryEdge(context, materialId, edgeColor, color, worldX, worldY);
                break;
            case MaterialRenderStyle.Emissive:
                styledEmissive = true;
                color = ApplyPulseHighlight(color, highlightColor, context.FrameTimeSeconds);
                break;
            case MaterialRenderStyle.Solid:
            case MaterialRenderStyle.Destructible:
            case MaterialRenderStyle.Ground:
            default:
                color = ApplyBoundaryEdge(context, materialId, edgeColor, color, worldX, worldY);
                break;
        }

        if ((renderStyle == MaterialRenderStyle.Destructible ||
                renderStyle == MaterialRenderStyle.Solid ||
                renderStyle == MaterialRenderStyle.Ground) &&
            integrity != 0)
        {
            color = ApplyDamageCrack(color, chunk.Damage[local], integrity);
        }

        return color;
    }

    private uint ApplyBoundaryEdge(RenderFrameContext context, ushort materialId, uint edgeColor, uint color, int worldX, int worldY)
    {
        return edgeColor != 0 &&
            (IsBoundaryEdge(context, materialId, worldX - 1, worldY) ||
                IsBoundaryEdge(context, materialId, worldX, worldY - 1))
            ? Blend(color, edgeColor, 0.65f)
            : color;
    }

    private bool IsBoundaryEdge(RenderFrameContext context, ushort materialId, int neighborX, int neighborY)
    {
        if (!context.Chunks.TryGetChunk(CellAddressing.WorldToChunk(neighborX, neighborY), out Chunk neighborChunk))
        {
            return true;
        }

        ushort neighborMaterial = neighborChunk.Material[CellAddressing.LocalIndex(neighborX, neighborY)];
        if (neighborMaterial == materialId)
        {
            return false;
        }

        CellType neighborType = context.Materials.Hot.Type[neighborMaterial];
        return neighborType != CellType.Solid &&
            (context.Materials.Hot.PropertyFlags[neighborMaterial] & MaterialProperty.Static) == 0;
    }

    private static uint ApplyDamageCrack(uint color, byte damage, ushort integrity)
    {
        if (damage == 0)
        {
            return color;
        }

        float amount = Math.Clamp(damage * EngineConstants.DamageIntegrityScale / (float)integrity, 0f, 1f);
        return Blend(color, 0xFF050505u, MathF.Min(0.75f, amount * 0.8f));
    }

    private static uint ApplyFlowHighlight(uint color, uint highlightColor, int worldX, int worldY, float frameTimeSeconds)
    {
        if (highlightColor == 0)
        {
            return color;
        }

        int phase = (int)(frameTimeSeconds * 12f);
        uint hash = unchecked((uint)((worldX * 1103515245) ^ (worldY * 12345) ^ phase));
        float amount = ((hash >> 28) & 0x7) == 0 ? 0.28f : 0.08f;
        return Blend(color, highlightColor, amount);
    }

    private static uint ApplyPulseHighlight(uint color, uint highlightColor, float frameTimeSeconds)
    {
        if (highlightColor == 0)
        {
            return color;
        }

        float amount = 0.18f + ((MathF.Sin(frameTimeSeconds * 8f) + 1f) * 0.11f);
        return Blend(color, highlightColor, amount);
    }

    private static uint ApplyOpacity(uint color, byte opacity)
    {
        if (opacity == byte.MaxValue)
        {
            return color;
        }

        float alpha = opacity / 255f;
        byte b = (byte)((color & 0xFF) * alpha);
        byte g = (byte)(((color >> 8) & 0xFF) * alpha);
        byte r = (byte)(((color >> 16) & 0xFF) * alpha);
        return PackBgra(b, g, r, opacity);
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

    private static uint Blend(uint source, uint target, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        byte b = AddScaled((byte)(source & 0xFF), (byte)(target & 0xFF), amount);
        byte g = AddScaled((byte)((source >> 8) & 0xFF), (byte)((target >> 8) & 0xFF), amount);
        byte r = AddScaled((byte)((source >> 16) & 0xFF), (byte)((target >> 16) & 0xFF), amount);
        byte a = AddScaled((byte)((source >> 24) & 0xFF), (byte)((target >> 24) & 0xFF), amount);
        return PackBgra(b, g, r, a);
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

    private static void RecordStyleSub(FrameProfiler? profiler, long started, bool active)
    {
        if (!active || profiler is null)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        profiler.RecordSub(FrameSubPhase.RenderStyleShading, elapsed * 1000.0 / Stopwatch.Frequency);
    }

    private sealed class BuildState
    {
        public RenderFrameContext? Context;
        public RenderBuffer? Target;
        public RenderAuxBuffers? Aux;
        public RenderBufferBuilder? Builder;
    }
}
