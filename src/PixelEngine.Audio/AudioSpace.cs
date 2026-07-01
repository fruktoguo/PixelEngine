using System.Numerics;

namespace PixelEngine.Audio;

/// <summary>
/// 将 PixelEngine 2D cell 坐标转换到 OpenAL 米空间。
/// </summary>
public readonly struct AudioSpace
{
    private readonly float _pixelsPerMeter;

    /// <summary>
    /// 创建音频空间换算器。
    /// </summary>
    /// <param name="pixelsPerMeter">每米对应的 cell 数。</param>
    public AudioSpace(float pixelsPerMeter)
    {
        if (!float.IsFinite(pixelsPerMeter) || pixelsPerMeter <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerMeter), "PixelsPerMeter 必须为正有限数。");
        }

        _pixelsPerMeter = pixelsPerMeter;
    }

    /// <summary>
    /// 将世界 cell 坐标转换为 OpenAL 位置，Z 固定为 0。
    /// </summary>
    /// <param name="cellX">世界 cell X。</param>
    /// <param name="cellY">世界 cell Y。</param>
    /// <returns>OpenAL 米空间位置。</returns>
    public Vector3 ToMeters(float cellX, float cellY)
    {
        return new Vector3(cellX / _pixelsPerMeter, cellY / _pixelsPerMeter, 0f);
    }
}
