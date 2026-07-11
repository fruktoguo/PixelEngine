using Hexa.NET.ImGui;
using PixelEngine.Physics;
using PixelEngine.Scripting;

namespace PixelEngine.Editor;

/// <summary>
/// 场景层级快照。
/// </summary>
/// <param name="Entities">脚本实体条目。</param>
/// <param name="Bodies">活跃刚体条目。</param>
public readonly record struct SceneHierarchySnapshot(
    IReadOnlyList<SceneHierarchyEntityItem> Entities,
    IReadOnlyList<SceneHierarchyBodyItem> Bodies);

/// <summary>
/// 场景层级脚本实体条目。
/// </summary>
/// <param name="Handle">Editor 选择态使用的实体句柄。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="ComponentCount">Behaviour 组件数量。</param>
public readonly record struct SceneHierarchyEntityItem(string Handle, string DisplayName, int ComponentCount);

/// <summary>
/// 场景层级刚体条目。
/// </summary>
/// <param name="BodyKey">刚体 key。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="X">世界 X 坐标。</param>
/// <param name="Y">世界 Y 坐标。</param>
public readonly record struct SceneHierarchyBodyItem(int BodyKey, string DisplayName, float X, float Y);

/// <summary>
/// 场景层级只读数据源。
/// </summary>
public interface ISceneHierarchyDataSource
{
    /// <summary>
    /// 捕获当前层级快照。
    /// </summary>
    /// <returns>层级快照。</returns>
    SceneHierarchySnapshot Capture();
}

/// <summary>
/// Runtime Hierarchy 与 Inspector 共用的数据源。写操作只允许由 Editor 帧末安全相位调用，
/// 并以临时编辑记录保证退出 Play 后可恢复。
/// </summary>
public interface IRuntimeSceneEditorDataSource : ISceneHierarchyDataSource
{
    /// <summary>读取当前 runtime 实体。</summary>
    /// <param name="handle">实体稳定 handle。</param>
    /// <param name="entity">找到的实体检查快照。</param>
    /// <returns>实体仍存在时返回 true。</returns>
    bool TryGetEntity(string handle, out ScriptEntityInspection entity);

    /// <summary>读取当前 runtime 刚体快照。</summary>
    /// <param name="bodyKey">刚体稳定键。</param>
    /// <param name="body">找到的刚体快照。</param>
    /// <returns>刚体仍存在时返回 true。</returns>
    bool TryGetBody(int bodyKey, out RigidBodySnapshot body);

    /// <summary>临时修改 runtime 实体 Transform。</summary>
    /// <param name="handle">实体稳定 handle。</param>
    /// <param name="x">世界 X。</param>
    /// <param name="y">世界 Y。</param>
    /// <param name="rotationRadians">弧度旋转。</param>
    /// <param name="scaleX">X 缩放。</param>
    /// <param name="scaleY">Y 缩放。</param>
    /// <returns>实体与 Transform 可写时返回 true。</returns>
    bool TrySetEntityTransform(
        string handle,
        float x,
        float y,
        float rotationRadians,
        float scaleX,
        float scaleY);

    /// <summary>临时修改 runtime Behaviour 字段。</summary>
    /// <param name="handle">实体稳定 handle。</param>
    /// <param name="componentIndex">Behaviour 在实体检查快照中的索引。</param>
    /// <param name="fieldName">字段或属性名。</param>
    /// <param name="value">新值。</param>
    /// <returns>成员存在、可写且值兼容时返回 true。</returns>
    bool TrySetBehaviourField(string handle, int componentIndex, string fieldName, object? value);

    /// <summary>恢复本数据源记录的全部临时 Play 编辑。</summary>
    void RestoreTemporaryEdits();
}

/// <summary>
/// 视口聚焦服务。
/// </summary>
public interface IViewportFocusService
{
    /// <summary>
    /// 聚焦指定世界坐标。
    /// </summary>
    /// <param name="worldX">世界 X。</param>
    /// <param name="worldY">世界 Y。</param>
    void Focus(float worldX, float worldY);
}

