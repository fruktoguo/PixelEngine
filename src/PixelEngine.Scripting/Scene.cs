namespace PixelEngine.Scripting;

/// <summary>
/// 脚本层实体容器；组件按类型分桶存储，不进入 Simulation 内核。
/// </summary>
public sealed class Scene
{
    private readonly Dictionary<int, Entity> _entities = [];
    private readonly Dictionary<Type, IComponentBucket> _buckets = [];
    private readonly List<ISystem> _systems = [];
    private int _nextEntityId;

    /// <summary>
    /// 当前活跃实体数量。
    /// </summary>
    public int EntityCount => _entities.Count;

    /// <summary>
    /// 已注册系统数量。
    /// </summary>
    public int SystemCount => _systems.Count;

    /// <summary>
    /// 创建一个脚本实体。
    /// </summary>
    public Entity CreateEntity()
    {
        int id = checked(++_nextEntityId);
        Entity entity = new(id, this);
        _entities.Add(id, entity);
        return entity;
    }

    /// <summary>
    /// 注册一个相位 1 脚本系统。
    /// </summary>
    public void RegisterSystem(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    /// <summary>
    /// 向指定实体添加组件。
    /// </summary>
    public T AddComponent<T>(Entity entity)
        where T : class, IComponent, new()
    {
        ValidateEntity(entity);
        ComponentBucket<T> bucket = GetOrCreateBucket<T>();
        T component = new();
        bucket.Add(entity, component);
        return component;
    }

    /// <summary>
    /// 尝试读取指定实体上的组件。
    /// </summary>
    public bool TryGetComponent<T>(Entity entity, out T component)
        where T : class, IComponent
    {
        ValidateEntity(entity);
        if (_buckets.TryGetValue(typeof(T), out IComponentBucket? bucket))
        {
            return ((ComponentBucket<T>)bucket).TryGet(entity.Id, out component);
        }

        component = null!;
        return false;
    }

    /// <summary>
    /// 移除指定实体上的组件。
    /// </summary>
    public void RemoveComponent<T>(Entity entity)
        where T : class, IComponent
    {
        ValidateEntity(entity);
        if (_buckets.TryGetValue(typeof(T), out IComponentBucket? bucket))
        {
            ((ComponentBucket<T>)bucket).Remove(entity.Id);
        }
    }

    /// <summary>
    /// 销毁指定实体并移除它的全部组件。
    /// </summary>
    public void Destroy(Entity entity)
    {
        ValidateEntity(entity);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.Remove(entity.Id);
        }

        _ = _entities.Remove(entity.Id);
    }

    /// <summary>
    /// 按注册顺序分发系统逐帧回调。
    /// </summary>
    public void DispatchFrameSystems(IScriptContext context, float dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].OnFrame(context, dt);
        }
    }

    /// <summary>
    /// 按注册顺序分发系统固定 sim tick 回调。
    /// </summary>
    public void DispatchSimSystems(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].OnSimTick(context);
        }
    }

    private void ValidateEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!ReferenceEquals(entity.Scene, this) || !_entities.ContainsKey(entity.Id))
        {
            throw new InvalidOperationException("实体不属于当前脚本场景或已经销毁。");
        }
    }

    private ComponentBucket<T> GetOrCreateBucket<T>()
        where T : class, IComponent
    {
        Type type = typeof(T);
        if (_buckets.TryGetValue(type, out IComponentBucket? bucket))
        {
            return (ComponentBucket<T>)bucket;
        }

        ComponentBucket<T> created = new();
        _buckets.Add(type, created);
        return created;
    }

    private interface IComponentBucket
    {
        void Remove(int entityId);
    }

    private sealed class ComponentBucket<T> : IComponentBucket
        where T : class, IComponent
    {
        private readonly Dictionary<int, T> _components = [];

        public void Add(Entity entity, T component)
        {
            if (component is Behaviour behaviour)
            {
                behaviour.Entity = entity;
            }

            _components[entity.Id] = component;
        }

        public bool TryGet(int entityId, out T component)
        {
            return _components.TryGetValue(entityId, out component!);
        }

        public void Remove(int entityId)
        {
            _ = _components.Remove(entityId);
        }
    }
}
