namespace PixelEngine.Scripting;

/// <summary>
/// 显式提供编辑态权威像素世界的脚本组件契约。
/// </summary>
/// <remarks>
/// Editor 只在场景投影边界调用本契约，不会为了预览执行任意 Behaviour 生命周期。
/// 实现必须让预览铺设与运行时初始世界共用同一份确定性逻辑。
/// </remarks>
public interface IAuthoringWorldPreviewProvider
{
    /// <summary>
    /// 描述预览世界尺寸与内容指纹。
    /// </summary>
    /// <returns>经过调用方校验的预览描述。</returns>
    AuthoringWorldPreviewDescriptor DescribeAuthoringWorld();

    /// <summary>
    /// 向已驻留的权威 Simulation 网格铺设编辑态初始世界。
    /// </summary>
    /// <param name="context">受限的材质查询与世界编辑上下文。</param>
    void PopulateAuthoringWorld(in AuthoringWorldPreviewContext context);

    /// <summary>
    /// 接受一个内容指纹完全相同、已经存在的编辑态世界，不重复写入 cell。
    /// </summary>
    /// <remarks>
    /// Editor 重建 Script Scene 投影但保留现有像素网格时调用。实现应只同步自身的运行时
    /// 初始化状态，使随后进入 Play 不会重新清场；不得在此方法写入世界。
    /// </remarks>
    void AdoptAuthoringWorld();
}

/// <summary>
/// authoring world provider 可使用的受限 cell 写入能力。
/// </summary>
/// <remarks>
/// Hosting/Editor 负责把该契约适配到权威 Simulation 编辑门面；玩法脚本不会直接依赖
/// Simulation 实现类型，也不能访问温度、刚体或调度生命周期。
/// </remarks>
public interface IAuthoringWorldEditApi
{
    /// <summary>
    /// 写入单个世界 cell。
    /// </summary>
    /// <param name="worldX">世界 X 坐标。</param>
    /// <param name="worldY">世界 Y 坐标。</param>
    /// <param name="material">要写入的材质句柄。</param>
    void PaintCell(int worldX, int worldY, MaterialId material);

    /// <summary>
    /// 写入世界坐标闭区间矩形。
    /// </summary>
    /// <param name="minX">闭区间最小 X。</param>
    /// <param name="minY">闭区间最小 Y。</param>
    /// <param name="maxX">闭区间最大 X。</param>
    /// <param name="maxY">闭区间最大 Y。</param>
    /// <param name="material">要写入的材质句柄。</param>
    /// <returns>实际写入的 cell 数量。</returns>
    int PaintRect(int minX, int minY, int maxX, int maxY, MaterialId material);

    /// <summary>
    /// 清空单个世界 cell。
    /// </summary>
    /// <param name="worldX">世界 X 坐标。</param>
    /// <param name="worldY">世界 Y 坐标。</param>
    void ClearCell(int worldX, int worldY);

    /// <summary>
    /// 清空世界坐标闭区间矩形。
    /// </summary>
    /// <param name="minX">闭区间最小 X。</param>
    /// <param name="minY">闭区间最小 Y。</param>
    /// <param name="maxX">闭区间最大 X。</param>
    /// <param name="maxY">闭区间最大 Y。</param>
    /// <returns>实际清空的 cell 数量。</returns>
    int ClearRect(int minX, int minY, int maxX, int maxY);
}

/// <summary>
/// 编辑态权威像素世界描述。
/// </summary>
/// <param name="WidthCells">世界宽度，单位 cell。</param>
/// <param name="HeightCells">世界高度，单位 cell。</param>
/// <param name="ContentHash">确定性内容指纹；任何会改变铺设结果的输入变化都必须改变该值。</param>
public readonly record struct AuthoringWorldPreviewDescriptor(
    int WidthCells,
    int HeightCells,
    ulong ContentHash)
{
    /// <summary>
    /// 校验尺寸并返回当前描述。
    /// </summary>
    /// <returns>当前描述。</returns>
    public AuthoringWorldPreviewDescriptor Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WidthCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(HeightCells);
        return this;
    }
}

/// <summary>
/// authoring world provider 的受限铺设上下文。
/// </summary>
/// <param name="Materials">材质查询能力。</param>
/// <param name="Config">当前项目 ContentRoot 的只读配置能力。</param>
/// <param name="Edit">由 Editor 适配到权威 Simulation 的受限编辑门面。</param>
/// <param name="WidthCells">本次描述的世界宽度。</param>
/// <param name="HeightCells">本次描述的世界高度。</param>
public readonly record struct AuthoringWorldPreviewContext(
    IMaterialQuery Materials,
    IConfigApi Config,
    IAuthoringWorldEditApi Edit,
    int WidthCells,
    int HeightCells);
