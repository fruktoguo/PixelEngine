using System.Numerics;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Inspector 复合字段的稳定编码与运行时绑定回归。
/// </summary>
public sealed class SerializedFieldValueCodecTests
{
    /// <summary>
    /// 验证 Vector2/3/4 使用同一 invariant 入盘格式并能无损绑定到运行时 Behaviour。
    /// </summary>
    [Fact]
    public void VectorValuesRoundTripThroughAuthoringCodecAndRuntimeBinder()
    {
        Vector2 vector2 = new(1.25f, -2.5f);
        Vector3 vector3 = new(3.5f, 4.75f, -5.125f);
        Vector4 vector4 = new(6.25f, -7.5f, 8.75f, 9.125f);
        string encoded2 = SerializedFieldValueCodec.Format(vector2);
        string encoded3 = SerializedFieldValueCodec.Format(vector3);
        string encoded4 = SerializedFieldValueCodec.Format(vector4);

        Assert.Equal("1.25,-2.5", encoded2);
        Assert.Equal("3.5,4.75,-5.125", encoded3);
        Assert.Equal("6.25,-7.5,8.75,9.125", encoded4);
        Assert.True(SerializedFieldValueCodec.TryParseVector2(encoded2, out Vector2 parsed2));
        Assert.True(SerializedFieldValueCodec.TryParseVector3(encoded3, out Vector3 parsed3));
        Assert.True(SerializedFieldValueCodec.TryParseVector4(encoded4, out Vector4 parsed4));
        Assert.Equal(vector2, parsed2);
        Assert.Equal(vector3, parsed3);
        Assert.Equal(vector4, parsed4);

        VectorBehaviour behaviour = new();
        SerializedFieldBinder.Bind(
            behaviour,
            new Dictionary<string, string>
            {
                [nameof(VectorBehaviour.Position)] = encoded2,
                [nameof(VectorBehaviour.Direction)] = encoded3,
                [nameof(VectorBehaviour.Tint)] = encoded4,
            });

        Assert.Equal(vector2, behaviour.Position);
        Assert.Equal(vector3, behaviour.Direction);
        Assert.Equal(vector4, behaviour.Tint);
    }

    /// <summary>
    /// 验证错误分量数、区域性小数和非有限值不会进入场景数据或运行时组件。
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("1,2,3")]
    [InlineData("1,5,2,5")]
    [InlineData("NaN,2")]
    [InlineData("Infinity,2")]
    public void VectorCodecRejectsMalformedOrNonFiniteVector2(string serialized)
    {
        Assert.False(SerializedFieldValueCodec.TryParseVector2(serialized, out _));
        VectorBehaviour behaviour = new();
        _ = Assert.Throws<FormatException>(() => SerializedFieldBinder.Bind(
            behaviour,
            new Dictionary<string, string>
            {
                [nameof(VectorBehaviour.Position)] = serialized,
            }));
    }

    /// <summary>
    /// 验证高维向量的分量数与有限值契约，并保证编码器不会写出解析器拒绝的文本。
    /// </summary>
    [Fact]
    public void VectorCodecRejectsMalformedHigherDimensionsAndNonFiniteFormat()
    {
        Assert.False(SerializedFieldValueCodec.TryParseVector3("1,2", out _));
        Assert.False(SerializedFieldValueCodec.TryParseVector3("1,2,NaN", out _));
        Assert.False(SerializedFieldValueCodec.TryParseVector4("1,2,3,4,5", out _));
        Assert.False(SerializedFieldValueCodec.TryParseVector4("1,2,3,Infinity", out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SerializedFieldValueCodec.Format(new Vector2(float.NaN, 0f)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SerializedFieldValueCodec.Format(new Vector4(0f, 0f, 0f, float.PositiveInfinity)));
    }

    /// <summary>
    /// 验证 Inspector 允许编辑的全部 C# 数值类型都能用 invariant 文本进入运行时，
    /// 避免 authoring 可保存、Play 时却因 Binder 不认识类型而失败。
    /// </summary>
    [Fact]
    public void BinderSupportsEveryInspectorNumericType()
    {
        NumericBehaviour behaviour = new();
        SerializedFieldBinder.Bind(
            behaviour,
            new Dictionary<string, string>
            {
                [nameof(NumericBehaviour.Byte)] = "250",
                [nameof(NumericBehaviour.SByte)] = "-120",
                [nameof(NumericBehaviour.Int16)] = "-32000",
                [nameof(NumericBehaviour.UInt16)] = "65000",
                [nameof(NumericBehaviour.Int32)] = "-2000000000",
                [nameof(NumericBehaviour.UInt32)] = "4000000000",
                [nameof(NumericBehaviour.Int64)] = "-9000000000000000000",
                [nameof(NumericBehaviour.UInt64)] = "18000000000000000000",
                [nameof(NumericBehaviour.Single)] = "1.25",
                [nameof(NumericBehaviour.Double)] = "-2.5",
                [nameof(NumericBehaviour.Decimal)] = "7922816251426433759354395033.5",
            });

        Assert.Equal((byte)250, behaviour.Byte);
        Assert.Equal((sbyte)-120, behaviour.SByte);
        Assert.Equal((short)-32000, behaviour.Int16);
        Assert.Equal((ushort)65000, behaviour.UInt16);
        Assert.Equal(-2_000_000_000, behaviour.Int32);
        Assert.Equal(4_000_000_000U, behaviour.UInt32);
        Assert.Equal(-9_000_000_000_000_000_000L, behaviour.Int64);
        Assert.Equal(18_000_000_000_000_000_000UL, behaviour.UInt64);
        Assert.Equal(1.25f, behaviour.Single);
        Assert.Equal(-2.5d, behaviour.Double);
        Assert.Equal(7_922_816_251_426_433_759_354_395_033.5m, behaviour.Decimal);
    }

    private sealed class VectorBehaviour : Behaviour
    {
        public Vector2 Position { get; set; }

        public Vector3 Direction { get; set; }

        public Vector4 Tint { get; set; }
    }

    private sealed class NumericBehaviour : Behaviour
    {
        public byte Byte { get; set; }

        public sbyte SByte { get; set; }

        public short Int16 { get; set; }

        public ushort UInt16 { get; set; }

        public int Int32 { get; set; }

        public uint UInt32 { get; set; }

        public long Int64 { get; set; }

        public ulong UInt64 { get; set; }

        public float Single { get; set; }

        public double Double { get; set; }

        public decimal Decimal { get; set; }
    }
}
