using PixelEngine.Simulation;

namespace PixelEngine.Editor;

/// <summary>
/// 将材质/温度画刷应用到世界坐标。
/// </summary>
public sealed class MaterialBrushApplicator(ISimulationEditApi editApi, uint seed = 0x9E3779B9u)
{
    private readonly ISimulationEditApi _editApi = editApi ?? throw new ArgumentNullException(nameof(editApi));
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
        ArgumentNullException.ThrowIfNull(settings);
        int radius = settings.ClampedRadius;
        if (TryApplyBulkRect(centerX, centerY, radius, settings, out int bulkWrites))
        {
            return bulkWrites;
        }

        int writes = 0;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (!Contains(settings.Shape, radius, dx, dy))
                {
                    continue;
                }

                int x = centerX + dx;
                int y = centerY + dy;
                if (!PassesProbability(x, y, settings.ClampedProbability))
                {
                    continue;
                }

                ApplyCell(x, y, settings);
                writes++;
            }
        }

        return writes;
    }

    private bool TryApplyBulkRect(int centerX, int centerY, int radius, MaterialBrushSettings settings, out int writes)
    {
        writes = 0;
        if (settings.Shape != EditorBrushShape.Square || settings.ClampedProbability < 1f)
        {
            return false;
        }

        int minX = centerX - radius;
        int minY = centerY - radius;
        int maxX = centerX + radius;
        int maxY = centerY + radius;
        switch (settings.Tool)
        {
            case EditorBrushTool.Paint:
                writes = _editApi.PaintRect(minX, minY, maxX, maxY, settings.MaterialId);
                return true;
            case EditorBrushTool.Dig:
            case EditorBrushTool.Erase:
                writes = _editApi.ClearRect(minX, minY, maxX, maxY);
                return true;
            case EditorBrushTool.Temperature:
                return false;
            default:
                return false;
        }
    }

    private static bool Contains(EditorBrushShape shape, int radius, int dx, int dy)
    {
        return shape switch
        {
            EditorBrushShape.Point => dx == 0 && dy == 0,
            EditorBrushShape.Circle => (dx * dx) + (dy * dy) <= radius * radius,
            EditorBrushShape.Square => true,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "未知画刷形状。"),
        };
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
