namespace PixelEngine.Rendering.Compute;

/// <summary>
/// ComputeSharp 编译边界与 DX12 可用性探测。
/// </summary>
/// <remarks>
/// 默认构建不引用 ComputeSharp；只有显式设置 <c>EnableComputeSharpBackend=true</c> 时才编译
/// <c>PIXELENGINE_COMPUTESHARP</c> 分支并触碰 ComputeSharp 类型。该类型只负责探测 DX12/ComputeSharp
/// 是否存在，不代表资源契约或真实后端已可执行。
/// </remarks>
internal static class ComputeSharpSupport
{
    /// <summary>
    /// 当前构建是否编译进 ComputeSharp 程序集引用。
    /// </summary>
#if PIXELENGINE_COMPUTESHARP
    public const bool IsCompiled = true;
#else
    public const bool IsCompiled = false;
#endif

    /// <summary>
    /// 尝试探测当前 Windows 进程是否可创建 ComputeSharp DX12 device。
    /// </summary>
    /// <returns>ComputeSharp/DX12 device 是否可用。</returns>
    public static bool TryProbeDx12Device()
    {
#if PIXELENGINE_COMPUTESHARP
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            return false;
        }

        try
        {
            foreach (ComputeSharp.GraphicsDevice device in ComputeSharp.GraphicsDevice.EnumerateDevices())
            {
                using (device)
                {
                    if (device.IsHardwareAccelerated)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception)
        {
            return false;
        }
#endif

        return false;
    }
}
