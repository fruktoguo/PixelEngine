using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本生命周期派发的稳态分配测试。
/// 不变式：生命周期派发稳态零额外分配（预热后）。
/// </summary>
public sealed class ScriptDispatchAllocationTests
{
    /// <summary>
    /// 验证 Behaviour 与 ISystem 的稳态派发不产生托管堆分配。
    /// </summary>
    [Fact]
    public void SceneSteadyDispatchDoesNotAllocateManagedMemory()
    {
        Scene scene = new();
        FakeScriptContext context = new(scene);
        for (int i = 0; i < 128; i++)
        {
            _ = scene.CreateEntity().AddComponent<NoopBehaviour>();
        }

        scene.RegisterSystem(new NoopSystem());
        scene.DispatchStart(context);
        scene.DispatchUpdate(context, 0.016f);
        scene.DispatchFixedSimTick(context);
        scene.DispatchFrameSystems(context, 0.016f);
        scene.DispatchSimSystems(context);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            scene.DispatchUpdate(context, 0.016f);
            scene.DispatchFixedSimTick(context);
            scene.DispatchFrameSystems(context, 0.016f);
            scene.DispatchSimSystems(context);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// 验证场景级组件查询会跟随稠密分桶的添加与 swap-remove 结果。
    /// </summary>
    [Fact]
    public void SceneComponentLookupTracksDenseBucketMutations()
    {
        Scene scene = new();
        Entity firstEntity = scene.CreateEntity();
        MarkerComponent first = firstEntity.AddComponent<MarkerComponent>();
        MarkerComponent second = scene.CreateEntity().AddComponent<MarkerComponent>();

        Assert.True(scene.TryGetFirstComponent(out MarkerComponent? found));
        Assert.Same(first, found);

        firstEntity.RemoveComponent<MarkerComponent>();

        Assert.True(scene.TryGetFirstComponent(out found));
        Assert.Same(second, found);
    }

    /// <summary>
    /// 验证缺失组件的场景级查询在预热后不创建检查快照或其他托管对象。
    /// </summary>
    [Fact]
    public void MissingSceneComponentLookupDoesNotAllocateManagedMemory()
    {
        Scene scene = new();
        _ = scene.CreateEntity().AddComponent<NoopBehaviour>();
        _ = scene.TryGetFirstComponent<MarkerComponent>(out _);
        bool found = false;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            found |= scene.TryGetFirstComponent<MarkerComponent>(out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(found);
        Assert.Equal(0, allocated);
    }

    private sealed class NoopBehaviour : Behaviour;

    private sealed class MarkerComponent : IComponent;

    private sealed class NoopSystem : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
        }

        public void OnFrame(IScriptContext context, float dt)
        {
        }
    }

    private sealed class FakeScriptContext(Scene scene) : IScriptContext
    {
        public IWorldCellAccess Cells => throw new NotSupportedException();

        public IWorldEffects World => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public ILightingApi Lighting => throw new NotSupportedException();

        public IDiagnosticsApi Diagnostics => throw new NotSupportedException();

        public IEventBus Events => throw new NotSupportedException();

        public IAudioApi Audio => throw new NotSupportedException();

        public IGameTime Time => throw new NotSupportedException();

        public Scene Scene { get; } = scene;
    }
}