/// <summary>
/// 基于脚本 Scene 与 PhysicsSystem 的层级数据源。
/// </summary>
public sealed class RuntimeSceneHierarchyDataSource : IRuntimeSceneEditorDataSource
{
    private readonly Func<Scene?> _sceneProvider;
    private readonly PhysicsSystem? _physics;
    private readonly Dictionary<string, RuntimeTransformSnapshot> _transformBaselines = new(StringComparer.Ordinal);
    private readonly Dictionary<RuntimeFieldKey, object?> _fieldBaselines = [];
    private Scene? _baselineScene;

    /// <summary>使用固定 Scene 创建数据源；兼容无需替换 projection 的调用方。</summary>
    /// <param name="scriptScene">固定脚本 Scene；可为空。</param>
    /// <param name="physics">可选物理系统。</param>
    public RuntimeSceneHierarchyDataSource(Scene? scriptScene = null, PhysicsSystem? physics = null)
        : this(() => scriptScene, physics, dynamicProvider: false)
    {
    }

    private RuntimeSceneHierarchyDataSource(
        Func<Scene?> sceneProvider,
        PhysicsSystem? physics,
        bool dynamicProvider)
    {
        _ = dynamicProvider;
        _sceneProvider = sceneProvider ?? throw new ArgumentNullException(nameof(sceneProvider));
        _physics = physics;
    }

    /// <summary>
    /// 创建会在每次读取时解析当前 Scene 的动态数据源，用于 Play/Edit projection 可替换的 Editor 宿主。
    /// </summary>
    /// <param name="sceneProvider">当前脚本 Scene provider。</param>
    /// <param name="physics">可选物理系统。</param>
    /// <returns>动态 runtime 层级数据源。</returns>
    public static RuntimeSceneHierarchyDataSource CreateDynamic(
        Func<Scene?> sceneProvider,
        PhysicsSystem? physics = null)
    {
        return new RuntimeSceneHierarchyDataSource(sceneProvider, physics, dynamicProvider: true);
    }

    /// <inheritdoc />
    public SceneHierarchySnapshot Capture()
    {
        return new SceneHierarchySnapshot(CaptureEntities(), CaptureBodies());
    }

    /// <inheritdoc />
    public bool TryGetEntity(string handle, out ScriptEntityInspection entity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        Scene? scene = _sceneProvider();
        if (scene is not null)
        {
            ScriptEntityInspection[] entities = scene.CaptureInspectionSnapshot();
            for (int i = 0; i < entities.Length; i++)
            {
                if (string.Equals(entities[i].Handle, handle, StringComparison.Ordinal))
                {
                    entity = entities[i];
                    return true;
                }
            }
        }

        entity = default;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetBody(int bodyKey, out RigidBodySnapshot body)
    {
        if (_physics is not null)
        {
            int count = _physics.Stats.ActiveBodyCount;
            if (count != 0)
            {
                RigidBodySnapshot[] snapshots = new RigidBodySnapshot[count];
                int written = _physics.CopyBodySnapshots(snapshots);
                for (int i = 0; i < written; i++)
                {
                    if (snapshots[i].BodyKey == bodyKey)
                    {
                        body = snapshots[i];
                        return true;
                    }
                }
            }
        }

        body = default;
        return false;
    }

    /// <inheritdoc />
    public bool TrySetEntityTransform(
        string handle,
        float x,
        float y,
        float rotationRadians,
        float scaleX,
        float scaleY)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(rotationRadians) ||
            !float.IsFinite(scaleX) || !float.IsFinite(scaleY) ||
            !TryGetEntity(handle, out ScriptEntityInspection entity) || entity.Transform is null)
        {
            return false;
        }

        Scene? scene = _sceneProvider();
        EnsureBaselineScene(scene);
        Transform transform = entity.Transform;
        if (!_transformBaselines.ContainsKey(handle))
        {
            _transformBaselines.Add(handle, RuntimeTransformSnapshot.Capture(transform));
        }

