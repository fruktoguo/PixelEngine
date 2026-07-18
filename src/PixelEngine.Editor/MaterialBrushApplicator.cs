using PixelEngine.Core;
using PixelEngine.Simulation;

namespace PixelEngine.Editor;

/// <summary>
/// 将材质/温度画刷应用到世界坐标。
/// </summary>
public sealed class MaterialBrushApplicator(
    ISimulationEditApi editApi,
    uint seed = 0x9E3779B9u,
    ISimulationInspectApi? inspectApi = null)
{
    private readonly ISimulationEditApi _editApi = editApi ?? throw new ArgumentNullException(nameof(editApi));
    private readonly ISimulationInspectApi? _inspectApi = inspectApi ?? editApi as ISimulationInspectApi;
    private readonly uint _seed = seed;

    /// <summary>
    /// 在指定世界坐标应用画刷。
    /// </summary>
    /// <param name="centerX">中心世界 X。</param>
    /// <param name="centerY">中心世界 Y。</param>
    /// <param name="settings">画刷参数。</param>
    /// <returns>实际写入的 cell 数。</returns>
    public int ApplyAt(int centerX, int centerY, MaterialBrushSettings settings)
    {
        return ApplyAt(centerX, centerY, settings, MaterialBrushBounds.Unbounded, out _, out _);
    }

    /// <summary>
    /// 在指定世界坐标应用画刷，并返回因 chunk 未驻留而跳过的 cell 数。
    /// </summary>
    /// <param name="centerX">中心世界 X。</param>
    /// <param name="centerY">中心世界 Y。</param>
    /// <param name="settings">画刷参数。</param>
    /// <param name="skippedNonResidentCells">因目标 chunk 未驻留而跳过的 cell 数。</param>
    /// <returns>实际写入的 cell 数。</returns>
    public int ApplyAt(
        int centerX,
        int centerY,
        MaterialBrushSettings settings,
        out int skippedNonResidentCells)
    {
        return ApplyAt(
            centerX,
            centerY,
            settings,
            MaterialBrushBounds.Unbounded,
            out skippedNonResidentCells,
            out _);
    }

    /// <summary>
    /// 在指定世界坐标与 authoring 边界内应用画刷。
    /// </summary>
    /// <param name="centerX">中心世界 X。</param>
    /// <param name="centerY">中心世界 Y。</param>
    /// <param name="settings">画刷参数。</param>
    /// <param name="bounds">允许编辑的闭区间 cell 边界。</param>
    /// <param name="skippedNonResidentCells">因目标 chunk 未驻留而跳过的 cell 数。</param>
    /// <param name="skippedOutOfBoundsCells">因超出 authoring 边界而跳过的 cell 数。</param>
    /// <returns>实际写入的 cell 数。</returns>
    public int ApplyAt(
        int centerX,
        int centerY,
        MaterialBrushSettings settings,
        MaterialBrushBounds bounds,
        out int skippedNonResidentCells,
        out int skippedOutOfBoundsCells)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int radiusX = settings.Shape == EditorBrushShape.Point ? 0 : settings.ClampedRadiusX;
        int radiusY = settings.Shape == EditorBrushShape.Point ? 0 : settings.ClampedRadiusY;
        if (TryApplyBulkRect(
            centerX,
            centerY,
            radiusX,
            radiusY,
            settings,
            bounds,
            out int bulkWrites,
            out skippedNonResidentCells,
            out skippedOutOfBoundsCells))
        {
            return bulkWrites;
        }

        skippedOutOfBoundsCells = 0;
        int writes = 0;
        for (int dy = -radiusY; dy <= radiusY; dy++)
        {
            for (int dx = -radiusX; dx <= radiusX; dx++)
            {
                if (!Contains(settings.Shape, radiusX, radiusY, dx, dy))
                {
                    continue;
                }

                int x = centerX + dx;
                int y = centerY + dy;
                if (!bounds.Contains(x, y))
                {
                    skippedOutOfBoundsCells++;
                    continue;
                }

                if (!PassesProbability(x, y, settings.ClampedProbability))
                {
                    continue;
                }

                if (!CanEditCell(x, y))
                {
                    skippedNonResidentCells++;
                    continue;
                }

                ApplyCell(x, y, settings);
                writes++;
            }
        }

        return writes;
    }

    private bool TryApplyBulkRect(
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        MaterialBrushSettings settings,
        MaterialBrushBounds bounds,
        out int writes,
        out int skippedNonResidentCells,
        out int skippedOutOfBoundsCells)
    {
        writes = 0;
        skippedNonResidentCells = 0;
        skippedOutOfBoundsCells = 0;
        if (settings.Shape != EditorBrushShape.Square || settings.ClampedProbability < 1f)
        {
            return false;
        }

        int minX = centerX - radiusX;
        int minY = centerY - radiusY;
        int maxX = centerX + radiusX;
        int maxY = centerY + radiusY;
        int clippedMinX = Math.Max(minX, bounds.MinX);
        int clippedMinY = Math.Max(minY, bounds.MinY);
        int clippedMaxX = Math.Min(maxX, bounds.MaxX);
        int clippedMaxY = Math.Min(maxY, bounds.MaxY);
        int requestedCells = ((radiusX * 2) + 1) * ((radiusY * 2) + 1);
        if (clippedMinX > clippedMaxX || clippedMinY > clippedMaxY)
        {
            skippedOutOfBoundsCells = requestedCells;
            return true;
        }

        int clippedCells = (clippedMaxX - clippedMinX + 1) * (clippedMaxY - clippedMinY + 1);
        skippedOutOfBoundsCells = requestedCells - clippedCells;
        minX = clippedMinX;
        minY = clippedMinY;
        maxX = clippedMaxX;
        maxY = clippedMaxY;

        if (_inspectApi is null)
        {
            writes = ApplyBulkRect(minX, minY, maxX, maxY, settings);
            return settings.Tool is EditorBrushTool.Paint or EditorBrushTool.Dig or EditorBrushTool.Erase;
        }

        if (settings.Tool is not EditorBrushTool.Paint and
            not EditorBrushTool.Dig and
            not EditorBrushTool.Erase)
        {
            return false;
        }

        // §7.4 / plan 19：世界画刷允许跨 chunk，但只对当前驻留 chunk 写入。
        // 按 chunk 拆分矩形，既避免未驻留坐标抛出，也保留每个驻留子矩形的批量 SoA 路径。
        ChunkCoord minCoord = CellAddressing.WorldToChunk(minX, minY);
        ChunkCoord maxCoord = CellAddressing.WorldToChunk(maxX, maxY);
        for (int cy = minCoord.Y; cy <= maxCoord.Y; cy++)
        {
            for (int cx = minCoord.X; cx <= maxCoord.X; cx++)
            {
                int chunkMinX = cx * EngineConstants.ChunkSize;
                int chunkMinY = cy * EngineConstants.ChunkSize;
                int runMinX = Math.Max(minX, chunkMinX);
                int runMinY = Math.Max(minY, chunkMinY);
                int runMaxX = Math.Min(maxX, chunkMinX + EngineConstants.ChunkSize - 1);
                int runMaxY = Math.Min(maxY, chunkMinY + EngineConstants.ChunkSize - 1);
                int cellCount = (runMaxX - runMinX + 1) * (runMaxY - runMinY + 1);
                if (!CanEditCell(runMinX, runMinY))
                {
                    skippedNonResidentCells += cellCount;
                    continue;
                }

                writes += ApplyBulkRect(runMinX, runMinY, runMaxX, runMaxY, settings);
            }
        }

        return true;
    }

    private int ApplyBulkRect(int minX, int minY, int maxX, int maxY, MaterialBrushSettings settings)
    {
        return settings.Tool switch
        {
            EditorBrushTool.Paint => _editApi.PaintRect(minX, minY, maxX, maxY, settings.MaterialId),
            EditorBrushTool.Dig or EditorBrushTool.Erase => _editApi.ClearRect(minX, minY, maxX, maxY),
            EditorBrushTool.Temperature => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Tool, "未知画刷工具。"),
        };
    }

    private bool CanEditCell(int x, int y)
    {
        return _inspectApi is null || _inspectApi.TryInspectCell(x, y, out _);
    }

    private static bool Contains(EditorBrushShape shape, int radiusX, int radiusY, int dx, int dy)
    {
        return shape switch
        {
            EditorBrushShape.Point => dx == 0 && dy == 0,
            EditorBrushShape.Circle => ContainsEllipse(radiusX, radiusY, dx, dy),
            EditorBrushShape.Square => true,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "未知画刷形状。"),
        };
    }

    private static bool ContainsEllipse(int radiusX, int radiusY, int dx, int dy)
    {
        if (radiusX == 0 || radiusY == 0)
        {
            return radiusX == 0
                ? dx == 0 && Math.Abs(dy) <= radiusY
                : dy == 0 && Math.Abs(dx) <= radiusX;
        }

        long radiusXSquared = (long)radiusX * radiusX;
        long radiusYSquared = (long)radiusY * radiusY;
        return ((long)dx * dx * radiusYSquared) + ((long)dy * dy * radiusXSquared) <=
            radiusXSquared * radiusYSquared;
    }

    private void ApplyCell(int x, int y, MaterialBrushSettings settings)
    {
        switch (settings.Tool)
        {
            case EditorBrushTool.Paint:
                _editApi.PaintCell(x, y, settings.MaterialId);
                break;
            case EditorBrushTool.Dig:
            case EditorBrushTool.Erase:
                _editApi.ClearCell(x, y);
                break;
            case EditorBrushTool.Temperature:
                if (settings.TemperatureMode == TemperatureBrushMode.Target)
                {
                    _editApi.SetTemperature(x, y, settings.TemperatureCelsius);
                }
                else
                {
                    _editApi.AddTemperature(x, y, settings.TemperatureCelsius);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.Tool, "未知画刷工具。");
        }
    }

    private bool PassesProbability(int x, int y, float probability)
    {
        if (probability <= 0f)
        {
            return false;
        }

        if (probability >= 1f)
        {
            return true;
        }

        uint hash = Hash(x, y, _seed);
        return (hash / (float)uint.MaxValue) <= probability;
    }

    private static uint Hash(int x, int y, uint seed)
    {
        uint value = seed ^ ((uint)x * 0x85EBCA6Bu) ^ ((uint)y * 0xC2B2AE35u);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}
