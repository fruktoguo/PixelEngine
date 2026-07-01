using System.Diagnostics.CodeAnalysis;

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
    private readonly ScriptInvoker _invoker;
    private int _nextEntityId;

    /// <summary>
    /// 创建一个脚本场景；脚本实体与组件只在相位 1 生命周期内被驱动。
    /// </summary>
    public Scene()
        : this(null)
    {
    }

    internal Scene(IScriptDiagnosticSink? diagnostics)
    {
        _invoker = new ScriptInvoker(diagnostics);
    }

    /// <summary>
    /// 当前活跃实体数量；脚本可在相位 1 读取。
    /// </summary>
    public int EntityCount => _entities.Count;

    /// <summary>
    /// 已注册系统数量；脚本可在相位 1 读取。
    /// </summary>
    public int SystemCount => _systems.Count;

    /// <summary>
    /// 已捕获的脚本回调异常数量；脚本可在相位 1 读取。
    /// </summary>
    public int ScriptExceptionCount => _invoker.Exceptions.Count;

    /// <summary>
    /// 捕获当前脚本实体与 Behaviour 组件快照，供 Editor 层级与 Inspector 使用。
    /// </summary>
    /// <returns>按实体 id 升序排列的只读快照数组。</returns>
    public ScriptEntityInspection[] CaptureInspectionSnapshot()
    {
        List<ScriptEntityInspection> snapshots = new(_entities.Count);
        foreach (Entity entity in _entities.Values.OrderBy(static item => item.Id))
        {
            List<ScriptComponentInspection> components = [];
            foreach (IComponentBucket bucket in _buckets.Values)
            {
                bucket.CaptureInspectionComponents(entity.Id, components);
            }

            snapshots.Add(new ScriptEntityInspection(entity.Id, $"script:{entity.Id}", [.. components]));
        }

        return [.. snapshots];
    }

    /// <summary>
    /// 创建一个脚本实体；脚本可在相位 1 调用。
    /// </summary>
    /// <returns>新创建的脚本实体。</returns>
    public Entity CreateEntity()
    {
        int id = _freeEntityIds.Count == 0 ? checked(++_nextEntityId) : _freeEntityIds.Pop();
        Entity entity = new(id, this);
        _entities.Add(id, entity);
        return entity;
    }

    /// <summary>
    /// 注册一个相位 1 脚本系统；通常在场景初始化或热重载边界调用。
    /// </summary>
    /// <param name="system">要按注册顺序派发的脚本系统。</param>
    public void RegisterSystem(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    /// <summary>
    /// 向指定实体添加组件。
    /// </summary>
    internal T AddComponent<T>(Entity entity)
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
    internal bool TryGetComponent<T>(Entity entity, out T component)
        where T : class, IComponent
    {
        ValidateEntity(entity);
        if (_buckets.TryGetValue(typeof(T), out IComponentBucket? bucket))
        {
            if (bucket.TryGetObject(entity.Id, out IComponent value))
            {
                component = (T)value;
                return true;
            }
        }

        component = null!;
        return false;
    }

    /// <summary>
    /// 移除指定实体上的组件。
    /// </summary>
    internal void RemoveComponent<T>(Entity entity)
        where T : class, IComponent
    {
        ValidateEntity(entity);
        RemoveComponent(entity, typeof(T));
    }

    internal void RemoveComponent(Entity entity, Type componentType)
    {
        ValidateEntity(entity);
        ArgumentNullException.ThrowIfNull(componentType);
        if (_buckets.TryGetValue(componentType, out IComponentBucket? bucket))
        {
            bucket.Remove(entity.Id);
            if (bucket.Count == 0)
            {
                _ = _buckets.Remove(componentType);
            }
        }
    }

    internal void DestroyComponent(Entity entity, Type componentType, IScriptContext context)
    {
        ValidateEntity(entity);
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentNullException.ThrowIfNull(context);
        if (_buckets.TryGetValue(componentType, out IComponentBucket? bucket))
        {
            bucket.Destroy(entity.Id, context, _invoker);
            if (bucket.Count == 0)
            {
                _ = _buckets.Remove(componentType);
            }
        }
    }

    internal void AddComponent(Entity entity, IComponent component)
    {
        ValidateEntity(entity);
        ArgumentNullException.ThrowIfNull(component);
        Type componentType = component.GetType();
        IComponentBucket bucket = GetOrCreateBucket(componentType);
        bucket.AddObject(entity, component);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Script components are runtime Behaviour types validated before activation; they are outside the trimmed engine static closure.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Constructor validation is performed over runtime script component types, not over trimmed engine metadata.")]
    internal IComponent AddComponent(Entity entity, Type componentType)
    {
        ValidateEntity(entity);
        ArgumentNullException.ThrowIfNull(componentType);
        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new ArgumentException("组件类型必须实现 IComponent。", nameof(componentType));
        }

        if (componentType.IsAbstract || componentType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new ArgumentException("组件类型必须是非抽象类型并提供无参构造。", nameof(componentType));
        }

        IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
        AddComponent(entity, component);
        return component;
    }

    /// <summary>
    /// 请求销毁指定实体；实际移除在 FlushDestroyed 中完成。
    /// </summary>
    internal void Destroy(Entity entity)
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
    internal void DispatchStart(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.DispatchStart(context, _invoker);
        }
    }

    /// <summary>
    /// 分发 Behaviour 的逐帧 OnUpdate 回调。
    /// </summary>
    internal void DispatchUpdate(IScriptContext context, float dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.DispatchUpdate(context, dt, _invoker);
        }
    }

    /// <summary>
    /// 分发 Behaviour 的固定 sim tick 回调。
    /// </summary>
    internal void DispatchFixedSimTick(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.DispatchFixedSimTick(context, _invoker);
        }
    }

    /// <summary>
    /// 分发 Behaviour 的 GUI 绘制回调。
    /// </summary>
    internal void DispatchGui(IScriptContext context, IGuiContext gui)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(gui);
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.DispatchGui(context, gui, _invoker);
        }
    }

    /// <summary>
    /// 刷新延迟销毁队列并分发 OnDestroy。
    /// </summary>
    internal void FlushDestroyed(IScriptContext context)
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
                bucket.Destroy(entity.Id, context, _invoker);
            }

            _ = _entities.Remove(entity.Id);
            _freeEntityIds.Push(entity.Id);
        }

        _destroyQueue.Clear();
    }

    /// <summary>
    /// 按注册顺序分发系统逐帧回调。
    /// </summary>
    internal void DispatchFrameSystems(IScriptContext context, float dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (int i = 0; i < _systems.Count; i++)
        {
            _invoker.InvokeFrameSystem(_systems[i], context, dt);
        }
    }

    /// <summary>
    /// 按注册顺序分发系统固定 sim tick 回调。
    /// </summary>
    internal void DispatchSimSystems(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (int i = 0; i < _systems.Count; i++)
        {
            _invoker.InvokeSimSystem(_systems[i], context);
        }
    }

    internal ScriptBehaviourRecord[] CaptureBehaviours()
    {
        List<ScriptBehaviourRecord> records = [];
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.CaptureBehaviours(records);
        }

        return [.. records];
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

    private IComponentBucket GetOrCreateBucket(Type type)
    {
        if (!typeof(IComponent).IsAssignableFrom(type))
        {
            throw new ArgumentException("组件类型必须实现 IComponent。", nameof(type));
        }

        if (_buckets.TryGetValue(type, out IComponentBucket? bucket))
        {
            return bucket;
        }

        IComponentBucket created = new DynamicComponentBucket(type);
        _buckets.Add(type, created);
        return created;
    }

    private interface IComponentBucket
    {
        int Count { get; }

        void AddObject(Entity entity, IComponent component);

        bool TryGetObject(int entityId, out IComponent component);

        void Remove(int entityId);

        void Destroy(int entityId, IScriptContext context, ScriptInvoker invoker);

        void DispatchStart(IScriptContext context, ScriptInvoker invoker);

        void DispatchUpdate(IScriptContext context, float dt, ScriptInvoker invoker);

        void DispatchFixedSimTick(IScriptContext context, ScriptInvoker invoker);

        void DispatchGui(IScriptContext context, IGuiContext gui, ScriptInvoker invoker);

        void CaptureBehaviours(List<ScriptBehaviourRecord> records);

        void CaptureInspectionComponents(int entityId, List<ScriptComponentInspection> records);
    }

    private sealed class ComponentBucket<T> : IComponentBucket
        where T : class, IComponent
    {
        private readonly Dictionary<int, int> _indices = [];
        private Entity[] _entities = [];
        private T[] _components = [];

        public int Count { get; private set; }

        public void Add(Entity entity, T component)
        {
            if (_indices.ContainsKey(entity.Id))
            {
                throw new InvalidOperationException($"实体 {entity.Id} 已经包含组件 {typeof(T).Name}。");
            }

            EnsureCapacity(Count + 1);
            if (component is Behaviour behaviour)
            {
                behaviour.Entity = entity;
            }

            _entities[Count] = entity;
            _components[Count] = component;
            _indices.Add(entity.Id, Count);
            Count++;
        }

        public void AddObject(Entity entity, IComponent component)
        {
            Add(entity, (T)component);
        }

        public bool TryGetObject(int entityId, out IComponent component)
        {
            if (TryGet(entityId, out T typed))
            {
                component = typed;
                return true;
            }

            component = null!;
            return false;
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

            int last = --Count;
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

        public void Destroy(int entityId, IScriptContext context, ScriptInvoker invoker)
        {
            if (TryGet(entityId, out T component) && component is Behaviour behaviour)
            {
                invoker.InvokeDestroy(behaviour, context);
            }

            Remove(entityId);
        }

        public void DispatchStart(IScriptContext context, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour { Started: false } behaviour)
                {
                    invoker.InvokeStart(behaviour, context);
                }
            }
        }

        public void DispatchUpdate(IScriptContext context, float dt, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    invoker.InvokeUpdate(behaviour, context, dt);
                }
            }
        }

        public void DispatchFixedSimTick(IScriptContext context, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    invoker.InvokeFixedSimTick(behaviour, context);
                }
            }
        }

        public void DispatchGui(IScriptContext context, IGuiContext gui, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    invoker.InvokeGui(behaviour, context, gui);
                }
            }
        }

        public void CaptureBehaviours(List<ScriptBehaviourRecord> records)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    records.Add(new ScriptBehaviourRecord(_entities[i], behaviour));
                }
            }
        }

        public void CaptureInspectionComponents(int entityId, List<ScriptComponentInspection> records)
        {
            if (_indices.TryGetValue(entityId, out int index) && _components[index] is Behaviour behaviour)
            {
                Type type = behaviour.GetType();
                records.Add(new ScriptComponentInspection(
                    type.FullName ?? type.Name,
                    behaviour,
                    behaviour.Enabled,
                    behaviour.Faulted));
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

    private sealed class DynamicComponentBucket(Type componentType) : IComponentBucket
    {
        private readonly Type _componentType = componentType;
        private readonly Dictionary<int, int> _indices = [];
        private Entity[] _entities = [];
        private IComponent[] _components = [];

        public int Count { get; private set; }

        public void AddObject(Entity entity, IComponent component)
        {
            if (!_componentType.IsInstanceOfType(component))
            {
                throw new ArgumentException("组件实例类型与 bucket 类型不匹配。", nameof(component));
            }

            if (_indices.ContainsKey(entity.Id))
            {
                throw new InvalidOperationException($"实体 {entity.Id} 已经包含组件 {_componentType.Name}。");
            }

            EnsureCapacity(Count + 1);
            if (component is Behaviour behaviour)
            {
                behaviour.Entity = entity;
            }

            _entities[Count] = entity;
            _components[Count] = component;
            _indices.Add(entity.Id, Count);
            Count++;
        }

        public bool TryGetObject(int entityId, out IComponent component)
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

            int last = --Count;
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

        public void Destroy(int entityId, IScriptContext context, ScriptInvoker invoker)
        {
            if (_indices.TryGetValue(entityId, out int index) && _components[index] is Behaviour behaviour)
            {
                invoker.InvokeDestroy(behaviour, context);
            }

            Remove(entityId);
        }

        public void DispatchStart(IScriptContext context, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour { Started: false } behaviour)
                {
                    invoker.InvokeStart(behaviour, context);
                }
            }
        }

        public void DispatchUpdate(IScriptContext context, float dt, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    invoker.InvokeUpdate(behaviour, context, dt);
                }
            }
        }

        public void DispatchFixedSimTick(IScriptContext context, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    invoker.InvokeFixedSimTick(behaviour, context);
                }
            }
        }

        public void DispatchGui(IScriptContext context, IGuiContext gui, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    invoker.InvokeGui(behaviour, context, gui);
                }
            }
        }

        public void CaptureBehaviours(List<ScriptBehaviourRecord> records)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour behaviour)
                {
                    records.Add(new ScriptBehaviourRecord(_entities[i], behaviour));
                }
            }
        }

        public void CaptureInspectionComponents(int entityId, List<ScriptComponentInspection> records)
        {
            if (_indices.TryGetValue(entityId, out int index) && _components[index] is Behaviour behaviour)
            {
                Type type = behaviour.GetType();
                records.Add(new ScriptComponentInspection(
                    type.FullName ?? type.Name,
                    behaviour,
                    behaviour.Enabled,
                    behaviour.Faulted));
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

internal readonly record struct ScriptBehaviourRecord(Entity Entity, Behaviour Behaviour);
