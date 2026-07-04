namespace PixelEngine.UI;

/// <summary>
/// UI 文档句柄。
/// </summary>
/// <param name="Value">正整数句柄值。</param>
public readonly record struct UiDocumentHandle(int Value)
{
    /// <summary>
    /// 校验句柄有效性。
    /// </summary>
    public void Validate()
    {
        if (Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), "UI 文档句柄必须为正数。");
        }
    }
}

/// <summary>
/// 可见屏幕实例句柄。
/// </summary>
/// <param name="Value">正整数句柄值。</param>
public readonly record struct UiScreenHandle(int Value);

/// <summary>
/// 稳定屏幕 id。
/// </summary>
/// <param name="Value">正整数 id。</param>
public readonly record struct UiScreenId(int Value)
{
    /// <summary>
    /// 校验 id 有效性。
    /// </summary>
    public void Validate()
    {
        if (Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), "UI 屏幕 id 必须为正数。");
        }
    }
}

/// <summary>
/// UI 元素 id。
/// </summary>
/// <param name="Value">稳定 hash 或句柄。</param>
public readonly record struct UiElementId(int Value);

/// <summary>
/// UI 动作 id。
/// </summary>
/// <param name="Value">稳定 hash 或句柄。</param>
public readonly record struct UiActionId(int Value);

/// <summary>
/// UI 模型路径 id。
/// </summary>
/// <param name="Value">稳定 hash 或句柄。</param>
public readonly record struct UiPathId(int Value);

/// <summary>
/// UI 字符串池句柄。
/// </summary>
/// <param name="Value">字符串池索引。</param>
public readonly record struct UiStringHandle(int Value);
