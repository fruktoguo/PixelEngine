namespace PixelEngine.Editor.Shell;

/// <summary>
/// 编辑器场景中的 GameObject 节点，持有层级、Transform 与组件列表。
/// </summary>
internal sealed class EditorGameObject
{
    public EditorGameObject(int stableId, string name)
    {
        if (stableId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stableId), stableId, "StableId 必须为正数。");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        StableId = stableId;
        Name = name.Trim();
    }

    public int StableId { get; }

    public string Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否在编辑态 Scene View 中可见；不进入 .scene / prefab，也不改变运行时 active。
    /// </summary>
    public bool SceneVisible { get; internal set; } = true;

    /// <summary>
    /// 是否允许在编辑态 Scene View 中被鼠标与 gizmo 拾取；不进入运行时。
    /// </summary>
    public bool ScenePickable { get; internal set; } = true;

    public int? ParentId { get; internal set; }

    public List<int> Children { get; } = [];

    public EditorSceneTransform Transform { get; set; } = new();

    public List<EditorComponentModel> Components { get; } = [];

    public EditorPrefabLink? PrefabLink { get; set; }

    public EditorGameObject CloneShallow()
    {
        EditorGameObject clone = new(StableId, Name)
        {
            Enabled = Enabled,
            SceneVisible = SceneVisible,
            ScenePickable = ScenePickable,
            ParentId = ParentId,
            Transform = Transform.Clone(),
            PrefabLink = PrefabLink?.Clone(),
        };
        for (int i = 0; i < Components.Count; i++)
        {
            clone.Components.Add(Components[i].Clone());
        }

        return clone;
    }
}
