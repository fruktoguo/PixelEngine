namespace PixelEngine.Simulation;

/// <summary>
/// 编辑器与脚本在输入相位使用的 Simulation 写入门面。
/// </summary>
public interface ISimulationEditApi
{
    /// <summary>
    /// 在 phase [1] 写入一个 cell，并让本帧 CA 可见。
    /// </summary>
    void PaintCell(int worldX, int worldY, ushort material);

    /// <summary>
    /// 在 phase [1] 批量写入世界坐标闭区间矩形，并让本帧 CA 可见。
    /// </summary>
    int PaintRect(int minX, int minY, int maxX, int maxY, ushort material);

    /// <summary>
    /// 在 phase [1] 清空一个 cell，并让本帧 CA 可见。
    /// </summary>
    void ClearCell(int worldX, int worldY);

    /// <summary>
    /// 在 phase [1] 批量清空世界坐标闭区间矩形，并让本帧 CA 可见。
    /// </summary>
    int ClearRect(int minX, int minY, int maxX, int maxY);

    /// <summary>
    /// 在粗温度场上叠加温度增量，并唤醒对应 dirty 区域。
    /// </summary>
    void AddTemperature(int worldX, int worldY, float deltaCelsius);

    /// <summary>
    /// 把粗温度场调整到目标温度，并唤醒对应 dirty 区域。
    /// </summary>
    void SetTemperature(int worldX, int worldY, float targetCelsius);
}
