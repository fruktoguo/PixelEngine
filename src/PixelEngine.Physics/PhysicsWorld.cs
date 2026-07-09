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
        return AddBody(bodyId, mask, preferredBodyKey: null);
    }

    /// <summary>
    /// 添加刚体并尽量恢复指定 bodyKey，用于读档重建稳定刚体 id。
    /// </summary>
    public PixelRigidBody AddBody(B2BodyId bodyId, BodyLocalMask mask, int preferredBodyKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(preferredBodyKey);
        return AddBody(bodyId, mask, (int?)preferredBodyKey);
    }

    private PixelRigidBody AddBody(B2BodyId bodyId, BodyLocalMask mask, int? preferredBodyKey)
    {
        // bodyKey 优先复用 free list 空洞；读档时可指定稳定 id 恢复。
        int key = preferredBodyKey.HasValue ? ReservePreferredKey(preferredBodyKey.Value) : ReserveNextKey();
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

        // userData 存 key+1，0 保留给 native 空槽语义。
        Box2D.b2Body_SetUserData(bodyId, (void*)(nint)(key + 1));
        return body;
    }

    private int ReserveNextKey()
    {
        // 优先弹出 free list，保持 slot 稠密且 key 单调递增。
        return _freeKeys.Count > 0 ? _freeKeys.Pop() : _bodies.Count;
    }

    private int ReservePreferredKey(int key)
    {
        // 读档恢复：在目标 key 前用 null 占位并登记可复用 slot。
        while (_bodies.Count < key)
        {
            _freeKeys.Push(_bodies.Count);
            _bodies.Add(null);
        }

        if (key == _bodies.Count)
        {
            return key;
        }

        if (_bodies[key] is not null)
        {
            throw new InvalidOperationException($"bodyKey {key} 已存在，不能重复恢复刚体。");
        }

        RemoveFromFreeKeys(key);
        return key;
    }

    // 指定 key 被占用后，从 free list 剔除避免重复分配。
    private void RemoveFromFreeKeys(int key)
    {
        if (_freeKeys.Count == 0)
        {
            return;
        }

        int[] keys = [.. _freeKeys];
        _freeKeys.Clear();
        for (int i = keys.Length - 1; i >= 0; i--)
        {
            if (keys[i] != key)
            {
                _freeKeys.Push(keys[i]);
            }
        }
    }

    /// <summary>
    /// 当前 body slot 数量，包含可复用空洞。
    /// </summary>
    public int BodySlotCount => _bodies.Count;

    /// <summary>
    /// 当前活跃 body 数量。
    /// </summary>
    public int ActiveBodyCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _bodies.Count; i++)
            {
                if (_bodies[i] is not null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// 清空托管刚体表。仅在 owning PhysicsSystem 已销毁 Box2D world 后调用，避免保留过期 native body 句柄。
    /// </summary>
    public void Clear()
    {
        _bodies.Clear();
        _freeKeys.Clear();
    }

    /// <summary>
    /// 移除刚体 key。
    /// </summary>
    public void RemoveBody(int bodyKey)
    {
        PixelRigidBody _ = GetBody(bodyKey);
        // 逻辑删除：slot 置 null 并回收 key，不压缩数组以保持 bodyKey 稳定。
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
