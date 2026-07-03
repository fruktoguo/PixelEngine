namespace PixelEngine.Rendering.Compute;

/// <summary>
/// plan/09 与 plan/08 共享的 GPU 资源句柄集合。
/// </summary>
/// <remarks>
/// 本类型只保存渲染资源句柄，不拥有 GL 上下文，也不读取 CPU 权威模拟数据；resize 时由 plan/08 重建并重新传入。
/// </remarks>
public sealed class GpuComputeResources
{
    /// <summary>
    /// 构造共享资源集合。
    /// </summary>
    public GpuComputeResources(
        int width,
        int height,
        uint worldTexture,
        uint emissiveTexture,
        uint occluderTexture,
        uint visibilityTexture,
        uint sceneTexture,
        uint litTexture,
        uint postATexture,
        uint postBTexture)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "compute 资源尺寸必须为正数。");
        }

        ValidateHandle(worldTexture, nameof(worldTexture));
        ValidateHandle(emissiveTexture, nameof(emissiveTexture));
        ValidateHandle(occluderTexture, nameof(occluderTexture));
        ValidateHandle(visibilityTexture, nameof(visibilityTexture));
        ValidateHandle(sceneTexture, nameof(sceneTexture));
        ValidateHandle(litTexture, nameof(litTexture));
        ValidateHandle(postATexture, nameof(postATexture));
        ValidateHandle(postBTexture, nameof(postBTexture));

        if (worldTexture == 0 ||
            emissiveTexture == 0 ||
            occluderTexture == 0 ||
            visibilityTexture == 0 ||
            sceneTexture == 0 ||
            litTexture == 0 ||
            postATexture == 0 ||
            postBTexture == 0)
        {
            throw new ArgumentException("compute 资源句柄不能为 0。");
        }

        Width = width;
        Height = height;
        WorldTexture = worldTexture;
        EmissiveTexture = emissiveTexture;
        OccluderTexture = occluderTexture;
        VisibilityTexture = visibilityTexture;
        SceneTexture = sceneTexture;
        LitTexture = litTexture;
        PostATexture = postATexture;
        PostBTexture = postBTexture;
    }

    /// <summary>资源宽度。</summary>
    public int Width { get; }

    /// <summary>资源高度。</summary>
    public int Height { get; }

    /// <summary>资源句柄契约。当前 plan/08 OpenGL 渲染路径只暴露 GL texture name。</summary>
    public GpuResourceContractKind ResourceContractKind => GpuResourceContractKind.OpenGlTextureNames;

    /// <summary>当前资源是否可由 ComputeSharp/DX12 直接消费。</summary>
    public bool CanBeConsumedByComputeSharp => false;

    /// <summary>plan/08 世界纹理句柄。</summary>
    public uint WorldTexture { get; }

    /// <summary>emissive additive buffer 纹理句柄。</summary>
    public uint EmissiveTexture { get; }

    /// <summary>occluder/solidity map 纹理句柄。</summary>
    public uint OccluderTexture { get; }

    /// <summary>fog-of-war / visibility 纹理句柄。</summary>
    public uint VisibilityTexture { get; }

    /// <summary>scene 中间纹理句柄。</summary>
    public uint SceneTexture { get; }

    /// <summary>lighting 后中间纹理句柄。</summary>
    public uint LitTexture { get; }

    /// <summary>post-process A 纹理句柄，可作为 compute bloom mip/临时目标。</summary>
    public uint PostATexture { get; }

    /// <summary>post-process B 纹理句柄，可作为 compute bloom mip/临时目标。</summary>
    public uint PostBTexture { get; }

    private static void ValidateHandle(uint handle, string parameterName)
    {
        if (handle == 0)
        {
            throw new ArgumentException("compute 资源句柄不能为 0。", parameterName);
        }
    }
}
