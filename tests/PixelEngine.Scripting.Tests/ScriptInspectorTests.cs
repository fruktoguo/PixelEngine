using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本 Inspector 反射测试。
/// 不变式：Inspector 反射暴露可序列化字段、只读属性只读。
/// </summary>
public sealed class ScriptInspectorTests
{
    /// <summary>
    /// 验证 Inspector 只枚举公开字段和 SerializeField 私有字段，并排除 HideInInspector 字段。
    /// </summary>
    [Fact]
    public void InspectFieldsReturnsVisiblePublicAndSerializedPrivateFields()
    {
        // Arrange：准备输入与初始状态
        InspectableBehaviour behaviour = new()
        {
            PublicValue = 7,
            HiddenValue = 11,
        };
        behaviour.SetPrivateValue(13);

        ScriptFieldDescriptor[] fields =
        [
            .. ScriptInspector.InspectFields(behaviour)
                .Where(static member => member.Name is "PublicValue" or "privateValue"),
        ];

        // Assert：验证预期结果
        Assert.Equal(["PublicValue", "privateValue"], [.. fields.Select(field => field.Name).Order(StringComparer.Ordinal)]);
        ScriptFieldDescriptor publicField = fields.Single(field => field.Name == "PublicValue");
        Assert.Equal(typeof(int), publicField.FieldType);
        Assert.Equal(7, publicField.Value);
        Assert.True(publicField.CanWrite);
        Assert.True(publicField.IsPublic);
        Assert.False(publicField.IsSerializedPrivate);
        Assert.Equal(ScriptFieldKind.Number, publicField.Kind);

        ScriptFieldDescriptor privateField = fields.Single(field => field.Name == "privateValue");
        Assert.Equal(13, privateField.Value);
        Assert.False(privateField.IsPublic);
        Assert.True(privateField.IsSerializedPrivate);
    }

    /// <summary>
    /// 验证 Inspector 写回字段值，并拒绝隐藏字段或类型不兼容值。
    /// </summary>
    [Fact]
    public void TrySetFieldValueUpdatesVisibleCompatibleFields()
    {
        InspectableBehaviour behaviour = new();

        Assert.True(ScriptInspector.TrySetFieldValue(behaviour, "PublicValue", 21));
        Assert.True(ScriptInspector.TrySetFieldValue(behaviour, "privateValue", 34));
        Assert.False(ScriptInspector.TrySetFieldValue(behaviour, "HiddenValue", 55));
        Assert.False(ScriptInspector.TrySetFieldValue(behaviour, "PublicValue", "bad"));

        Assert.Equal(21, behaviour.PublicValue);
        Assert.Equal(34, behaviour.GetPrivateValue());
        Assert.Equal(0, behaviour.HiddenValue);
    }

    /// <summary>
    /// 验证 Inspector 与场景 SerializedFieldBinder 一致地暴露 public property，
    /// 可写属性可修改，只读/隐藏属性不会被错误写回。
    /// </summary>
    [Fact]
    public void InspectFieldsAndSetterSupportPublicProperties()
    {
        InspectableBehaviour behaviour = new() { PublicProperty = 17 };

        ScriptFieldDescriptor[] members = ScriptInspector.InspectFields(behaviour);

        ScriptFieldDescriptor writable = members.Single(member => member.Name == nameof(InspectableBehaviour.PublicProperty));
        Assert.Equal(typeof(int), writable.FieldType);
        Assert.Equal(17, writable.Value);
        Assert.True(writable.CanWrite);
        ScriptFieldDescriptor readOnly = members.Single(member => member.Name == nameof(InspectableBehaviour.ReadOnlyProperty));
        Assert.False(readOnly.CanWrite);
        Assert.DoesNotContain(members, member => member.Name == nameof(InspectableBehaviour.HiddenProperty));

        Assert.True(ScriptInspector.TrySetFieldValue(behaviour, nameof(InspectableBehaviour.PublicProperty), 23));
        Assert.False(ScriptInspector.TrySetFieldValue(behaviour, nameof(InspectableBehaviour.ReadOnlyProperty), 99));
        Assert.False(ScriptInspector.TrySetFieldValue(behaviour, nameof(InspectableBehaviour.HiddenProperty), 99));
        Assert.Equal(23, behaviour.PublicProperty);
    }

