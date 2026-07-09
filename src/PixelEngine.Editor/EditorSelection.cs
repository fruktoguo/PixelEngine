namespace PixelEngine.Editor;

/// <summary>
/// Editor 跨面板共享的选择态。
/// </summary>
public sealed class EditorSelection
{
    /// <summary>
    /// 当前锁定的世界 cell X 坐标。
    /// </summary>
    public int? CellX { get; private set; }

    /// <summary>
    /// 当前锁定的世界 cell Y 坐标。
    /// </summary>
    public int? CellY { get; private set; }

    /// <summary>
    /// 当前选中的材质运行时 id。
    /// </summary>
    public int? MaterialId { get; private set; }

    /// <summary>
    /// 当前选中的资产路径。
    /// </summary>
    public string? AssetPath { get; private set; }

    /// <summary>
    /// 当前选中的实体句柄。
    /// </summary>
    public string? EntityHandle { get; private set; }

    /// <summary>
    /// 当前选中的编辑态 GameObject 稳定 id。
    /// </summary>
    public int? GameObjectStableId { get; private set; }

    /// <summary>
    /// 当前选中的刚体 id。
    /// </summary>
    public int? BodyId { get; private set; }

    /// <summary>
    /// 设置 cell 选择。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    public void SelectCell(int x, int y)
    {
        CellX = x;
        CellY = y;
    }

    /// <summary>
    /// 设置材质选择。
    /// </summary>
    /// <param name="materialId">材质运行时 id。</param>
    public void SelectMaterial(int materialId)
    {
        MaterialId = materialId;
    }

    /// <summary>
    /// 设置资产选择。
    /// </summary>
    /// <param name="assetPath">资产路径。</param>
    public void SelectAsset(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        AssetPath = assetPath;
        EntityHandle = null;
        GameObjectStableId = null;
        BodyId = null;
    }

    /// <summary>
    /// 设置实体选择。
    /// </summary>
    /// <param name="entityHandle">实体句柄。</param>
    public void SelectEntity(string entityHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityHandle);
        EntityHandle = entityHandle;
        GameObjectStableId = null;
    }

    /// <summary>
    /// 设置编辑态 GameObject 选择。
    /// </summary>
    /// <param name="stableId">GameObject 稳定 id。</param>
    public void SelectGameObject(int stableId)
    {
        if (stableId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stableId), stableId, "GameObject stable id 必须为正数。");
        }

        GameObjectStableId = stableId;
        AssetPath = null;
        EntityHandle = null;
        BodyId = null;
    }

    /// <summary>
    /// 设置刚体选择。
    /// </summary>
    /// <param name="bodyId">刚体 id。</param>
    public void SelectBody(int bodyId)
    {
        BodyId = bodyId;
    }

    /// <summary>
    /// 清空全部选择。
    /// </summary>
    public void Clear()
    {
        CellX = null;
        CellY = null;
        MaterialId = null;
        AssetPath = null;
        EntityHandle = null;
        GameObjectStableId = null;
        BodyId = null;
    }
}
