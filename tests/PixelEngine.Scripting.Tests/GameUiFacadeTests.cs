using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// Game UI 脚本契约测试。
/// 不变式：Game UI 脚本契约与后端能力表一致。
/// </summary>
public sealed class GameUiFacadeTests
{
    /// <summary>
    /// 验证脚本 UI 热路径结构不含托管引用。
    /// </summary>
    [Fact]
    public void ScriptUiHotPathContractsContainNoManagedReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiValue>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiEvent>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiScreenHandle>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiCanvasId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiCanvasHandle>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiElementId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiActionId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiPathId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiModelName>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiStringHandle>());
    }

    /// <summary>
    /// 验证 Scripting 不引用 PixelEngine.UI。
    /// </summary>
    [Fact]
    public void ScriptingAssemblyDoesNotReferencePixelEngineUi()
    {
        Assembly assembly = typeof(IGameUiService).Assembly;
        string[] references = [.. assembly.GetReferencedAssemblies().Select(static name => name.Name ?? string.Empty)];

        Assert.DoesNotContain("PixelEngine.UI", references);
    }

    /// <summary>
    /// 验证未注入 UI 后端时默认属性返回安全空服务。
    /// </summary>
    [Fact]
    public void DefaultScriptContextGameUiIsNoopWhenNotInjected()
    {
        // Arrange：准备输入与初始状态
        EmptyContext context = new();

        IGameUiService gameUi = ((IScriptContext)context).GameUi;
        IGameUiService ui = ((IScriptContext)context).Ui;
        UiScreenHandle screen = gameUi.ShowScreen("main");
        UiStringHandle text = gameUi.InternString("开始游戏");
        gameUi.HideScreen(screen);
        gameUi.BindModel(screen, new UiModelName(1), new MissingModel());
        gameUi.SetValue(screen, new UiPathId(7), new UiValue(42L));
        gameUi.Invoke(screen, new UiActionId(3), UiValue.FromBoolean(true));

        // Assert：验证预期结果
        Assert.Same(NoopGameUiService.Instance, gameUi);
        Assert.Same(gameUi, ui);
        Assert.Equal(default, screen);
        Assert.Equal(default, text);
        Assert.False(gameUi.TryGetValue(screen, new UiPathId(7), out UiValue value));
        Assert.Equal(default, value);
    }

    /// <summary>
    /// 验证禁用 UI 的空服务不会保留脚本事件订阅。
    /// </summary>
    [Fact]
    public void NoopGameUiServiceDoesNotRetainUiEventSubscribers()
    {
        WeakReference subscribedHandlerTarget = SubscribeTransientNoopUiHandler(unsubscribe: false);
        WeakReference unsubscribedHandlerTarget = SubscribeTransientNoopUiHandler(unsubscribe: true);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.False(subscribedHandlerTarget.IsAlive);
        Assert.False(unsubscribedHandlerTarget.IsAlive);
    }

    /// <summary>
    /// 验证禁用 UI 的空服务对所有脚本写入静默安全失败。
    /// </summary>
    [Fact]
    public void NoopGameUiServiceSilentlyRejectsAllOperations()
    {
        // Arrange：准备输入与初始状态
        IGameUiService gameUi = NoopGameUiService.Instance;
        UiScreenHandle screen = gameUi.ShowScreen("hud");
        UiScreenHandle modal = gameUi.PushModal("pause");
        UiScreenHandle explicitScreen = gameUi.ShowScreen(new UiCanvasHandle(99), "hud");
        UiScreenHandle explicitModal = gameUi.PushModal(new UiCanvasHandle(99), "pause");
        UiStringHandle text = gameUi.InternString("暂停");

        gameUi.HideScreen(new UiScreenHandle(99));
        gameUi.BindModel(new UiScreenHandle(99), new UiModelName(1), new MissingModel());
        gameUi.SetValue(new UiScreenHandle(99), new UiPathId(7), new UiValue(42L));
        gameUi.Invoke(new UiScreenHandle(99), new UiActionId(3), UiValue.FromBoolean(true));

        // Assert：验证预期结果
        Assert.Equal(default, screen);
        Assert.Equal(default, modal);
        Assert.Equal(default, explicitScreen);
        Assert.Equal(default, explicitModal);
        Assert.Equal(default, text);
        Assert.Equal(default, gameUi.PrimaryCanvas);
        Assert.False(gameUi.TryGetCanvas(new UiCanvasId(1), out UiCanvasHandle canvas));
        Assert.Equal(default, canvas);
        Assert.Equal(0, gameUi.CopyCanvases(new UiCanvasHandle[2]));
        Assert.False(gameUi.TryGetValue(new UiScreenHandle(99), new UiPathId(7), out UiValue value));
        Assert.Equal(default, value);
    }

    /// <summary>
    /// 验证脚本 UI 值保留强类型载荷。
    /// </summary>
    [Fact]
    public void ScriptUiValuePreservesTypedPayload()
    {
        UiValue number = new(9L);
        UiValue scalar = new(2.25);
        UiValue flag = UiValue.FromBoolean(true);
        UiValue text = UiValue.FromStringHandle(new UiStringHandle(5));

        Assert.Equal(9L, number.AsInt64());
        Assert.Equal(2.25, scalar.AsDouble());
        Assert.True(flag.AsBoolean());
        Assert.Equal(new UiStringHandle(5), text.AsStringHandle());
        _ = Assert.Throws<InvalidOperationException>(() => flag.AsDouble());
    }

    /// <summary>旧四字段 UiEvent 构造与解构保持源码兼容，同时新事件携带 Canvas 来源。</summary>
    [Fact]
    public void UiEventKeepsFourFieldCompatibilityAndCarriesCanvas()
    {
        UiEvent legacy = new(
            new UiScreenHandle(2),
            new UiElementId(3),
            new UiActionId(4),
            new UiValue(5L));
        UiEvent current = new(
            new UiCanvasHandle(1),
            new UiScreenHandle(2),
            new UiElementId(3),
            new UiActionId(4),
            new UiValue(5L));

        (UiScreenHandle screen, UiElementId element, UiActionId action, UiValue payload) = legacy;
        Assert.Equal(default, legacy.Canvas);
        Assert.Equal(new UiScreenHandle(2), screen);
        Assert.Equal(new UiElementId(3), element);
        Assert.Equal(new UiActionId(4), action);
        Assert.Equal(5L, payload.AsInt64());
        Assert.Equal(new UiCanvasHandle(1), current.Canvas);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeTransientNoopUiHandler(bool unsubscribe)
    {
        NoopSubscriberTarget target = new();

        NoopGameUiService.Instance.UiEventRaised += target.OnUiEvent;
        if (unsubscribe)
        {
            NoopGameUiService.Instance.UiEventRaised -= target.OnUiEvent;
        }

        return new WeakReference(target);
    }

    private sealed class NoopSubscriberTarget
    {
        public void OnUiEvent(UiEvent uiEvent)
        {
            _ = uiEvent;
        }
    }

    private sealed class EmptyContext : IScriptContext
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

        public Scene Scene => throw new NotSupportedException();
    }

    private sealed class MissingModel : IUiModel
    {
        public bool TryGetValue(UiPathId path, out UiValue value)
        {
            _ = path;
            value = default;
            return false;
        }
    }
}