    /// <summary>
    /// 验证用户 getter 抛错只降级当前属性，且引擎 Behaviour 基类运行态属性不会污染脚本 Inspector。
    /// </summary>
    [Fact]
    public void InspectFieldsContainsThrowingPropertyGetterWithoutCrashingEditor()
    {
        ThrowingPropertyBehaviour behaviour = new();

        ScriptFieldDescriptor[] members = ScriptInspector.InspectFields(behaviour);

        ScriptFieldDescriptor broken = members.Single(member => member.Name == nameof(ThrowingPropertyBehaviour.Broken));
        Assert.Equal(ScriptFieldKind.Unsupported, broken.Kind);
        Assert.False(broken.CanWrite);
        Assert.Contains("getter error", Assert.IsType<string>(broken.Value), StringComparison.Ordinal);
        Assert.DoesNotContain(members, member => member.Name is nameof(Behaviour.Entity) or nameof(Behaviour.Enabled) or nameof(Behaviour.Faulted) or nameof(Behaviour.LastException));
        Assert.DoesNotContain(members, member => member.Name == nameof(ThrowingPropertyBehaviour.Span));
        Assert.False(ScriptInspector.TrySetFieldValue(behaviour, nameof(ThrowingPropertyBehaviour.Rejected), 12));
    }

    /// <summary>
    /// 验证十参数描述器构造器继续存在，保护已编译 Editor 扩展的 CLR 调用点。
    /// </summary>
    [Fact]
    public void ScriptFieldDescriptorRetainsLegacyTenParameterConstructor()
    {
        ScriptFieldDescriptor descriptor = new(
            "Legacy",
            typeof(int),
            7,
            true,
            true,
            false,
            ScriptFieldKind.Number,
            null,
            null,
            null);

        System.Reflection.ConstructorInfo constructor = Assert.Single(
            typeof(ScriptFieldDescriptor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 10);
        Assert.Equal(
            ["Name", "FieldType", "Value", "CanWrite", "IsPublic", "IsSerializedPrivate", "Kind", "RangeMinimum", "RangeMaximum", "AssetKind"],
            constructor.GetParameters().Select(parameter => parameter.Name));
        System.Reflection.MethodInfo deconstruct = Assert.Single(
            typeof(ScriptFieldDescriptor).GetMethods().Where(method => method.Name == nameof(ScriptFieldDescriptor.Deconstruct)),
            method => method.GetParameters().Length == 10);
        Assert.Equal(
            ["Name", "FieldType", "Value", "CanWrite", "IsPublic", "IsSerializedPrivate", "Kind", "RangeMinimum", "RangeMaximum", "AssetKind"],
            deconstruct.GetParameters().Select(parameter => parameter.Name));

        (string name,
            Type fieldType,
            object? value,
            bool canWrite,
            bool isPublic,
            bool isSerializedPrivate,
            ScriptFieldKind kind,
            double? rangeMinimum,
            double? rangeMaximum,
            ScriptAssetKind? assetKind) = descriptor;
        Assert.Equal("Legacy", name);
        Assert.Equal(typeof(int), fieldType);
        Assert.Equal(7, value);
        Assert.True(canWrite);
        Assert.True(isPublic);
        Assert.False(isSerializedPrivate);
        Assert.Equal(ScriptFieldKind.Number, kind);
        Assert.Null(rangeMinimum);
        Assert.Null(rangeMaximum);
        Assert.Null(assetKind);
    }

    /// <summary>
    /// 验证 Inspector 字段描述会识别范围、向量、枚举与材质引用。
    /// </summary>
    [Fact]
    public void InspectFieldsClassifiesEditorFieldKindsAndRanges()
    {
        AdvancedBehaviour behaviour = new();

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);

        ScriptFieldDescriptor ranged = fields.Single(field => field.Name == nameof(AdvancedBehaviour.Ranged));
        Assert.Equal(ScriptFieldKind.Number, ranged.Kind);
        Assert.Equal(0d, ranged.RangeMinimum);
        Assert.Equal(10d, ranged.RangeMaximum);
        Assert.Equal(ScriptFieldKind.Vector, fields.Single(field => field.Name == nameof(AdvancedBehaviour.Position)).Kind);
        Assert.Equal(ScriptFieldKind.Enum, fields.Single(field => field.Name == nameof(AdvancedBehaviour.Mode)).Kind);
        Assert.Equal(ScriptFieldKind.Material, fields.Single(field => field.Name == nameof(AdvancedBehaviour.Material)).Kind);
    }

