using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// GPU compute 能力快照，启动期探测一次后缓存。
/// </summary>
public readonly record struct GpuCapabilities
{
    /// <summary>
    /// 构造能力快照。
    /// </summary>
    public GpuCapabilities(
        int glMajorVersion,
        int glMinorVersion,
        bool isGles,
        bool isAngle,
        bool hasComputeShader,
        bool hasShaderStorageBufferObject,
        bool hasShaderImageLoadStore,
        int maxWorkGroupCountX,
        int maxWorkGroupCountY,
        int maxWorkGroupCountZ,
        int maxWorkGroupSizeX,
        int maxWorkGroupSizeY,
        int maxWorkGroupSizeZ,
        bool isWindows,
        bool isDx12Available,
        bool isComputeSharpCompiled,
        bool hasComputeSharpResourceContract = false,
        GpuResourceContractKind computeSharpResourceContractKind = GpuResourceContractKind.OpenGlTextureNames)
    {
        if (hasComputeSharpResourceContract && computeSharpResourceContractKind == GpuResourceContractKind.OpenGlTextureNames)
        {
            throw new ArgumentException("ComputeSharp 资源契约不能声明为 OpenGL texture name。", nameof(computeSharpResourceContractKind));
        }

        if (hasComputeSharpResourceContract &&
            computeSharpResourceContractKind != GpuResourceContractKind.D3D12RenderGraph &&
            computeSharpResourceContractKind != GpuResourceContractKind.GlDx12SharedResources)
        {
            throw new ArgumentOutOfRangeException(
                nameof(computeSharpResourceContractKind),
                computeSharpResourceContractKind,
                "未知的 ComputeSharp/DX12 资源契约类型。");
        }

        GlMajorVersion = glMajorVersion;
        GlMinorVersion = glMinorVersion;
        IsGles = isGles;
        IsAngle = isAngle;
        HasComputeShader = hasComputeShader;
        HasShaderStorageBufferObject = hasShaderStorageBufferObject;
        HasShaderImageLoadStore = hasShaderImageLoadStore;
        MaxWorkGroupCountX = maxWorkGroupCountX;
        MaxWorkGroupCountY = maxWorkGroupCountY;
        MaxWorkGroupCountZ = maxWorkGroupCountZ;
        MaxWorkGroupSizeX = maxWorkGroupSizeX;
        MaxWorkGroupSizeY = maxWorkGroupSizeY;
        MaxWorkGroupSizeZ = maxWorkGroupSizeZ;
        IsWindows = isWindows;
        IsDx12Available = isDx12Available;
        IsComputeSharpCompiled = isComputeSharpCompiled;
        HasComputeSharpResourceContract = hasComputeSharpResourceContract;
        ComputeSharpResourceContractKind = computeSharpResourceContractKind;
    }

    /// <summary>GL 主版本号。</summary>
    public int GlMajorVersion { get; init; }

    /// <summary>GL 次版本号。</summary>
    public int GlMinorVersion { get; init; }

    /// <summary>当前上下文是否为 OpenGL ES。</summary>
    public bool IsGles { get; init; }

    /// <summary>当前上下文是否疑似 ANGLE。</summary>
    public bool IsAngle { get; init; }

    /// <summary>是否具备 compute shader。</summary>
    public bool HasComputeShader { get; init; }

    /// <summary>是否具备 shader storage buffer object。</summary>
    public bool HasShaderStorageBufferObject { get; init; }

    /// <summary>是否具备 shader image load/store。</summary>
    public bool HasShaderImageLoadStore { get; init; }

    /// <summary>X 方向最大 work group 数。</summary>
    public int MaxWorkGroupCountX { get; init; }

    /// <summary>Y 方向最大 work group 数。</summary>
    public int MaxWorkGroupCountY { get; init; }

    /// <summary>Z 方向最大 work group 数。</summary>
    public int MaxWorkGroupCountZ { get; init; }

    /// <summary>X 方向最大 local size。</summary>
    public int MaxWorkGroupSizeX { get; init; }

    /// <summary>Y 方向最大 local size。</summary>
    public int MaxWorkGroupSizeY { get; init; }

    /// <summary>Z 方向最大 local size。</summary>
    public int MaxWorkGroupSizeZ { get; init; }

    /// <summary>当前运行平台是否为 Windows。</summary>
    public bool IsWindows { get; init; }

    /// <summary>是否检测到 DX12 可用。当前实现保持显式注入，避免把 ComputeSharp 变成硬依赖。</summary>
    public bool IsDx12Available { get; init; }

    /// <summary>当前发行是否编译进 ComputeSharp 后端。</summary>
    public bool IsComputeSharpCompiled { get; init; }

    /// <summary>
    /// 是否已落地 D3D-only 或 GL-DX12 shared resource/fence 契约。未满足时 ComputeSharp 不得消费 GL texture name。
    /// </summary>
    public bool HasComputeSharpResourceContract { get; init; }

    /// <summary>
    /// ComputeSharp 可消费资源契约的类型；无契约时保持 <see cref="GpuResourceContractKind.OpenGlTextureNames"/>。
    /// </summary>
    public GpuResourceContractKind ComputeSharpResourceContractKind { get; init; }

    /// <summary>
    /// 从 plan/08 的 GL 能力快照构造 compute 基础快照。生产门控必须使用 <see cref="Query(GL, GlCapabilities)" /> 补齐 work group 限制。
    /// </summary>
    /// <param name="capabilities">GL 能力快照。</param>
    /// <returns>compute 能力快照。</returns>
    public static GpuCapabilities FromGlCapabilities(GlCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        bool hasSufficientGl = !capabilities.IsGles &&
            (capabilities.MajorVersion > 4 || (capabilities.MajorVersion == 4 && capabilities.MinorVersion >= 3));
        bool hasComputeExtension = capabilities.Extensions.Contains("GL_ARB_compute_shader", StringComparer.Ordinal);
        bool hasSsboExtension = capabilities.Extensions.Contains("GL_ARB_shader_storage_buffer_object", StringComparer.Ordinal);
        bool hasImageExtension = capabilities.Extensions.Contains("GL_ARB_shader_image_load_store", StringComparer.Ordinal);
        bool hasCompute = hasSufficientGl || hasComputeExtension || capabilities.HasComputeShader;
        return new GpuCapabilities(
            capabilities.MajorVersion,
            capabilities.MinorVersion,
            capabilities.IsGles,
            capabilities.IsAngle,
            hasCompute,
            hasSufficientGl || hasSsboExtension,
            hasSufficientGl || hasImageExtension,
            maxWorkGroupCountX: 0,
            maxWorkGroupCountY: 0,
            maxWorkGroupCountZ: 0,
            maxWorkGroupSizeX: 0,
            maxWorkGroupSizeY: 0,
            maxWorkGroupSizeZ: 0,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            ComputeSharpSupport.TryProbeDx12Device(),
            ComputeSharpSupport.IsCompiled);
    }

    /// <summary>
    /// 查询当前 GL 上下文的 compute 限制。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="capabilities">plan/08 GL 能力快照。</param>
    /// <returns>带 work group 限制的能力快照。</returns>
    public static GpuCapabilities Query(GL gl, GlCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(gl);
        GpuCapabilities baseCapabilities = FromGlCapabilities(capabilities);
        return !baseCapabilities.HasComputeShader
            ? baseCapabilities
            : baseCapabilities with
            {
                MaxWorkGroupCountX = GetIndexedInteger(gl, GLEnum.MaxComputeWorkGroupCount, 0),
                MaxWorkGroupCountY = GetIndexedInteger(gl, GLEnum.MaxComputeWorkGroupCount, 1),
                MaxWorkGroupCountZ = GetIndexedInteger(gl, GLEnum.MaxComputeWorkGroupCount, 2),
                MaxWorkGroupSizeX = GetIndexedInteger(gl, GLEnum.MaxComputeWorkGroupSize, 0),
                MaxWorkGroupSizeY = GetIndexedInteger(gl, GLEnum.MaxComputeWorkGroupSize, 1),
                MaxWorkGroupSizeZ = GetIndexedInteger(gl, GLEnum.MaxComputeWorkGroupSize, 2),
            };
    }

    private static int GetIndexedInteger(GL gl, GLEnum parameter, uint index)
    {
        gl.GetInteger(parameter, index, out int value);
        return value;
    }
}
