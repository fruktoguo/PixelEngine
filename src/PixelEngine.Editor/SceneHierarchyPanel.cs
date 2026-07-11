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
/// <param name="scriptScene">脚本场景。</param>
/// <param name="physics">物理系统。</param>
public sealed class RuntimeSceneHierarchyDataSource(Scene? scriptScene = null, PhysicsSystem? physics = null) : ISceneHierarchyDataSource
{
    private readonly Scene? _scriptScene = scriptScene;
    private readonly PhysicsSystem? _physics = physics;

    /// <inheritdoc />
    public SceneHierarchySnapshot Capture()
    {
        return new SceneHierarchySnapshot(CaptureEntities(), CaptureBodies());
    }

    private IReadOnlyList<SceneHierarchyEntityItem> CaptureEntities()
    {
        if (_scriptScene is null)
        {
            return [];
        }

        ScriptEntityInspection[] entities = _scriptScene.CaptureInspectionSnapshot();
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
