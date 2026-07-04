using System.Runtime.CompilerServices;
using System.Reflection;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class UiContractTests
{
    [Theory]
    [InlineData(typeof(UiValue))]
    [InlineData(typeof(UiEvent))]
    [InlineData(typeof(UiDocumentHandle))]
    [InlineData(typeof(UiScreenHandle))]
    [InlineData(typeof(UiScreenId))]
    [InlineData(typeof(UiElementId))]
    [InlineData(typeof(UiActionId))]
    [InlineData(typeof(UiPathId))]
    [InlineData(typeof(UiStringHandle))]
    public void HotPathContractsContainNoManagedReferences(Type type)
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiValue>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiEvent>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiDocumentHandle>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiScreenHandle>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiScreenId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiElementId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiActionId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiPathId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiStringHandle>());
        Assert.True(type.IsValueType);
    }

    [Fact]
    public void UiAssemblyDoesNotReferenceEditorOrScripting()
    {
        Assembly assembly = typeof(GameUiHost).Assembly;
        string[] references = [.. assembly.GetReferencedAssemblies().Select(static name => name.Name ?? string.Empty)];

        Assert.DoesNotContain("PixelEngine.Editor", references);
        Assert.DoesNotContain("PixelEngine.Scripting", references);
    }

    [Fact]
    public void UiValuePreservesTypedPayload()
    {
        UiValue number = new(42L);
        UiValue scalar = new(12.5);
        UiValue flag = UiValue.FromBoolean(true);
        UiValue text = UiValue.FromStringHandle(new UiStringHandle(7));

        Assert.Equal(42L, number.AsInt64());
        Assert.Equal(12.5, scalar.AsDouble());
        Assert.True(flag.AsBoolean());
        Assert.Equal(new UiStringHandle(7), text.AsStringHandle());
        _ = Assert.Throws<InvalidOperationException>(() => scalar.AsInt64());
    }
}
