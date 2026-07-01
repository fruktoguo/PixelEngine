using System.Runtime.InteropServices;
using PixelEngine.Core;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// plan/09 §4.5 自由粒子 GPU point-sprite 批绘器。只读 CPU 粒子活跃前缀，不修改模拟权威状态。
/// </summary>
public sealed unsafe class GpuParticleRenderer : IDisposable
{
    /// <summary>
    /// 上传到 VBO 的单粒子顶点字节数。
    /// </summary>
    public const int VertexStrideBytes = 10 * sizeof(float);

    private const int PositionOffset = 0;
    private const int ColorOffset = 2;
    private const int EmissiveOffset = 6;
    private const int RadiusOffset = 7;
    private const int MaterialOffset = 8;
    private const int ColorVariantOffset = 9;

    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly GlBuffer _vertexBuffer;
    private readonly uint _vao;
    private readonly int _cameraWorldOriginLocation;
    private readonly int _viewportSizeLocation;
    private readonly int _cellsPerPixelLocation;
    private readonly int _emissivePassLocation;
    private GpuParticleVertex[] _vertices;
    private bool _disposed;

    /// <summary>
    /// 创建 GPU 粒子渲染器。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    /// <param name="initialCapacity">初始 VBO 与 pinned staging 容量。</param>
    public GpuParticleRenderer(GL gl, GlslProfile profile, int initialCapacity = EngineConstants.ParticleCapacityDefault)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "GPU 粒子初始容量必须为正数。");
        }

        _gl = gl;
        _program = ShaderProgram.Create(gl, GpuParticleShaderSources.Vertex(profile), GpuParticleShaderSources.Fragment(profile));
        _cameraWorldOriginLocation = _program.GetUniformLocation("uCameraWorldOrigin");
        _viewportSizeLocation = _program.GetUniformLocation("uViewportSize");
        _cellsPerPixelLocation = _program.GetUniformLocation("uCellsPerPixel");
        _emissivePassLocation = _program.GetUniformLocation("uEmissivePass");
        _vertices = GC.AllocateArray<GpuParticleVertex>(initialCapacity, pinned: true);
        _vao = gl.GenVertexArray();
        _vertexBuffer = new GlBuffer(gl, BufferTargetARB.ArrayBuffer);

        gl.BindVertexArray(_vao);
        _vertexBuffer.Allocate((nuint)(_vertices.Length * VertexStrideBytes), BufferUsageARB.DynamicDraw);
        ConfigureVertexAttributes(gl);
    }

    /// <summary>
    /// 当前已分配的粒子容量。
    /// </summary>
    public int Capacity => _vertices.Length;

    /// <summary>
    /// 上传并绘制活跃粒子前缀。scene pass 使用 alpha 混合；emissive pass 使用加色混合写入 bloom 输入。
    /// </summary>
    public void Render(
        ReadOnlySpan<Particle> particles,
        MaterialTable materials,
        CameraState camera,
        ColorRenderTarget scene,
        EmissiveBuffer emissive)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(emissive);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (scene.Width != emissive.Width || scene.Height != emissive.Height)
        {
            throw new ArgumentException("scene 与 emissive 尺寸必须一致。", nameof(emissive));
        }

        if (camera.ViewportWidth != scene.Width || camera.ViewportHeight != scene.Height)
        {
            throw new ArgumentException("Camera viewport 必须与粒子渲染目标一致。", nameof(camera));
        }

        if (particles.IsEmpty)
        {
            return;
        }

        EnsureCapacity(particles.Length);
        FillVertices(particles, materials, camera);
        UploadVertices(particles.Length);

        _gl.Viewport(0, 0, (uint)scene.Width, (uint)scene.Height);
        _program.Use();
        _gl.Uniform2(_cameraWorldOriginLocation, camera.OriginWorldX, camera.OriginWorldY);
        _gl.Uniform2(_viewportSizeLocation, scene.Width, scene.Height);
        _gl.Uniform1(_cellsPerPixelLocation, camera.CellsPerPixel);
        _gl.BindVertexArray(_vao);
        _gl.Enable(EnableCap.ProgramPointSize);
        _gl.Enable(EnableCap.Blend);

        scene.BindFramebuffer();
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Uniform1(_emissivePassLocation, 0);
        _gl.DrawArrays(PrimitiveType.Points, 0, (uint)particles.Length);

        emissive.BindFramebuffer();
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        _gl.Uniform1(_emissivePassLocation, 1);
        _gl.DrawArrays(PrimitiveType.Points, 0, (uint)particles.Length);

        _gl.Disable(EnableCap.Blend);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _vertexBuffer.Dispose();
        _gl.DeleteVertexArray(_vao);
        _program.Dispose();
        _disposed = true;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _vertices.Length)
        {
            return;
        }

        int capacity = _vertices.Length;
        while (capacity < required)
        {
            capacity = checked(capacity * 2);
        }

        _vertices = GC.AllocateArray<GpuParticleVertex>(capacity, pinned: true);
        _vertexBuffer.Allocate((nuint)(capacity * VertexStrideBytes), BufferUsageARB.DynamicDraw);
    }

    private void FillVertices(ReadOnlySpan<Particle> particles, MaterialTable materials, CameraState camera)
    {
        float pointSize = MathF.Max(1f, 1f / camera.CellsPerPixel);
        for (int i = 0; i < particles.Length; i++)
        {
            Particle particle = particles[i];
            ref readonly MaterialDef material = ref materials.Get(particle.Material);
            ColorToRgba(ApplyVariant(material.BaseColorBGRA, particle.ColorVariant, material.ColorNoise), out float r, out float g, out float b, out float a);
            _vertices[i] = new GpuParticleVertex
            {
                X = particle.X,
                Y = particle.Y,
                R = r,
                G = g,
                B = b,
                A = a,
                Emissive = IsEmissive(in material) ? 1f : 0f,
                RadiusPixels = pointSize,
                MaterialId = particle.Material,
                ColorVariant = particle.ColorVariant,
            };
        }
    }

    private void UploadVertices(int count)
    {
        _vertexBuffer.Bind();
        ref GpuParticleVertex first = ref MemoryMarshal.GetArrayDataReference(_vertices);
        fixed (GpuParticleVertex* data = &first)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(count * VertexStrideBytes), data);
        }
    }

    private void ConfigureVertexAttributes(GL gl)
    {
        const uint stride = VertexStrideBytes;
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)(PositionOffset * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(ColorOffset * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, (void*)(EmissiveOffset * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(RadiusOffset * sizeof(float)));
        gl.EnableVertexAttribArray(4);
        gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, (void*)(MaterialOffset * sizeof(float)));
        gl.EnableVertexAttribArray(5);
        gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, (void*)(ColorVariantOffset * sizeof(float)));
    }

    private static bool IsEmissive(in MaterialDef material)
    {
        return (material.PropertyFlags & MaterialProperty.Emissive) != 0 || material.Type == CellType.Fire;
    }

    private static uint ApplyVariant(uint bgra, byte variant, byte materialNoise)
    {
        if (variant == 0 && materialNoise == 0)
        {
            return bgra;
        }

        int delta = (variant - 128) * Math.Max(1, (int)materialNoise) / 255;
        byte b = Adjust((byte)(bgra & 0xFF), delta);
        byte g = Adjust((byte)((bgra >> 8) & 0xFF), delta);
        byte r = Adjust((byte)((bgra >> 16) & 0xFF), delta);
        byte a = (byte)((bgra >> 24) & 0xFF);
        return b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    private static byte Adjust(byte value, int delta)
    {
        return (byte)Math.Clamp(value + delta, 0, 255);
    }

    private static void ColorToRgba(uint bgra, out float r, out float g, out float b, out float a)
    {
        const float scale = 1f / 255f;
        b = (byte)(bgra & 0xFF) * scale;
        g = (byte)((bgra >> 8) & 0xFF) * scale;
        r = (byte)((bgra >> 16) & 0xFF) * scale;
        a = (byte)((bgra >> 24) & 0xFF) * scale;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuParticleVertex
    {
        public float X;
        public float Y;
        public float R;
        public float G;
        public float B;
        public float A;
        public float Emissive;
        public float RadiusPixels;
        public float MaterialId;
        public float ColorVariant;
    }
}