    /// <summary>
    /// 验证 Inspector 字段描述会识别 typed asset reference 字段，并支持字符串编码与强类型引用互转写回。
    /// </summary>
    [Fact]
    public void InspectFieldsClassifiesTypedAssetReferencesAndSetsCompatibleValues()
    {
        // Arrange：准备输入与初始状态
        AssetFieldBehaviour behaviour = new();
        string textureValue = ScriptAssetReference.Encode("asset_texture", "textures/sand.png", ScriptAssetKind.Texture);
        ScriptAssetReference audioReference = new(ScriptAssetKind.Audio, "asset_audio", "audio/hit.wav");

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);

        ScriptFieldDescriptor texture = fields.Single(field => field.Name == nameof(AssetFieldBehaviour.Texture));
        // Assert：验证预期结果
        Assert.Equal(ScriptFieldKind.AssetReference, texture.Kind);
        Assert.Equal(ScriptAssetKind.Texture, texture.AssetKind);
        ScriptFieldDescriptor audio = fields.Single(field => field.Name == nameof(AssetFieldBehaviour.Audio));
        Assert.Equal(ScriptFieldKind.AssetReference, audio.Kind);
        Assert.Equal(ScriptAssetKind.Audio, audio.AssetKind);
        ScriptFieldDescriptor unsupported = fields.Single(field => field.Name == nameof(AssetFieldBehaviour.Invalid));
        Assert.Equal(ScriptFieldKind.Unsupported, unsupported.Kind);
        Assert.Equal(ScriptAssetKind.Prefab, unsupported.AssetKind);

        Assert.True(ScriptInspector.TrySetFieldValue(behaviour, nameof(AssetFieldBehaviour.Texture), textureValue));
        Assert.True(ScriptInspector.TrySetFieldValue(behaviour, nameof(AssetFieldBehaviour.Audio), audioReference));

        Assert.Equal(new ScriptAssetReference(ScriptAssetKind.Texture, "asset_texture", "textures/sand.png"), behaviour.Texture);
        Assert.Equal(audioReference.ToString(), behaviour.Audio);
    }

    private sealed class InspectableBehaviour : Behaviour
    {
        [SerializeField]
        private int privateValue;

        public int PublicValue;

        [HideInInspector]
        public int HiddenValue;

        public int PublicProperty { get; set; }

        public int ReadOnlyProperty => PublicProperty + 1;

        [HideInInspector]
        public int HiddenProperty { get; set; }

        public int GetPrivateValue()
        {
            return privateValue;
        }

        public void SetPrivateValue(int value)
        {
            privateValue = value;
        }
    }

    private sealed class ThrowingPropertyBehaviour : Behaviour
    {
        private static readonly int[] SpanValues = [1, 2, 3];

        public int Broken => throw new InvalidOperationException("not attached");

        public ReadOnlySpan<int> Span => SpanValues;

        public int Rejected
        {
            get => 0;
            set => throw new InvalidOperationException("rejected");
        }
    }

    private sealed class AssetFieldBehaviour : Behaviour
    {
        [AssetField(ScriptAssetKind.Texture)]
        public ScriptAssetReference Texture = ScriptAssetReference.Empty;

        [AssetField(ScriptAssetKind.Audio)]
        public string Audio = string.Empty;

        [AssetField(ScriptAssetKind.Prefab)]
        public int Invalid = 0;
    }

    private sealed class AdvancedBehaviour : Behaviour
    {
        [Range(0, 10)]
        public int Ranged = 4;

        public System.Numerics.Vector2 Position = new(1, 2);

        public TestMode Mode = TestMode.A;

        public MaterialId Material = new(1);
    }

    private enum TestMode
    {
        A,
        B,
    }
}
