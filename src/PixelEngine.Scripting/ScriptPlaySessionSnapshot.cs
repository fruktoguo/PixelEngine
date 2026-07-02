namespace PixelEngine.Scripting;

/// <summary>
/// 脚本 Play Session 的字段状态快照；用于 Editor 临时 Play 退出时恢复存活 Behaviour 的公开或 Persist 字段。
/// </summary>
public sealed class ScriptPlaySessionSnapshot
{
    internal ScriptPlaySessionSnapshot(ScriptPlaySessionBehaviourSnapshot[] behaviours)
    {
        Behaviours = behaviours ?? throw new ArgumentNullException(nameof(behaviours));
    }

    internal ScriptPlaySessionBehaviourSnapshot[] Behaviours { get; }

    /// <summary>
    /// 快照中记录的 Behaviour 数量。
    /// </summary>
    public int BehaviourCount => Behaviours.Length;
}

internal readonly record struct ScriptPlaySessionBehaviourSnapshot(
    int EntityId,
    Type BehaviourType,
    ScriptStateSnapshot State);
