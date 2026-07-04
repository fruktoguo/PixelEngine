namespace PixelEngine.Scripting;

/// <summary>
/// 脚本场景中一个可供 Editor 检视的实体快照。
/// </summary>
/// <param name="EntityId">脚本场景内实体 id。</param>
/// <param name="Handle">Editor 选择态使用的稳定实体句柄。</param>
/// <param name="Transform">实体当前 Transform；无 Transform 组件时为空。</param>
/// <param name="Components">实体当前挂载的脚本组件快照。</param>
public readonly record struct ScriptEntityInspection(
    int EntityId,
    string Handle,
    Transform? Transform,
    ScriptComponentInspection[] Components);

/// <summary>
/// 脚本实体上一个可供 Editor 检视的组件快照。
/// </summary>
/// <param name="TypeName">组件类型全名。</param>
/// <param name="Behaviour">组件实例引用；Editor 只在相位 1 安全窗口编辑其字段。</param>
/// <param name="Enabled">组件是否启用。</param>
/// <param name="Faulted">组件是否已被脚本运行时隔离。</param>
public readonly record struct ScriptComponentInspection(
    string TypeName,
    Behaviour Behaviour,
    bool Enabled,
    bool Faulted);
