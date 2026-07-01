using PixelEngine.Core;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// 根据目标像素尺寸与 plan/09 固定 work group 尺寸计算 compute dispatch group。
/// </summary>
public static class GpuComputeDispatchGrid
{
    /// <summary>
    /// X 方向 local size。
    /// </summary>
    public const int LocalSizeX = EngineConstants.GpuComputeWorkGroupSizeX;

    /// <summary>
    /// Y 方向 local size。
    /// </summary>
    public const int LocalSizeY = EngineConstants.GpuComputeWorkGroupSizeY;

    /// <summary>
    /// Z 方向 local size。
    /// </summary>
    public const int LocalSizeZ = EngineConstants.GpuComputeWorkGroupSizeZ;

    /// <summary>
    /// 计算覆盖指定二维输出尺寸所需的 work group 数。
    /// </summary>
    /// <param name="width">输出宽度。</param>
    /// <param name="height">输出高度。</param>
    /// <returns>dispatch group 数。</returns>
    public static ComputeDispatchSize ForTexture2D(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return new ComputeDispatchSize(
            CeilDiv(width, LocalSizeX),
            CeilDiv(height, LocalSizeY),
            LocalSizeZ);
    }

    /// <summary>
    /// 判断固定 work group 尺寸是否不超过当前 GPU 能力。
    /// </summary>
    /// <param name="capabilities">GPU compute 能力。</param>
    /// <returns>是否可用。</returns>
    public static bool IsLocalSizeSupported(in GpuCapabilities capabilities)
    {
        return
            !ExceedsDeviceLimit(LocalSizeX, capabilities.MaxWorkGroupSizeX) &&
            !ExceedsDeviceLimit(LocalSizeY, capabilities.MaxWorkGroupSizeY) &&
            !ExceedsDeviceLimit(LocalSizeZ, capabilities.MaxWorkGroupSizeZ);
    }

    /// <summary>
    /// 验证固定 work group 尺寸不超过当前 GPU 能力。
    /// </summary>
    /// <param name="capabilities">GPU compute 能力。</param>
    public static void ValidateLocalSize(in GpuCapabilities capabilities)
    {
        if (ExceedsDeviceLimit(LocalSizeX, capabilities.MaxWorkGroupSizeX))
        {
            throw new InvalidOperationException("GPU compute work group X 尺寸超过当前设备限制。");
        }

        if (ExceedsDeviceLimit(LocalSizeY, capabilities.MaxWorkGroupSizeY))
        {
            throw new InvalidOperationException("GPU compute work group Y 尺寸超过当前设备限制。");
        }

        if (ExceedsDeviceLimit(LocalSizeZ, capabilities.MaxWorkGroupSizeZ))
        {
            throw new InvalidOperationException("GPU compute work group Z 尺寸超过当前设备限制。");
        }
    }

    private static uint CeilDiv(int value, int divisor)
    {
        return (uint)((value + divisor - 1) / divisor);
    }

    private static bool ExceedsDeviceLimit(int localSize, int maxSize)
    {
        return maxSize > 0 && localSize > maxSize;
    }
}
