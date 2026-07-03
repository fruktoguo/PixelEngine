namespace PixelEngine.Rendering.Compute;

/// <summary>
/// ComputeSharp/DX12 可消费的渲染资源契约描述。
/// </summary>
/// <remarks>
/// 本类型只表达 D3D-only 或 GL-DX12 shared resource/fence 的合法资源边界，不持有 ComputeSharp 类型，
/// 也不把 OpenGL texture name 解释为 DX12 resource。真实后端实现仍需在此契约之上接入 D3D barrier 与 queue/fence 同步。
/// </remarks>
public sealed class ComputeSharpResourceContract
{
    private ComputeSharpResourceContract(
        GpuResourceContractKind kind,
        int width,
        int height,
        nint deviceHandle,
        nint commandQueueHandle,
        nint worldResource,
        nint emissiveResource,
        nint occluderResource,
        nint visibilityResource,
        nint sceneResource,
        nint litResource,
        nint postAResource,
        nint postBResource,
        nint fenceHandle)
    {
        if (kind == GpuResourceContractKind.OpenGlTextureNames)
        {
            throw new ArgumentException("ComputeSharp/DX12 资源契约不能使用 OpenGL texture name。", nameof(kind));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "ComputeSharp 资源尺寸必须为正数。");
        }

        ValidateHandle(deviceHandle, nameof(deviceHandle));
        ValidateHandle(commandQueueHandle, nameof(commandQueueHandle));
        ValidateHandle(worldResource, nameof(worldResource));
        ValidateHandle(emissiveResource, nameof(emissiveResource));
        ValidateHandle(occluderResource, nameof(occluderResource));
        ValidateHandle(visibilityResource, nameof(visibilityResource));
        ValidateHandle(sceneResource, nameof(sceneResource));
        ValidateHandle(litResource, nameof(litResource));
        ValidateHandle(postAResource, nameof(postAResource));
        ValidateHandle(postBResource, nameof(postBResource));
        ValidateHandle(fenceHandle, nameof(fenceHandle));

        Kind = kind;
        Width = width;
        Height = height;
        DeviceHandle = deviceHandle;
        CommandQueueHandle = commandQueueHandle;
        WorldResource = worldResource;
        EmissiveResource = emissiveResource;
        OccluderResource = occluderResource;
        VisibilityResource = visibilityResource;
        SceneResource = sceneResource;
        LitResource = litResource;
        PostAResource = postAResource;
        PostBResource = postBResource;
        FenceHandle = fenceHandle;
    }

    /// <summary>资源契约类型。</summary>
    public GpuResourceContractKind Kind { get; }

    /// <summary>资源宽度。</summary>
    public int Width { get; }

    /// <summary>资源高度。</summary>
    public int Height { get; }

    /// <summary>D3D device 或共享资源导入层的设备句柄。</summary>
    public nint DeviceHandle { get; }

    /// <summary>D3D command queue 或共享资源同步队列句柄。</summary>
    public nint CommandQueueHandle { get; }

    /// <summary>世界纹理对应的 D3D resource/shared resource 句柄。</summary>
    public nint WorldResource { get; }

    /// <summary>emissive buffer 对应的 D3D resource/shared resource 句柄。</summary>
    public nint EmissiveResource { get; }

    /// <summary>occluder map 对应的 D3D resource/shared resource 句柄。</summary>
    public nint OccluderResource { get; }

    /// <summary>visibility/fog buffer 对应的 D3D resource/shared resource 句柄。</summary>
    public nint VisibilityResource { get; }

    /// <summary>scene target 对应的 D3D resource/shared resource 句柄。</summary>
    public nint SceneResource { get; }

    /// <summary>lighting target 对应的 D3D resource/shared resource 句柄。</summary>
    public nint LitResource { get; }

    /// <summary>post-process A target 对应的 D3D resource/shared resource 句柄。</summary>
    public nint PostAResource { get; }

    /// <summary>post-process B target 对应的 D3D resource/shared resource 句柄。</summary>
    public nint PostBResource { get; }

    /// <summary>D3D fence 或 GL-DX12 shared fence 句柄。</summary>
    public nint FenceHandle { get; }

    /// <summary>
    /// 创建指定类型的 ComputeSharp/DX12 资源契约。
    /// </summary>
    public static ComputeSharpResourceContract Create(
        GpuResourceContractKind kind,
        int width,
        int height,
        nint deviceHandle,
        nint commandQueueHandle,
        nint worldResource,
        nint emissiveResource,
        nint occluderResource,
        nint visibilityResource,
        nint sceneResource,
        nint litResource,
        nint postAResource,
        nint postBResource,
        nint fenceHandle)
    {
        return new ComputeSharpResourceContract(
            kind,
            width,
            height,
            deviceHandle,
            commandQueueHandle,
            worldResource,
            emissiveResource,
            occluderResource,
            visibilityResource,
            sceneResource,
            litResource,
            postAResource,
            postBResource,
            fenceHandle);
    }

    /// <summary>
    /// 创建 D3D-only 渲染后端资源契约。
    /// </summary>
    public static ComputeSharpResourceContract CreateD3D12(
        int width,
        int height,
        nint deviceHandle,
        nint commandQueueHandle,
        nint worldResource,
        nint emissiveResource,
        nint occluderResource,
        nint visibilityResource,
        nint sceneResource,
        nint litResource,
        nint postAResource,
        nint postBResource,
        nint fenceHandle)
    {
        return Create(
            GpuResourceContractKind.D3D12RenderGraph,
            width,
            height,
            deviceHandle,
            commandQueueHandle,
            worldResource,
            emissiveResource,
            occluderResource,
            visibilityResource,
            sceneResource,
            litResource,
            postAResource,
            postBResource,
            fenceHandle);
    }

    /// <summary>
    /// 创建显式 GL-DX12 shared resource/fence 契约。
    /// </summary>
    public static ComputeSharpResourceContract CreateGlDx12Shared(
        int width,
        int height,
        nint deviceHandle,
        nint commandQueueHandle,
        nint worldResource,
        nint emissiveResource,
        nint occluderResource,
        nint visibilityResource,
        nint sceneResource,
        nint litResource,
        nint postAResource,
        nint postBResource,
        nint fenceHandle)
    {
        return Create(
            GpuResourceContractKind.GlDx12SharedResources,
            width,
            height,
            deviceHandle,
            commandQueueHandle,
            worldResource,
            emissiveResource,
            occluderResource,
            visibilityResource,
            sceneResource,
            litResource,
            postAResource,
            postBResource,
            fenceHandle);
    }

    private static void ValidateHandle(nint handle, string parameterName)
    {
        if (handle == 0)
        {
            throw new ArgumentException("ComputeSharp 资源契约句柄不能为 0。", parameterName);
        }
    }
}
