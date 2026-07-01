using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// 空 compute 后端；用于 G3 基线回退，所有图形效果由 plan/08 的 fragment/CPU 路径执行。
/// </summary>
public sealed class NullComputeBackend : IComputeBackend
{
    /// <inheritdoc />
    public ComputeBackendKind Kind => ComputeBackendKind.Null;

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public ComputeKernel LoadKernel(string name, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        return new ComputeKernel(name, 0);
    }

    /// <inheritdoc />
    public void BindStorageBuffer(uint bindingIndex, uint bufferHandle)
    {
    }

    /// <inheritdoc />
    public void BindImage(uint unit, uint textureHandle, int level, bool layered, int layer, GLEnum access, GLEnum format)
    {
    }

    /// <inheritdoc />
    public void Dispatch(ComputeKernel kernel, uint groupsX, uint groupsY, uint groupsZ)
    {
    }

    /// <inheritdoc />
    public void MemoryBarrier(MemoryBarrierMask barriers)
    {
    }

    /// <inheritdoc />
    public uint BeginTimerQuery(string passName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        return 0;
    }

    /// <inheritdoc />
    public void EndTimerQuery()
    {
    }

    /// <inheritdoc />
    public bool TryGetTimerResult(uint queryHandle, out ulong elapsedNanoseconds)
    {
        elapsedNanoseconds = 0;
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
