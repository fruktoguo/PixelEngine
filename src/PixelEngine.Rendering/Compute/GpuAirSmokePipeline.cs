using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// CP-A1 非权威 air/smoke Margolus 扩散 compute dispatch。只操作渲染侧 density texture，不读写 CPU 权威网格。
/// </summary>
public sealed class GpuAirSmokePipeline
{
    private readonly IComputeBackend _backend;
    private readonly ComputeKernel _margolus;

    /// <summary>
    /// 加载 air/smoke compute kernel。
    /// </summary>
    public GpuAirSmokePipeline(IComputeBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _margolus = _backend.LoadKernel(GpuComputeShaderSources.AirDiffuseMargolusName, GpuComputeShaderSources.GetSource(GpuComputeShaderSources.AirDiffuseMargolusName));
    }

    /// <summary>
    /// 执行一次 Margolus 2×2 block 扩散。parity 由调用者逐步翻转。
    /// </summary>
    public void DispatchMargolusStep(
        uint sourceDensity,
        uint destinationDensity,
        int width,
        int height,
        int parity,
        AirSmokeSettings settings)
    {
        settings = settings.Validate();
        ValidateHandles(sourceDensity, destinationDensity);
        // Margolus 以 2×2 block 为调度单位，dispatch grid 覆盖 ceil(w/2)×ceil(h/2)。
        ComputeDispatchSize groups = GpuComputeDispatchGrid.ForTexture2D((width + 1) / 2, (height + 1) / 2);
        // 绑定密度读写 image：source 只读、destination 只写，parity 控制棋盘格相位。
        _backend.BindImage(0, sourceDensity, level: 0, layered: false, layer: 0, GLEnum.ReadOnly, GLEnum.R16f);
        _backend.BindImage(1, destinationDensity, level: 0, layered: false, layer: 0, GLEnum.WriteOnly, GLEnum.R16f);
        _backend.SetUniform2(_margolus, "uOutputSize", width, height);
        _backend.SetUniform1(_margolus, "uParity", parity & 1);
        _backend.SetUniform1(_margolus, "uDiffusion", settings.Diffusion);
        _backend.Dispatch(_margolus, groups.GroupsX, groups.GroupsY, groups.GroupsZ);
        // 屏障保证下一 pass 或 fragment 采样能读到最新密度场。
        _backend.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    private static void ValidateHandles(uint sourceDensity, uint destinationDensity)
    {
        if (sourceDensity == 0 || destinationDensity == 0)
        {
            throw new ArgumentException("air/smoke density texture 句柄不能为 0。");
        }
    }
}
