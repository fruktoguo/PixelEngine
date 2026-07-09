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

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);

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

        public int GetPrivateValue()
        {
            return privateValue;
        }

        public void SetPrivateValue(int value)
        {
            privateValue = value;
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
