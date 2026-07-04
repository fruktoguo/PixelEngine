using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// Game UI 脚本契约测试。
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
    /// 验证未注入 UI 后端时默认属性给出明确失败。
    /// </summary>
    [Fact]
    public void DefaultScriptContextGameUiFailsClearlyWhenNotInjected()
    {
        EmptyContext context = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => ((IScriptContext)context).GameUi);

        Assert.Contains("Game UI", ex.Message, StringComparison.Ordinal);
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
}
