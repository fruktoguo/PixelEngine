using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL shader program 封装，负责编译、链接与 uniform location 缓存。
/// </summary>
public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniformLocations = new(StringComparer.Ordinal);
    private bool _disposed;

    private ShaderProgram(GL gl, uint handle)
    {
        _gl = gl;
        Handle = handle;
    }

    /// <summary>
    /// OpenGL program 句柄。
    /// </summary>
    public uint Handle { get; }

    /// <summary>
    /// 编译并链接 vertex/fragment shader。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="vertexSource">vertex shader 源码。</param>
    /// <param name="fragmentSource">fragment shader 源码。</param>
    /// <returns>已链接 program。</returns>
    public static ShaderProgram Create(GL gl, string vertexSource, string fragmentSource)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(vertexSource);
        ArgumentNullException.ThrowIfNull(fragmentSource);

        uint vertex = Compile(gl, ShaderType.VertexShader, vertexSource);
        uint fragment = Compile(gl, ShaderType.FragmentShader, fragmentSource);
        uint program = gl.CreateProgram();
        try
        {
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0)
            {
                string info = gl.GetProgramInfoLog(program);
                throw new InvalidOperationException($"Shader program 链接失败: {info}");
            }

            return new ShaderProgram(gl, program);
        }
        catch
        {
            gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            gl.DeleteShader(vertex);
            gl.DeleteShader(fragment);
        }
    }

    /// <summary>
    /// 绑定该 shader program。
    /// </summary>
    public void Use()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.UseProgram(Handle);
    }

    /// <summary>
    /// 获取并缓存 uniform location。
    /// </summary>
    /// <param name="name">uniform 名称。</param>
    /// <returns>OpenGL uniform location。</returns>
    public int GetUniformLocation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_uniformLocations.TryGetValue(name, out int location))
        {
            return location;
        }

        location = _gl.GetUniformLocation(Handle, name);
        _uniformLocations.Add(name, location);
        return location;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteProgram(Handle);
        _disposed = true;
    }

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        try
        {
            gl.ShaderSource(shader, source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
            if (compiled == 0)
            {
                string info = gl.GetShaderInfoLog(shader);
                throw new InvalidOperationException($"{type} 编译失败: {info}");
            }

            return shader;
        }
        catch
        {
            gl.DeleteShader(shader);
            throw;
        }
    }
}
