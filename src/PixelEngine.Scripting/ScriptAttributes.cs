namespace PixelEngine.Scripting;

/// <summary>
/// 标记私有字段可被 Inspector 序列化与编辑。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeFieldAttribute : Attribute
{
}

/// <summary>
/// 标记字段或属性在脚本热重载时应尝试保留状态。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PersistAttribute : Attribute
{
}

/// <summary>
/// 标记字段或属性不在 Inspector 中显示。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute
{
}

/// <summary>
/// 标记数值字段或属性在 Inspector 中使用范围滑条编辑。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class RangeAttribute : Attribute
{
    /// <summary>
    /// 创建 Inspector 数值范围。
    /// </summary>
    /// <param name="minimum">允许的最小值。</param>
    /// <param name="maximum">允许的最大值。</param>
    public RangeAttribute(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Inspector 范围必须是有限值，且 minimum 不能大于 maximum。");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// 允许的最小值。
    /// </summary>
    public double Minimum { get; }

    /// <summary>
    /// 允许的最大值。
    /// </summary>
    public double Maximum { get; }
}

/// <summary>
/// Inspector 可绑定的工程资产类别。
/// </summary>
public enum ScriptAssetKind
{
    /// <summary>
    /// 材质定义资产。
    /// </summary>
    Material,

    /// <summary>
    /// 纹理资产。
    /// </summary>
    Texture,

    /// <summary>
    /// 音频资产。
    /// </summary>
    Audio,

    /// <summary>
    /// 场景资产。
    /// </summary>
    Scene,

    /// <summary>
    /// Prefab 资产。
    /// </summary>
    Prefab,

    /// <summary>
    /// 脚本资产。
    /// </summary>
    Script,
}

/// <summary>
/// 标记字符串或 <see cref="ScriptAssetReference"/> 成员为 typed asset reference Inspector 成员。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AssetFieldAttribute(ScriptAssetKind assetType) : Attribute
{
    /// <summary>
    /// 成员允许绑定的工程资产类别。
    /// </summary>
    public ScriptAssetKind AssetType { get; } = assetType;
}

/// <summary>
/// 标记类型是可被脚本运行时发现和挂载的脚本组件。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ScriptComponentAttribute : Attribute
{
}
