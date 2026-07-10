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
    /// 尝试从场景的同类型稠密组件分桶中读取一个组件实例，不创建检查快照或临时集合。
    /// </summary>
    /// <typeparam name="T">要查找的组件类型。</typeparam>
    /// <param name="component">找到时返回组件实例；未找到时为默认值。</param>
    /// <returns>场景中存在该组件类型时返回 true，否则返回 false。</returns>
    /// <remarks>
    /// 返回实例的实体顺序不是稳定排序契约；组件被移除后稠密分桶会执行 swap-remove。
    /// 需要完整有序视图的 Editor/诊断代码应继续使用 <see cref="CaptureInspectionSnapshot" />。
    /// </remarks>
    public bool TryGetFirstComponent<T>([NotNullWhen(true)] out T? component)
        where T : class, IComponent
    {
        if (_buckets.TryGetValue(typeof(T), out IComponentBucket? bucket) &&
            bucket.TryGetFirstObject(out IComponent value))
        {
            component = (T)value;
            return true;
        }

        component = null;
        return false;
    }

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

            bool hasTransform = TryGetComponent(entity, out Transform transform);
            snapshots.Add(new ScriptEntityInspection(entity.Id, $"script:{entity.Id}", hasTransform ? transform : null, [.. components]));
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
        // 帧末统一销毁：先 OnDestroy 再摘组件/实体，避免 OnUpdate 期间读到悬空引用。
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
    /// 结束 Play Session：对仍存活且已启动的 Behaviour 派发 OnDestroy，并保留编辑态实体与组件。
    /// </summary>
    internal void EndPlaySession(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _destroyQueue.Clear();
        foreach (IComponentBucket bucket in _buckets.Values)
        {
            bucket.EndPlaySession(context, _invoker);
        }
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

    internal ScriptPlaySessionSnapshot CapturePlaySessionSnapshot()
    {
        ScriptBehaviourRecord[] records = CaptureBehaviours();
        ScriptPlaySessionBehaviourSnapshot[] snapshots = new ScriptPlaySessionBehaviourSnapshot[records.Length];
        for (int i = 0; i < records.Length; i++)
        {
            Behaviour behaviour = records[i].Behaviour;
            snapshots[i] = new ScriptPlaySessionBehaviourSnapshot(
                records[i].Entity.Id,
                behaviour.GetType(),
                ScriptStateSnapshot.Capture(behaviour));
        }

        return new ScriptPlaySessionSnapshot([.. _entities.Keys], snapshots);
    }

    internal void RestorePlaySessionSnapshot(ScriptPlaySessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // Play Session 快照恢复：先删多余实体，再补缺失 id，最后对齐 Behaviour 集合与序列化状态。
        HashSet<int> savedEntityIds = [.. snapshot.EntityIds];
        Entity[] currentEntities = [.. _entities.Values];
        for (int i = 0; i < currentEntities.Length; i++)
        {
            if (!savedEntityIds.Contains(currentEntities[i].Id))
            {
                RemoveEntityImmediate(currentEntities[i].Id);
            }
        }

        for (int i = 0; i < snapshot.EntityIds.Length; i++)
        {
            if (!_entities.ContainsKey(snapshot.EntityIds[i]))
            {
                _ = CreateEntityWithId(snapshot.EntityIds[i]);
            }
        }

        ScriptBehaviourRecord[] records = CaptureBehaviours();
        for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
        {
            ScriptBehaviourRecord current = records[recordIndex];
            if (!ContainsSnapshotBehaviour(snapshot, current.Entity.Id, current.Behaviour.GetType()))
            {
                RemoveComponent(current.Entity, current.Behaviour.GetType());
            }
        }

        for (int snapshotIndex = 0; snapshotIndex < snapshot.Behaviours.Length; snapshotIndex++)
        {
            ScriptPlaySessionBehaviourSnapshot saved = snapshot.Behaviours[snapshotIndex];
            Entity entity = _entities[saved.EntityId];
            if (!TryGetComponent(entity, saved.BehaviourType, out IComponent component))
            {
                component = AddComponent(entity, saved.BehaviourType);
            }

            if (component is Behaviour behaviour)
            {
                saved.State.Restore(behaviour);
            }
        }
    }

    private bool TryGetComponent(Entity entity, Type componentType, out IComponent component)
    {
        ValidateEntity(entity);
        if (_buckets.TryGetValue(componentType, out IComponentBucket? bucket))
        {
            return bucket.TryGetObject(entity.Id, out component);
        }

        component = null!;
        return false;
    }

    private Entity CreateEntityWithId(int id)
    {
        if (id <= 0)
        {
            throw new InvalidOperationException("脚本快照包含非法实体 id。");
        }

        if (_entities.ContainsKey(id))
        {
            throw new InvalidOperationException($"脚本快照重复实体 id：{id}。");
        }

        Entity entity = new(id, this);
        _entities.Add(id, entity);
        if (id > _nextEntityId)
        {
            _nextEntityId = id;
        }

        RebuildFreeEntityIds();
        return entity;
    }

    private void RemoveEntityImmediate(int entityId)
    {
        List<Type> emptyBuckets = [];
        foreach (KeyValuePair<Type, IComponentBucket> pair in _buckets)
        {
            pair.Value.Remove(entityId);
            if (pair.Value.Count == 0)
            {
                emptyBuckets.Add(pair.Key);
            }
        }

        for (int i = 0; i < emptyBuckets.Count; i++)
        {
            _ = _buckets.Remove(emptyBuckets[i]);
        }

        _ = _entities.Remove(entityId);
        _ = _destroyQueue.RemoveAll(entity => entity.Id == entityId);
        RebuildFreeEntityIds();
    }

    private void RebuildFreeEntityIds()
    {
        _freeEntityIds.Clear();
        for (int id = _nextEntityId; id >= 1; id--)
        {
            if (!_entities.ContainsKey(id))
            {
                _freeEntityIds.Push(id);
            }
        }
    }

    private static bool ContainsSnapshotBehaviour(ScriptPlaySessionSnapshot snapshot, int entityId, Type behaviourType)
    {
        for (int i = 0; i < snapshot.Behaviours.Length; i++)
        {
            ScriptPlaySessionBehaviourSnapshot saved = snapshot.Behaviours[i];
            if (saved.EntityId == entityId && saved.BehaviourType == behaviourType)
            {
                return true;
            }
        }

        return false;
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

        bool TryGetFirstObject(out IComponent component);

        void Remove(int entityId);

        void Destroy(int entityId, IScriptContext context, ScriptInvoker invoker);

        void DispatchStart(IScriptContext context, ScriptInvoker invoker);

        void DispatchUpdate(IScriptContext context, float dt, ScriptInvoker invoker);

        void DispatchFixedSimTick(IScriptContext context, ScriptInvoker invoker);

        void DispatchGui(IScriptContext context, IGuiContext gui, ScriptInvoker invoker);

        void EndPlaySession(IScriptContext context, ScriptInvoker invoker);

        void CaptureBehaviours(List<ScriptBehaviourRecord> records);

        void CaptureInspectionComponents(int entityId, List<ScriptComponentInspection> records);
    }

    private sealed class ComponentBucket<T> : IComponentBucket
        where T : class, IComponent
    {
        private readonly Dictionary<int, int> _indices = [];
        private Entity[] _entities = [];
        private T[] _components = [];

        // 同类型组件稠密分桶：entityId→index 映射支撑 O(1) 增删与顺序遍历派发。
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

        public bool TryGetFirstObject(out IComponent component)
        {
            if (Count > 0)
            {
                component = _components[0];
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

            // swap-remove 保持稠密存储，避免 Destroy 后留下空洞影响逐帧遍历。
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

        public void EndPlaySession(IScriptContext context, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour { Started: true } behaviour)
                {
                    invoker.InvokeDestroy(behaviour, context);
                    behaviour.ResetPlaySessionLifecycle();
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

        public bool TryGetFirstObject(out IComponent component)
        {
            if (Count > 0)
            {
                component = _components[0];
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

        public void EndPlaySession(IScriptContext context, ScriptInvoker invoker)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_components[i] is Behaviour { Started: true } behaviour)
                {
                    invoker.InvokeDestroy(behaviour, context);
                    behaviour.ResetPlaySessionLifecycle();
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
