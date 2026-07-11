using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 玩家出生点 authoring 标记。位置由所属实体的 <see cref="Transform"/> 提供，
/// 因而可由 Editor Hierarchy、Inspector 与 Scene gizmo 统一编辑并随 .scene 落盘。
/// </summary>
public sealed class PlayerSpawnPoint : Behaviour
{
}

/// <summary>
/// 关卡目标点 authoring 标记。位置由所属实体的 <see cref="Transform"/> 提供。
/// </summary>
public sealed class GoalPoint : Behaviour
{
}
