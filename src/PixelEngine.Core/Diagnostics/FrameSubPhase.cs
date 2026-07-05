namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 表示诊断 HUD 可选显示的细分相位。
/// </summary>
public enum FrameSubPhase
{
    /// <summary>
    /// CA pass A。
    /// </summary>
    CaPassA,

    /// <summary>
    /// CA pass B。
    /// </summary>
    CaPassB,

    /// <summary>
    /// CA pass C。
    /// </summary>
    CaPassC,

    /// <summary>
    /// CA pass D。
    /// </summary>
    CaPassD,

    /// <summary>
    /// 物理步进。
    /// </summary>
    PhysicsStep,

    /// <summary>
    /// 刚体破坏 CCL、轮廓追踪与凸分解准备。
    /// </summary>
    PhysicsCcl,

    /// <summary>
    /// 刚体 Box2D body 销毁、重建与碎片写出。
    /// </summary>
    ShapeRebuild,

    /// <summary>
    /// 刚体旧 stamp 从权威网格擦除。
    /// </summary>
    PhysicsErase,

    /// <summary>
    /// 刚体 inverse-sampling 写回权威网格。
    /// </summary>
    PhysicsInverseSample,

    /// <summary>
    /// 局部静态地形 collider 构建与回收。
    /// </summary>
    StaticCollider,

    /// <summary>
    /// 像素场角色控制器移动解算。
    /// </summary>
    CharacterController,

    /// <summary>
    /// GPU 上传。
    /// </summary>
    GpuUpload,

    /// <summary>
    /// render buffer 构建。
    /// </summary>
    RenderBufferBuild,

    /// <summary>
    /// RenderStyle 差异化着色路径。
    /// </summary>
    RenderStyleShading,

    /// <summary>
    /// 自由粒子 stamp。
    /// </summary>
    ParticleStamp,

    /// <summary>
    /// 光照合成。
    /// </summary>
    Lighting,

    /// <summary>
    /// GPU compute light composite。
    /// </summary>
    GpuLightComposite,

    /// <summary>
    /// Bloom 后处理。
    /// </summary>
    Bloom,

    /// <summary>
    /// GPU 粒子 point-sprite 绘制。
    /// </summary>
    GpuParticleDraw,

    /// <summary>
    /// GPU compute bloom。
    /// </summary>
    GpuComputeBloom,

    /// <summary>
    /// Radiance Cascades compute GI。
    /// </summary>
    GpuRadianceCascades,

    /// <summary>
    /// 非权威 air/smoke compute 扩散。
    /// </summary>
    GpuAirSmoke,

    /// <summary>
    /// Dither、gamma 与 CRT 后处理。
    /// </summary>
    PostProcess,

    /// <summary>
    /// 游戏 UI 模型推送、后端 Update 与事件 drain。
    /// </summary>
    UiUpdate,

    /// <summary>
    /// 游戏 UI / GUI present 层合成。
    /// </summary>
    UiComposite,

    /// <summary>
    /// Present 提交、UI 绘制与交换前 hook 的 CPU 工作；不包含 SwapBuffers 阻塞等待。
    /// </summary>
    Present,

    /// <summary>
    /// SwapBuffers / present 阻塞等待；属于非工作时间，不计入 CPU/GPU busy。
    /// </summary>
    PresentWait,

    /// <summary>
    /// OpenGL timer query 异步回读的整帧 GPU 执行耗时；不可用后端保持 0。
    /// </summary>
    GpuFrame,

    /// <summary>
    /// 音频派发。
    /// </summary>
    AudioDispatch,
}