        transform.SetPosition(x, y);
        transform.RotationRadians = rotationRadians;
        transform.ScaleX = scaleX;
        transform.ScaleY = scaleY;
        return true;
    }

    /// <inheritdoc />
    public bool TrySetBehaviourField(string handle, int componentIndex, string fieldName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (!TryGetEntity(handle, out ScriptEntityInspection entity) ||
            (uint)componentIndex >= (uint)entity.Components.Length)
        {
            return false;
        }

        Behaviour behaviour = entity.Components[componentIndex].Behaviour;
        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        for (int i = 0; i < fields.Length; i++)
        {
            ScriptFieldDescriptor field = fields[i];
            if (!string.Equals(field.Name, fieldName, StringComparison.Ordinal) || !field.CanWrite)
            {
                continue;
            }

            Scene? scene = _sceneProvider();
            EnsureBaselineScene(scene);
            RuntimeFieldKey key = new(handle, componentIndex, fieldName);
            _ = _fieldBaselines.TryAdd(key, field.Value);
            return ScriptInspector.TrySetFieldValue(behaviour, fieldName, value);
        }

        return false;
    }

    /// <inheritdoc />
    public void RestoreTemporaryEdits()
    {
        Scene? scene = _sceneProvider();
        if (scene is null || !ReferenceEquals(scene, _baselineScene))
        {
            ClearTemporaryEditBaselines();
            return;
        }

        foreach (KeyValuePair<string, RuntimeTransformSnapshot> item in _transformBaselines)
        {
            if (TryGetEntity(item.Key, out ScriptEntityInspection entity) && entity.Transform is not null)
            {
                item.Value.Apply(entity.Transform);
            }
        }

        foreach (KeyValuePair<RuntimeFieldKey, object?> item in _fieldBaselines)
        {
            if (TryGetEntity(item.Key.Handle, out ScriptEntityInspection entity) &&
                (uint)item.Key.ComponentIndex < (uint)entity.Components.Length)
            {
                _ = ScriptInspector.TrySetFieldValue(
                    entity.Components[item.Key.ComponentIndex].Behaviour,
                    item.Key.FieldName,
                    item.Value);
            }
        }

        ClearTemporaryEditBaselines();
    }

    private IReadOnlyList<SceneHierarchyEntityItem> CaptureEntities()
    {
        Scene? scriptScene = _sceneProvider();
        if (scriptScene is null)
        {
            return [];
        }

        ScriptEntityInspection[] entities = scriptScene.CaptureInspectionSnapshot();
        SceneHierarchyEntityItem[] items = new SceneHierarchyEntityItem[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            ScriptEntityInspection entity = entities[i];
            string displayName = entity.Components.Length == 0
                ? $"Entity {entity.EntityId}"
                : $"{GetShortTypeName(entity.Components[0].TypeName)} · Entity {entity.EntityId}";
            items[i] = new SceneHierarchyEntityItem(
                entity.Handle,
                displayName,
                entity.Components.Length);
        }

        return items;
    }

    private static string GetShortTypeName(string typeName)
    {
        int separator = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
        return separator >= 0 && separator < typeName.Length - 1
            ? typeName[(separator + 1)..]
            : typeName;
    }

    private IReadOnlyList<SceneHierarchyBodyItem> CaptureBodies()
    {
        if (_physics is null)
        {
            return [];
        }

        int count = _physics.Stats.ActiveBodyCount;
        if (count == 0)
        {
            return [];
        }

        RigidBodySnapshot[] snapshots = new RigidBodySnapshot[count];
        int written = _physics.CopyBodySnapshots(snapshots);
        SceneHierarchyBodyItem[] bodies = new SceneHierarchyBodyItem[written];
        for (int i = 0; i < written; i++)
        {
            RigidBodySnapshot body = snapshots[i];
            bodies[i] = new SceneHierarchyBodyItem(
                body.BodyKey,
                $"Body {body.BodyKey}",
                body.Transform.Position.X,
                body.Transform.Position.Y);
        }

        return bodies;
    }

    private void EnsureBaselineScene(Scene? scene)
    {
        if (ReferenceEquals(scene, _baselineScene))
        {
            return;
        }

        ClearTemporaryEditBaselines();
        _baselineScene = scene;
    }

    private void ClearTemporaryEditBaselines()
    {
        _transformBaselines.Clear();
        _fieldBaselines.Clear();
        _baselineScene = null;
    }

    private readonly record struct RuntimeFieldKey(string Handle, int ComponentIndex, string FieldName);

    private readonly record struct RuntimeTransformSnapshot(
        float X,
        float Y,
        float RotationRadians,
        float ScaleX,
        float ScaleY)
    {
        public static RuntimeTransformSnapshot Capture(Transform transform)
        {
            return new RuntimeTransformSnapshot(
                transform.X,
                transform.Y,
                transform.RotationRadians,
                transform.ScaleX,
                transform.ScaleY);
        }

        public void Apply(Transform transform)
        {
            transform.SetPosition(X, Y);
            transform.RotationRadians = RotationRadians;
            transform.ScaleX = ScaleX;
            transform.ScaleY = ScaleY;
        }
    }
}

