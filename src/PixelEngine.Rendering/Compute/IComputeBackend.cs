using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// GPU compute 后端统一接口，负责加载 kernel、绑定 SSBO/image、dispatch、memory barrier 与 GPU timer query。
/// </summary>
/// <remarks>
/// 本接口只服务渲染相位 10 的非权威 GPU 增强路径；任何实现都不得把 GPU 结果回读进 CPU 权威模拟网格。
/// </remarks>
public interface IComputeBackend : IDisposable
{
    /// <summary>
    /// 后端类型。
    /// </summary>
    ComputeBackendKind Kind { get; }

    /// <summary>
    /// 当前后端是否可执行 compute pass。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 编译或加载 compute kernel。
    /// </summary>
    /// <param name="name">诊断用 kernel 名称。</param>
    /// <param name="source">后端源码。GL 后端要求 GLSL <c>#version 430</c> compute shader。</param>
    /// <returns>已加载 kernel 句柄。</returns>
    ComputeKernel LoadKernel(string name, string source);

    /// <summary>
    /// 绑定 shader storage buffer object 到指定 binding index。
    /// </summary>
    /// <param name="bindingIndex">SSBO binding index。</param>
    /// <param name="bufferHandle">OpenGL buffer object 名称。当前绑定 API 是 GL-only 契约；非 GL 后端必须先落地 D3D resource owner 或 GL-DX12 shared resource/fence 层，不能直接解释该值。</param>
    void BindStorageBuffer(uint bindingIndex, uint bufferHandle);

    /// <summary>
    /// 绑定 2D texture 到指定 sampler texture unit。
    /// </summary>
    /// <param name="unit">texture unit。</param>
    /// <param name="textureHandle">OpenGL texture name。当前绑定 API 是 GL-only 契约；非 GL 后端必须先落地 D3D resource owner 或 GL-DX12 shared resource/fence 层，不能直接解释该值。</param>
    void BindTexture(uint unit, uint textureHandle);

    /// <summary>
    /// 绑定 image load/store 纹理到指定 image unit。
    /// </summary>
    /// <param name="unit">image unit。</param>
    /// <param name="textureHandle">OpenGL texture name。当前绑定 API 是 GL-only 契约；非 GL 后端必须先落地 D3D resource owner 或 GL-DX12 shared resource/fence 层，不能直接解释该值。</param>
    /// <param name="level">mip level。</param>
    /// <param name="layered">是否按 layered image 绑定。</param>
    /// <param name="layer">layered 为 false 时的 layer。</param>
    /// <param name="access">image 访问模式。</param>
    /// <param name="format">image 格式。</param>
    void BindImage(
        uint unit,
        uint textureHandle,
        int level,
        bool layered,
        int layer,
        GLEnum access,
        GLEnum format);

    /// <summary>
    /// 设置 int uniform。
    /// </summary>
    void SetUniform1(ComputeKernel kernel, string name, int value);

    /// <summary>
    /// 设置 float uniform。
    /// </summary>
    void SetUniform1(ComputeKernel kernel, string name, float value);

    /// <summary>
    /// 设置 ivec2 uniform。
    /// </summary>
    void SetUniform2(ComputeKernel kernel, string name, int x, int y);

    /// <summary>
    /// 设置 vec2 uniform。
    /// </summary>
    void SetUniform2(ComputeKernel kernel, string name, float x, float y);

    /// <summary>
    /// 使用指定 kernel dispatch compute 工作组。
    /// </summary>
    /// <param name="kernel">已加载 kernel。</param>
    /// <param name="groupsX">X 方向 work group 数。</param>
    /// <param name="groupsY">Y 方向 work group 数。</param>
    /// <param name="groupsZ">Z 方向 work group 数。</param>
    void Dispatch(ComputeKernel kernel, uint groupsX, uint groupsY, uint groupsZ);

    /// <summary>
    /// 插入 compute 与 graphics 之间的显式内存可见性屏障。
    /// </summary>
    /// <param name="barriers">OpenGL memory barrier bit；其它后端可映射到自身 barrier。</param>
    void MemoryBarrier(MemoryBarrierMask barriers);

    /// <summary>
    /// 开始一个 GPU timer query。
    /// </summary>
    /// <param name="passName">诊断用 pass 名称。</param>
    /// <returns>timer query 令牌；不可用后端返回 0。</returns>
    uint BeginTimerQuery(string passName);

    /// <summary>
    /// 结束当前 GPU timer query。
    /// </summary>
    void EndTimerQuery();

    /// <summary>
    /// 尝试读取上一帧或更早的 GPU timer query 结果。
    /// </summary>
    /// <param name="queryHandle">query 句柄。</param>
    /// <param name="elapsedNanoseconds">耗时，单位纳秒。</param>
    /// <returns>结果是否已经可用；实现不得为等待结果而阻塞 sim。</returns>
    bool TryGetTimerResult(uint queryHandle, out ulong elapsedNanoseconds);

    /// <summary>
    /// 删除尚未被读取的 GPU timer query。用于资源释放路径，不得阻塞等待结果。
    /// </summary>
    /// <param name="queryHandle">query 句柄；0 表示无操作。</param>
    void DeleteTimerQuery(uint queryHandle);
}
