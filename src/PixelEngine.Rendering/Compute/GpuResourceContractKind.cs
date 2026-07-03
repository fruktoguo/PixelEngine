namespace PixelEngine.Rendering.Compute;

/// <summary>
/// 描述渲染资源句柄所属的 GPU API 契约。
/// </summary>
public enum GpuResourceContractKind
{
    /// <summary>
    /// OpenGL texture/image/SSBO name 契约。该契约只能由 GL compute 或 fragment fallback 消费。
    /// </summary>
    OpenGlTextureNames = 0,

    /// <summary>
    /// D3D12 渲染后端直接拥有的资源契约，可作为 ComputeSharp/DX12 的合法输入。
    /// </summary>
    D3D12RenderGraph = 1,

    /// <summary>
    /// 显式 GL-DX12 共享资源与 fence 契约，可作为 ComputeSharp/DX12 的合法输入。
    /// </summary>
    GlDx12SharedResources = 2,
}
