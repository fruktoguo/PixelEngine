using System.Diagnostics.CodeAnalysis;
using PixelEngine.Interop.Box2D;

namespace PixelEngine.Physics;

/// <summary>
/// 管理 PixelRigidBody 的稠密 key、自由列表和 Box2D userData 绑定。
/// </summary>
public sealed unsafe class PhysicsWorld
{
    private readonly List<PixelRigidBody?> _bodies = [];
    private readonly Stack<int> _freeKeys = new();

    /// <summary>
    /// 添加刚体并写入 Box2D userData。
    /// </summary>
    public PixelRigidBody AddBody(B2BodyId bodyId, BodyLocalMask mask)
    {
        int key = _freeKeys.Count > 0 ? _freeKeys.Pop() : _bodies.Count;
        PixelRigidBody body = new(key, bodyId, mask)
        {
            PreviousTransform = PhysicsScale.ToTransform2D(Box2D.b2Body_GetTransform(bodyId)),
        };
        if (key == _bodies.Count)
        {
            _bodies.Add(body);
        }
        else
        {
            _bodies[key] = body;
        }

        Box2D.b2Body_SetUserData(bodyId, (void*)(nint)(key + 1));
        return body;
    }

    /// <summary>
    /// 当前 body slot 数量，包含可复用空洞。
    /// </summary>
    public int BodySlotCount => _bodies.Count;

    /// <summary>
    /// 移除刚体 key。
    /// </summary>
    public void RemoveBody(int bodyKey)
    {
        PixelRigidBody _ = GetBody(bodyKey);
        _bodies[bodyKey] = null;
        _freeKeys.Push(bodyKey);
    }

    /// <summary>
    /// 获取指定 key 的刚体。
    /// </summary>
    public PixelRigidBody GetBody(int bodyKey)
    {
        return (uint)bodyKey < (uint)_bodies.Count && _bodies[bodyKey] is PixelRigidBody body
            ? body
            : throw new ArgumentOutOfRangeException(nameof(bodyKey), bodyKey, "bodyKey 不存在。");
    }

    /// <summary>
    /// 尝试获取指定 key 的刚体。
    /// </summary>
    public bool TryGetBody(int bodyKey, [NotNullWhen(true)] out PixelRigidBody? body)
    {
        if ((uint)bodyKey >= (uint)_bodies.Count)
        {
            body = null;
            return false;
        }

        body = _bodies[bodyKey];
        return body is not null;
    }
}
