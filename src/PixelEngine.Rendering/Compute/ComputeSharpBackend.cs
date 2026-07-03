using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// ComputeSharp 后端隔离类型。当前默认发行未引用 ComputeSharp 程序集，因此该类型不触碰任何 ComputeSharp API。
/// </summary>
/// <remarks>
/// DX12 后端必须在 plan/15 打包与 AOT 策略明确后启用；当前类型只保证接口隔离和 G2 恒假时不会形成硬依赖。
/// 后续真实实现必须接收 <see cref="ComputeSharpResourceContract"/>，不得消费 <see cref="GpuComputeResources"/> 的 OpenGL texture name。
/// </remarks>
public sealed class ComputeSharpBackend : IComputeBackend
{
    /// <summary>
    /// 当前后端是否具备真实可执行实现。资源契约和 ComputeSharp 引用未落地前必须保持 false。
    /// </summary>
    public const bool IsExecutable = false;

    /// <inheritdoc />
    public ComputeBackendKind Kind => ComputeBackendKind.ComputeSharp;

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public ComputeKernel LoadKernel(string name, string source)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void BindStorageBuffer(uint bindingIndex, uint bufferHandle)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void BindTexture(uint unit, uint textureHandle)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void BindImage(uint unit, uint textureHandle, int level, bool layered, int layer, GLEnum access, GLEnum format)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void SetUniform1(ComputeKernel kernel, string name, int value)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void SetUniform1(ComputeKernel kernel, string name, float value)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void SetUniform2(ComputeKernel kernel, string name, int x, int y)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void SetUniform2(ComputeKernel kernel, string name, float x, float y)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void Dispatch(ComputeKernel kernel, uint groupsX, uint groupsY, uint groupsZ)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void MemoryBarrier(MemoryBarrierMask barriers)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public uint BeginTimerQuery(string passName)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void EndTimerQuery()
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public bool TryGetTimerResult(uint queryHandle, out ulong elapsedNanoseconds)
    {
        elapsedNanoseconds = 0;
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void DeleteTimerQuery(uint queryHandle)
    {
        throw CreateUnavailableException();
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static InvalidOperationException CreateUnavailableException()
    {
        return new InvalidOperationException("ComputeSharp 后端尚未具备 ComputeSharpResourceContract 资源契约、D3D/GL-DX12 同步和真实可执行实现；请使用 NullComputeBackend 或 GLComputeBackend。");
    }
}