/// <summary>
/// Demo 实体与活跃刚体层级面板。
/// </summary>
/// <param name="source">层级数据源。</param>
/// <param name="focus">视口聚焦服务。</param>
public sealed class SceneHierarchyPanel(ISceneHierarchyDataSource source, IViewportFocusService? focus = null) : IEditorPanel
{
    private readonly ISceneHierarchyDataSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IViewportFocusService? _focus = focus;

    /// <inheritdoc />
    public string Title => EditorDockSpace.SceneHierarchyWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次层级快照。
    /// </summary>
    public SceneHierarchySnapshot LastSnapshot { get; private set; } = new([], []);

    /// <summary>
    /// 刷新层级快照。
    /// </summary>
    /// <returns>刷新后的层级快照。</returns>
    public SceneHierarchySnapshot Refresh()
    {
        LastSnapshot = _source.Capture();
        return LastSnapshot;
    }

    /// <summary>
    /// 选择实体并联动 Inspector。
    /// </summary>
    /// <param name="handle">实体句柄。</param>
    /// <param name="selection">Editor 选择态。</param>
    /// <returns>找到实体时返回 true。</returns>
    public bool SelectEntity(string handle, EditorSelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentNullException.ThrowIfNull(selection);
        SceneHierarchySnapshot snapshot = EnsureSnapshot();
        for (int i = 0; i < snapshot.Entities.Count; i++)
        {
            if (string.Equals(snapshot.Entities[i].Handle, handle, StringComparison.Ordinal))
            {
                selection.SelectEntity(handle);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 选择刚体并请求视口聚焦。
    /// </summary>
    /// <param name="bodyKey">刚体 key。</param>
    /// <param name="selection">Editor 选择态。</param>
    /// <returns>找到刚体时返回 true。</returns>
    public bool SelectBody(int bodyKey, EditorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        SceneHierarchySnapshot snapshot = EnsureSnapshot();
        for (int i = 0; i < snapshot.Bodies.Count; i++)
        {
            SceneHierarchyBodyItem body = snapshot.Bodies[i];
            if (body.BodyKey != bodyKey)
            {
                continue;
            }

            selection.SelectBody(bodyKey);
            _focus?.Focus(body.X, body.Y);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        SceneHierarchySnapshot snapshot = Refresh();
        if (ImGui.CollapsingHeader("实体", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < snapshot.Entities.Count; i++)
            {
                SceneHierarchyEntityItem entity = snapshot.Entities[i];
                bool selected = string.Equals(context.Selection.EntityHandle, entity.Handle, StringComparison.Ordinal);
                if (ImGui.Selectable($"{entity.DisplayName} ({entity.ComponentCount})", selected))
                {
                    _ = SelectEntity(entity.Handle, context.Selection);
                }
            }
        }

        if (ImGui.CollapsingHeader("刚体", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < snapshot.Bodies.Count; i++)
            {
                SceneHierarchyBodyItem body = snapshot.Bodies[i];
                bool selected = context.Selection.BodyId == body.BodyKey;
                if (ImGui.Selectable($"{body.DisplayName}  {body.X:F1},{body.Y:F1}", selected))
                {
                    _ = SelectBody(body.BodyKey, context.Selection);
                }
            }
        }

        ImGui.End();
    }

    private SceneHierarchySnapshot EnsureSnapshot()
    {
        return LastSnapshot.Entities.Count == 0 && LastSnapshot.Bodies.Count == 0
            ? Refresh()
            : LastSnapshot;
    }
}
