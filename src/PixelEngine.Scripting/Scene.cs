namespace PixelEngine.Scripting;

/// <summary>
/// 脚本层实体容器；组件按类型分桶存储，不进入 Simulation 内核。
/// </summary>
public sealed class Scene
{
    private readonly Dictionary<int, Entity> _entities = [];
    private readonly Dictionary<Type, IComponentBucket> _buckets = [];
    private readonly List<ISystem> _systems = [];
    private readonly List<Entity> _destroyQueue = [];
    private readonly Stack<int> _freeEntityIds = new();
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
        int id = _freeEntityIds.Count == 0 ? checked(++_nextEntityId) : _freeEntityIds.Pop();
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
    /// 请求销毁指定实体；实际移除在 FlushDestroyed 中完成。
    /// </summary>
    public void Destroy(Entity entity)
    {
        ValidateEntity(entity);
        if (!_destroyQueue.Contains(entity))
        {
            _destroyQueue.Add(entity);
        }
    }

    /// <summary>
    /// 分发尚未启动 Behaviour 的 OnStart 回调。
    /// </summary>
    public void DispatchStart(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.DispatchStart(context);
        }
    }

    /// <summary>
    /// 分发 Behaviour 的逐帧 OnUpdate 回调。
    /// </summary>
    public void DispatchUpdate(IScriptContext context, float dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.DispatchUpdate(context, dt);
        }
    }

    /// <summary>
    /// 分发 Behaviour 的固定 sim tick 回调。
    /// </summary>
    public void DispatchFixedSimTick(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.DispatchFixedSimTick(context);
        }
    }

    /// <summary>
    /// 刷新延迟销毁队列并分发 OnDestroy。
    /// </summary>
    public void FlushDestroyed(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (int i = 0; i < _destroyQueue.Count; i++)
        {
            Entity entity = _destroyQueue[i];
            if (!_entities.ContainsKey(entity.Id))
            {
                continue;
            }

            foreach (IComponentBucket bucket in _buckets.Values)
            {
                bucket.Destroy(entity.Id, context);
            }

            _ = _entities.Remove(entity.Id);
            _freeEntityIds.Push(entity.Id);
        }

        _destroyQueue.Clear();
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

        void Destroy(int entityId, IScriptContext context);

        void DispatchStart(IScriptContext context);

        void DispatchUpdate(IScriptContext context, float dt);

        void DispatchFixedSimTick(IScriptContext context);
    }

    private sealed class ComponentBucket<T> : IComponentBucket
        where T : class, IComponent
    {
        private readonly Dictionary<int, int> _indices = [];
        private Entity[] _entities = [];
        private T[] _components = [];
        private int _count;

        public void Add(Entity entity, T component)
        {
            if (_indices.ContainsKey(entity.Id))
            {
                throw new InvalidOperationException($"实体 {entity.Id} 已经包含组件 {typeof(T).Name}。");
            }

            EnsureCapacity(_count + 1);
            if (component is Behaviour behaviour)
            {
                behaviour.Entity = entity;
            }

            _entities[_count] = entity;
            _components[_count] = component;
            _indices.Add(entity.Id, _count);
            _count++;
        }

        public bool TryGet(int entityId, out T component)
        {
            if (_indices.TryGetValue(entityId, out int index))
            {
                component = _components[index];
                return true;
            }

            component = null!;
            return false;
        }

        public void Remove(int entityId)
        {
            if (!_indices.TryGetValue(entityId, out int index))
            {
                return;
            }

            int last = --_count;
            _ = _indices.Remove(entityId);
            if (index != last)
            {
                _entities[index] = _entities[last];
                _components[index] = _components[last];
                _indices[_entities[index].Id] = index;
            }

            _entities[last] = null!;
            _components[last] = null!;
        }

        public void Destroy(int entityId, IScriptContext context)
        {
            if (TryGet(entityId, out T component) && component is Behaviour behaviour)
            {
                behaviour.InvokeDestroy(context);
            }

            Remove(entityId);
        }

        public void DispatchStart(IScriptContext context)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_components[i] is Behaviour { Started: false } behaviour)
                {
                    behaviour.InvokeStart(context);
                }
            }
        }

        public void DispatchUpdate(IScriptContext context, float dt)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_components[i] is Behaviour { Enabled: true } behaviour)
                {
                    behaviour.InvokeUpdate(context, dt);
                }
            }
        }

        public void DispatchFixedSimTick(IScriptContext context)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_components[i] is Behaviour { Enabled: true } behaviour)
                {
                    behaviour.InvokeFixedSimTick(context);
                }
            }
        }

        private void EnsureCapacity(int required)
        {
            if (_components.Length >= required)
            {
                return;
            }

            int capacity = _components.Length == 0 ? 4 : Math.Max(_components.Length * 2, required);
            Array.Resize(ref _entities, capacity);
            Array.Resize(ref _components, capacity);
        }
    }
}
