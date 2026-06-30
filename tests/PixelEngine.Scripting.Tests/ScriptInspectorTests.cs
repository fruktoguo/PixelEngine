using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本 Inspector 反射测试。
/// </summary>
public sealed class ScriptInspectorTests
{
    /// <summary>
    /// 验证 Inspector 只枚举公开字段和 SerializeField 私有字段，并排除 HideInInspector 字段。
    /// </summary>
    [Fact]
    public void InspectFieldsReturnsVisiblePublicAndSerializedPrivateFields()
    {
        InspectableBehaviour behaviour = new()
        {
            PublicValue = 7,
            HiddenValue = 11,
        };
        behaviour.SetPrivateValue(13);

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);

        Assert.Equal(["PublicValue", "privateValue"], [.. fields.Select(field => field.Name).Order(StringComparer.Ordinal)]);
        ScriptFieldDescriptor publicField = fields.Single(field => field.Name == "PublicValue");
        Assert.Equal(typeof(int), publicField.FieldType);
        Assert.Equal(7, publicField.Value);
        Assert.True(publicField.CanWrite);
        Assert.True(publicField.IsPublic);
        Assert.False(publicField.IsSerializedPrivate);

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
}
