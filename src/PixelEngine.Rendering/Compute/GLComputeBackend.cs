using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// Silk.NET OpenGL 4.3 compute shader 后端。
/// </summary>
public sealed class GLComputeBackend : IComputeBackend
{
    private readonly GL _gl;
    private readonly List<uint> _programs = [];
    private bool _disposed;
    private bool _timerQueryActive;

    /// <summary>
    /// 创建 GL compute 后端。
    /// </summary>
    /// <param name="gl">OpenGL 入口，必须属于 plan/08 已创建的上下文。</param>
    /// <param name="gate">compute 能力门控。</param>
    public GLComputeBackend(GL gl, ComputeCapabilityGate gate)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (!gate.GlComputeAvailable)
        {
            throw new InvalidOperationException("当前 GL 上下文未通过 G1 compute 门控。");
        }

        _gl = gl;
    }

    /// <inheritdoc />
    public ComputeBackendKind Kind => ComputeBackendKind.GlCompute;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public ComputeKernel LoadKernel(string name, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint shader = CompileComputeShader(source);
        uint program = _gl.CreateProgram();
        try
        {
            _gl.AttachShader(program, shader);
            _gl.LinkProgram(program);
            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0)
            {
                string info = _gl.GetProgramInfoLog(program);
                throw new InvalidOperationException($"Compute kernel '{name}' 链接失败: {info}");
            }

            _programs.Add(program);
            return new ComputeKernel(name, program);
        }
        catch
        {
            _gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            _gl.DeleteShader(shader);
        }
    }

    /// <inheritdoc />
    public void BindStorageBuffer(uint bindingIndex, uint bufferHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, bindingIndex, bufferHandle);
    }

    /// <inheritdoc />
    public void BindImage(uint unit, uint textureHandle, int level, bool layered, int layer, GLEnum access, GLEnum format)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.BindImageTexture(unit, textureHandle, level, layered, layer, access, format);
    }

    /// <inheritdoc />
    public void Dispatch(ComputeKernel kernel, uint groupsX, uint groupsY, uint groupsZ)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (kernel.Handle == 0)
        {
            throw new ArgumentException("GL compute kernel 句柄无效。", nameof(kernel));
        }

        _gl.UseProgram(kernel.Handle);
        _gl.DispatchCompute(groupsX, groupsY, groupsZ);
    }

    /// <inheritdoc />
    public void MemoryBarrier(MemoryBarrierMask barriers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.MemoryBarrier(barriers);
    }

    /// <inheritdoc />
    public uint BeginTimerQuery(string passName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_timerQueryActive)
        {
            throw new InvalidOperationException("已有 GPU timer query 处于开启状态。");
        }

        uint query = _gl.GenQuery();
        _gl.BeginQuery(QueryTarget.TimeElapsed, query);
        _timerQueryActive = true;
        return query;
    }

    /// <inheritdoc />
    public void EndTimerQuery()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_timerQueryActive)
        {
            throw new InvalidOperationException("没有开启中的 GPU timer query。");
        }

        _gl.EndQuery(QueryTarget.TimeElapsed);
        _timerQueryActive = false;
    }

    /// <inheritdoc />
    public bool TryGetTimerResult(uint queryHandle, out ulong elapsedNanoseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (queryHandle == 0)
        {
            elapsedNanoseconds = 0;
            return false;
        }

        _gl.GetQueryObject(queryHandle, QueryObjectParameterName.ResultAvailable, out int available);
        if (available == 0)
        {
            elapsedNanoseconds = 0;
            return false;
        }

        _gl.GetQueryObject(queryHandle, QueryObjectParameterName.Result, out elapsedNanoseconds);
        _gl.DeleteQuery(queryHandle);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (uint program in _programs)
        {
            _gl.DeleteProgram(program);
        }

        _programs.Clear();
        _disposed = true;
    }

    private uint CompileComputeShader(string source)
    {
        uint shader = _gl.CreateShader(ShaderType.ComputeShader);
        try
        {
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);
            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
            if (compiled == 0)
            {
                string info = _gl.GetShaderInfoLog(shader);
                throw new InvalidOperationException($"Compute shader 编译失败: {info}");
            }

            return shader;
        }
        catch
        {
            _gl.DeleteShader(shader);
            throw;
        }
    }
}
